// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class UserSessionAdmissionTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-session-admission-{Guid.NewGuid():N}.db");

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public async Task LocalLoginRejectsAccountChangesCommittedAfterAuthentication(int change)
	{
		await using var context = await CreateContextAsync();
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, change);
	}

	[Theory]
	[InlineData(EnterpriseIdentityProviderKind.OpenIdConnect, 0)]
	[InlineData(EnterpriseIdentityProviderKind.OpenIdConnect, 1)]
	[InlineData(EnterpriseIdentityProviderKind.MicrosoftEntraId, 0)]
	[InlineData(EnterpriseIdentityProviderKind.MicrosoftEntraId, 1)]
	public async Task EnterpriseLoginRejectsAccountChangesAfterIdentityResolution(EnterpriseIdentityProviderKind kind, int change)
	{
		await using var context = await CreateContextAsync();
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, change, kind);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public async Task AdmissionCommittedBeforeAccountChangeIsRevokedByThatChange(int change)
	{
		await using var context = await CreateContextAsync();
		await UserSessionAdmissionScenarios.RevokeAlreadyAdmittedSessionAsync(context, change);
	}

	[Theory]
	[InlineData(ConcurrentSessionLimitAction.RejectNewSession)]
	[InlineData(ConcurrentSessionLimitAction.SupersedeOldestSession)]
	public async Task SimultaneousLoginsPreserveTheConcurrentSessionPolicy(ConcurrentSessionLimitAction action)
	{
		await using var context = await CreateContextAsync();
		await UserSessionAdmissionScenarios.VerifyConcurrentLoginsAsync(context, action);
	}

	[Fact]
	public async Task StaleAdmissionCannotSupersedeAnAlreadyAuthenticatedCurrentRevision()
	{
		await using var context = await CreateContextAsync();
		await context.SetSingleSessionPolicyAsync(ConcurrentSessionLimitAction.SupersedeOldestSession);
		var user = await context.CreateUserAsync();
		await context.ChangeAccountAsync(user, 1);
		var current = context.CreateClient();
		Assert.True(await current.Authentication.SignInAsync(user.Email, UserSessionAdmissionTestContext.NewPassword, CancellationToken.None));
		var sessionId = Assert.IsType<Guid>(current.Session.CurrentSessionId);
		var stale = context.CreateClient();

		await Assert.ThrowsAsync<AuthenticationStateChangedException>(() =>
			stale.Session.StartAuthenticatedSessionAsync(user.Id, user.Version, CancellationToken.None));

		Assert.Equal(sessionId, Assert.Single(await context.Sessions.GetOpenSessionsForUserAsync(user.Id, CancellationToken.None)).SessionId);
		Assert.Null(stale.Session.CurrentSessionId);
		Assert.Equal(1, await context.CountSessionsAsync(user.Id));
	}

	[Fact]
	public async Task AdmissionRequiresAnExistingActiveAccountAndDoesNotChangeItsRevision()
	{
		await using var context = await CreateContextAsync();
		var client = context.CreateClient();
		await Assert.ThrowsAsync<AuthenticationStateChangedException>(() =>
			client.Session.StartAuthenticatedSessionAsync(long.MaxValue, 1, CancellationToken.None));
		var user = await context.CreateUserAsync();
		Assert.True(await client.Authentication.SignInAsync(user.Email, UserSessionAdmissionTestContext.Password, CancellationToken.None));
		Assert.Equal(user.Version, (await context.Users.GetByIdAsync(user.Id, CancellationToken.None))!.Version);
		Assert.Single(await context.Sessions.GetOpenSessionsForUserAsync(user.Id, CancellationToken.None));
	}

	[Fact]
	public async Task UnconfiguredSessionPersistenceCannotAdmitAnAccount()
	{
		using var session = new SessionService(new AuthorizationService());
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAuthenticatedSessionAsync(1, 1, CancellationToken.None));
		Assert.Null(session.CurrentSessionId);
	}

	private Task<UserSessionAdmissionTestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		return UserSessionAdmissionTestContext.CreateAsync(factory, new DepotDatabase(factory));
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}

internal static class UserSessionAdmissionScenarios
{
	internal static async Task RejectChangedAccountAsync(
		UserSessionAdmissionTestContext context,
		int change,
		EnterpriseIdentityProviderKind? externalKind = null)
	{
		var user = await context.CreateUserAsync();
		var barrier = new SessionAdmissionBarrier(context.Transactions);
		var client = context.CreateClient(barrier);
		EnterpriseSignInResult? externalResult = null;
		Task<bool> pending;
		if (externalKind is { } kind)
		{
			var (service, providerCode) = await context.CreateEnterpriseAuthenticationAsync(client, user, kind);
			pending = SignInExternalAsync();
			async Task<bool> SignInExternalAsync()
			{
				externalResult = await service.SignInAsync(providerCode);
				return externalResult.Succeeded;
			}
		}
		else
		{
			pending = client.Authentication.SignInAsync(user.Email, UserSessionAdmissionTestContext.Password, CancellationToken.None);
		}

		try
		{
			await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(20));
			await context.ChangeAccountAsync(user, change);
		}
		finally { barrier.Release(); }

		Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
		Assert.Null(client.Authorization.CurrentUser);
		Assert.Null(client.Session.CurrentSessionId);
		Assert.Equal(0, await context.CountSessionsAsync(user.Id));
		if (externalKind is not null)
		{
			Assert.Equal(EnterpriseSignInStatus.Failed, externalResult!.Status);
			Assert.Equal("account_state_changed", externalResult.FailureCode);
		}
	}

	internal static async Task RevokeAlreadyAdmittedSessionAsync(UserSessionAdmissionTestContext context, int change)
	{
		var user = await context.CreateUserAsync();
		var barrier = new SessionAdmissionBarrier(context.Transactions, pauseAfterCommit: true);
		var client = context.CreateClient(barrier);
		var pending = client.Authentication.SignInAsync(user.Email, UserSessionAdmissionTestContext.Password, CancellationToken.None);
		UserSession admitted;
		try
		{
			await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(20));
			admitted = Assert.Single(await context.Sessions.GetOpenSessionsForUserAsync(user.Id, CancellationToken.None));
			await context.ChangeAccountAsync(user, change);
		}
		finally { barrier.Release(); }

		Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
		var ended = await context.Sessions.GetBySessionIdAsync(admitted.SessionId, CancellationToken.None);
		Assert.NotNull(ended!.EndedUtc);
		Assert.Equal(change == 0 ? UserSessionEndReason.Revoked : UserSessionEndReason.CredentialsChanged, ended.EndReason);
		Assert.False(await client.Session.TrySendHeartbeatAsync());
		Assert.Null(client.Authorization.CurrentUser);
		Assert.Null(client.Session.CurrentSessionId);
		Assert.Empty(await context.Sessions.GetOpenSessionsForUserAsync(user.Id, CancellationToken.None));
	}

	internal static async Task VerifyConcurrentLoginsAsync(UserSessionAdmissionTestContext context, ConcurrentSessionLimitAction action)
	{
		await context.SetSingleSessionPolicyAsync(action);
		var user = await context.CreateUserAsync();
		var first = context.CreateClient();
		var second = context.CreateClient();
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstLogin = Task.Run(() => SignInAsync(first, firstReady));
		var secondLogin = Task.Run(() => SignInAsync(second, secondReady));
		await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(TimeSpan.FromSeconds(20));
		release.SetResult(true);
		var results = await Task.WhenAll(firstLogin, secondLogin).WaitAsync(TimeSpan.FromSeconds(30));

		var open = Assert.Single(await context.Sessions.GetOpenSessionsForUserAsync(user.Id, CancellationToken.None));
		Assert.Contains(open.SessionId, new[] { first.Session.CurrentSessionId, second.Session.CurrentSessionId });
		Assert.Equal(user.Version, (await context.Users.GetByIdAsync(user.Id, CancellationToken.None))!.Version);
		if (action == ConcurrentSessionLimitAction.RejectNewSession)
		{
			Assert.Single(results, value => value);
			var rejected = results[0] ? second : first;
			Assert.Null(rejected.Authorization.CurrentUser);
			Assert.Null(rejected.Session.CurrentSessionId);
			Assert.Equal(1, await context.CountSessionsAsync(user.Id));
		}
		else
		{
			Assert.All(results, result => Assert.True(result));
			Assert.Equal(2, await context.CountSessionsAsync(user.Id));
			var superseded = first.Session.CurrentSessionId == open.SessionId ? second : first;
			var supersededId = Assert.IsType<Guid>(superseded.Session.CurrentSessionId);
			Assert.Equal(UserSessionEndReason.Superseded, (await context.Sessions.GetBySessionIdAsync(supersededId, CancellationToken.None))!.EndReason);
			Assert.False(await superseded.Session.TrySendHeartbeatAsync());
			Assert.Null(superseded.Authorization.CurrentUser);
		}

		async Task<bool> SignInAsync(SessionAdmissionClient client, TaskCompletionSource<bool> ready)
		{
			ready.SetResult(true);
			await release.Task;
			return await client.Authentication.SignInAsync(user.Email, UserSessionAdmissionTestContext.Password, CancellationToken.None);
		}
	}
}

internal sealed class SessionAdmissionBarrier(IDatabaseTransactionRunner inner, bool pauseAfterCommit = false) : IDatabaseTransactionRunner
{
	private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
	internal Task Entered => _entered.Task;
	internal void Release() => _release.TrySetResult(true);

	public async Task<T> ExecuteAsync<T>(Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
	{
		if (pauseAfterCommit)
		{
			var result = await inner.ExecuteAsync(operation, cancellationToken);
			await PauseAsync(cancellationToken);
			return result;
		}
		await PauseAsync(cancellationToken);
		return await inner.ExecuteAsync(operation, cancellationToken);
	}

	private async Task PauseAsync(CancellationToken cancellationToken)
	{
		_entered.TrySetResult(true);
		await _release.Task.WaitAsync(cancellationToken);
	}
}

internal sealed record SessionAdmissionClient(AuthorizationService Authorization, SessionService Session, AuthenticationService Authentication);

internal sealed class UserSessionAdmissionTestContext : IAsyncDisposable
{
	internal const string Password = "Original-Password-42!";
	internal const string NewPassword = "Replacement-Password-73!";
	private readonly PasswordHasher _passwordHasher = new();
	private readonly RoleRepository _roles;
	private readonly SecurityEventRepository _securityEvents;
	private readonly EnterpriseIdentityRepository _identities;
	private readonly EnterpriseIdentityService _identityService;
	private readonly UserService _userService;
	private readonly long _roleId;
	private readonly UserSessionPolicy _originalPolicy;
	private readonly List<SessionAdmissionClient> _clients = [];
	private bool _policyChanged;

	private UserSessionAdmissionTestContext(DatabaseAccess access, IDatabaseTransactionRunner transactions,
		UserRepository users, RoleRepository roles, UserSessionRepository sessions,
		SecurityEventRepository securityEvents, EnterpriseIdentityRepository identities,
		EnterpriseIdentityService identityService, UserService userService, long roleId, UserSessionPolicy originalPolicy)
	{
		Access = access; Transactions = transactions; Users = users; _roles = roles; Sessions = sessions;
		_securityEvents = securityEvents; _identities = identities; _identityService = identityService;
		_userService = userService; _roleId = roleId; _originalPolicy = originalPolicy;
	}

	internal DatabaseAccess Access { get; }
	internal IDatabaseTransactionRunner Transactions { get; }
	internal UserRepository Users { get; }
	internal UserSessionRepository Sessions { get; }

	internal static async Task<UserSessionAdmissionTestContext> CreateAsync(IDatabaseConnectionFactory factory, IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		SecurityEventSchemaMigration.Migrate(factory);
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		var data = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(data);
		var users = new UserRepository(data);
		var roles = new RoleRepository(data);
		var sessions = new UserSessionRepository(data);
		var auditEntries = new AuditRepository(data);
		var securityEntries = new SecurityEventRepository(data);
		var identities = new EnterpriseIdentityRepository(data);
		var authorization = new AuthorizationService();
		var admin = new User { Email = $"admission-admin-{Guid.NewGuid():N}@test.local", DisplayName = "Admission Administrator", IsActive = true, CreatedUtc = DateTime.UtcNow };
		admin.Id = await users.CreateAsync(admin, "unused", CancellationToken.None);
		authorization.SignIn(admin, PermissionCatalog.All);
		var audit = new AuditService(auditEntries, authorization);
		var security = new SecurityEventService(securityEntries, authorization);
		var role = new Role { Code = $"admission-{Guid.NewGuid():N}", Name = "Admission test", IsActive = true };
		role.Id = await transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await RoleRepository.CreateAsync(transaction, role, token);
			await RoleRepository.ReplacePermissionsAsync(transaction, id, [ApplicationPermission.DashboardView], token);
			return id;
		});
		var userService = new UserService(transactions, users, roles, auditEntries, new PasswordHasher(), authorization, audit, securityEvents: security);
		var identity = new EnterpriseIdentityService(transactions, identities, users, roles, auditEntries, audit, authorization);
		return new UserSessionAdmissionTestContext(data, transactions, users, roles, sessions, securityEntries, identities, identity, userService, role.Id,
			await sessions.GetPolicyAsync(CancellationToken.None));
	}

	internal Task<User> CreateUserAsync() =>
		_userService.CreateUserAsync($"admission-{Guid.NewGuid():N}@test.local", "Admission User", Password, [_roleId], CancellationToken.None);

	internal SessionAdmissionClient CreateClient(IDatabaseTransactionRunner? transactions = null)
	{
		var authorization = new AuthorizationService();
		var security = new SecurityEventService(_securityEvents, authorization);
		var session = new SessionService(authorization);
		session.Configure(transactions ?? Transactions, Sessions, security, new UserSessionClientInfo(Guid.NewGuid(), "ADMISSION-CLIENT", "test"));
		var authentication = new AuthenticationService(Users, _roles, _passwordHasher, authorization, securityEvents: security);
		authentication.ConfigureSession(session);
		var client = new SessionAdmissionClient(authorization, session, authentication);
		_clients.Add(client);
		return client;
	}

	internal async Task<(EnterpriseAuthenticationService Service, string ProviderCode)> CreateEnterpriseAuthenticationAsync(
		SessionAdmissionClient client, User user, EnterpriseIdentityProviderKind kind)
	{
		var code = $"admission-{Guid.NewGuid():N}";
		var tenant = kind == EnterpriseIdentityProviderKind.MicrosoftEntraId ? "tenant-a" : null;
		var issuer = tenant is null ? "https://issuer.example.test" : "https://login.microsoftonline.com/tenant-a/v2.0";
		await _identityService.CreateProviderAsync(code, kind, "Admission identity", issuer, "depot-client", tenant, true);
		var identity = new ValidatedExternalIdentity(code, issuer, Guid.NewGuid().ToString("N"), tenant);
		await _identityService.LinkAsync(user.Id, identity);
		var security = new SecurityEventService(_securityEvents, client.Authorization);
		return (new EnterpriseAuthenticationService(_identities, _identityService, new SuccessfulOidcClient(identity), client.Session,
			client.Authorization, security, _securityEvents), code);
	}

	internal async Task ChangeAccountAsync(User user, int change)
	{
		var stored = await Users.GetByIdAsync(user.Id, CancellationToken.None) ?? throw new InvalidOperationException();
		if (change == 1)
		{
			await _userService.UpdateUserAsync(stored.Id, stored.Version, stored.Email, stored.DisplayName, NewPassword, [_roleId], CancellationToken.None);
		}
		else
		{
			var inactive = await _userService.SetActiveAsync(stored.Id, false, stored.Version, CancellationToken.None);
			if (change == 2) await _userService.SetActiveAsync(inactive.Id, true, inactive.Version, CancellationToken.None);
		}
	}

	internal async Task SetSingleSessionPolicyAsync(ConcurrentSessionLimitAction action)
	{
		var policy = await Sessions.GetPolicyAsync(CancellationToken.None);
		policy.ConcurrentSessionMode = ConcurrentSessionMode.SingleSession;
		policy.ConcurrentSessionLimitAction = action;
		policy.UpdatedUtc = DateTime.UtcNow;
		Assert.True(await Sessions.UpdatePolicyAsync(policy, policy.Version, CancellationToken.None));
		_policyChanged = true;
	}

	internal async Task<long> CountSessionsAsync(long userId) => Convert.ToInt64(await Access.ExecuteScalarAsync(
		"SELECT COUNT(*) FROM UserSessions WHERE UserId=$UserId;", CancellationToken.None, new DatabaseParameter("$UserId", userId)));

	public async ValueTask DisposeAsync()
	{
		foreach (var client in _clients)
		{
			try { await client.Session.CloseApplicationAsync(); }
			finally { client.Session.Dispose(); }
		}
		if (_policyChanged)
		{
			var current = await Sessions.GetPolicyAsync(CancellationToken.None);
			_originalPolicy.UpdatedUtc = DateTime.UtcNow;
			Assert.True(await Sessions.UpdatePolicyAsync(_originalPolicy, current.Version, CancellationToken.None));
		}
	}

	private sealed class SuccessfulOidcClient(ValidatedExternalIdentity identity) : IOpenIdConnectAuthenticationClient
	{
		public Task<OidcAuthenticationResult> AuthenticateAsync(EnterpriseIdentityProvider provider, CancellationToken cancellationToken) =>
			Task.FromResult(OidcAuthenticationResult.Success(identity));
	}
}
