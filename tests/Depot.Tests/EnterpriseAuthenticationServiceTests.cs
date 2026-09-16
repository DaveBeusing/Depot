// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class EnterpriseAuthenticationServiceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-enterprise-auth-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task SuccessfulEnterpriseSignInUsesExistingLinkAndLocalRbac()
	{
		var context = await CreateContextAsync();
		var target = await context.CreateUserAsync("linked@example.test", "Linked User");
		await context.AssignRoleAsync(target.Id, "enterprise-user", ApplicationPermission.DashboardView);
		await context.Identity.CreateProviderAsync(
			"entra-main",
			EnterpriseIdentityProviderKind.MicrosoftEntraId,
			"Microsoft Entra ID",
			"https://login.microsoftonline.com/tenant-a/v2.0",
			"depot-client",
			"tenant-a",
			true);
		var identity = new ValidatedExternalIdentity(
			"entra-main",
			"https://login.microsoftonline.com/tenant-a/v2.0",
			"subject-1",
			"tenant-a",
			"external@example.test",
			"External Name");
		await context.Identity.LinkAsync(target.Id, identity);
		context.Authorization.SignOut();
		var service = context.CreateAuthentication(new FakeOidcClient(OidcAuthenticationResult.Success(identity)));

		var result = await service.SignInAsync("entra-main");

		Assert.True(result.Succeeded);
		Assert.NotNull(context.Authorization.CurrentUser);
		Assert.Equal(target.Id, context.Authorization.CurrentUser!.Id);
		Assert.Equal("linked@example.test", context.Authorization.CurrentUser.Email);
		Assert.Contains(ApplicationPermission.DashboardView, context.Authorization.EffectivePermissions);
		Assert.DoesNotContain(ApplicationPermission.UsersManage, context.Authorization.EffectivePermissions);
		var resolution = await context.Identity.ResolveAsync(identity);
		Assert.NotNull(resolution);
		Assert.NotNull(resolution!.Link.LastSeenUtc);
		Assert.Equal("external@example.test", resolution.Link.Email);
	}

	[Fact]
	public async Task ValidButUnlinkedIdentityFailsClosedAndWritesSecurityEvidence()
	{
		var context = await CreateContextAsync();
		await context.Identity.CreateProviderAsync(
			"oidc-main",
			EnterpriseIdentityProviderKind.OpenIdConnect,
			"OIDC Main",
			"https://issuer.example.test",
			"depot-client",
			null,
			true);
		context.Authorization.SignOut();
		var identity = new ValidatedExternalIdentity("oidc-main", "https://issuer.example.test", "unlinked-subject");
		var service = context.CreateAuthentication(new FakeOidcClient(OidcAuthenticationResult.Success(identity)));

		var result = await service.SignInAsync("oidc-main");

		Assert.Equal(EnterpriseSignInStatus.IdentityNotLinked, result.Status);
		Assert.Null(context.Authorization.CurrentUser);
		var evidence = await context.Access.QuerySingleOrDefaultAsync(
			"SELECT Summary FROM SecurityEvents WHERE AccountIdentifier = $Account ORDER BY Id DESC;",
			reader => reader.GetString(0),
			CancellationToken.None,
			new DatabaseParameter("$Account", "enterprise:oidc-main"));
		Assert.Equal("Enterprise authentication failed", evidence);
	}

	[Fact]
	public async Task LoginProviderListExcludesDisabledAndUnboundEntraProviders()
	{
		var context = await CreateContextAsync();
		await context.Identity.CreateProviderAsync("oidc", EnterpriseIdentityProviderKind.OpenIdConnect, "OIDC", "https://oidc.example.test", "client-1", null, true);
		await context.Identity.CreateProviderAsync("disabled", EnterpriseIdentityProviderKind.OpenIdConnect, "Disabled", "https://disabled.example.test", "client-2", null, false);
		await context.Identity.CreateProviderAsync("entra-unbound", EnterpriseIdentityProviderKind.MicrosoftEntraId, "Unbound Entra", "https://login.microsoftonline.com/common/v2.0", "client-3", null, true);
		await context.Identity.CreateProviderAsync("entra-bound", EnterpriseIdentityProviderKind.MicrosoftEntraId, "Bound Entra", "https://login.microsoftonline.com/tenant-a/v2.0", "client-4", "tenant-a", true);
		context.Authorization.SignOut();
		var service = context.CreateAuthentication(new FakeOidcClient(OidcAuthenticationResult.Failed("unused")));

		var providers = await service.ListLoginProvidersAsync();

		Assert.Equal(2, providers.Count);
		Assert.Contains(providers, provider => provider.Code == "oidc");
		Assert.Contains(providers, provider => provider.Code == "entra-bound");
		Assert.DoesNotContain(providers, provider => provider.Code == "disabled");
		Assert.DoesNotContain(providers, provider => provider.Code == "entra-unbound");
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(access);
		var users = new UserRepository(access);
		var roles = new RoleRepository(access);
		var identities = new EnterpriseIdentityRepository(access);
		var auditEntries = new AuditRepository(access);
		var securityEvents = new SecurityEventRepository(access);
		var authorization = new AuthorizationService();
		var admin = new User
		{
			Email = "enterprise-admin@example.test",
			DisplayName = "Enterprise Admin",
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};
		admin.Id = await users.CreateAsync(admin, "unused", CancellationToken.None);
		authorization.SignIn(admin, [ApplicationPermission.UsersView, ApplicationPermission.UsersManage]);
		var audit = new AuditService(auditEntries, authorization);
		var identity = new EnterpriseIdentityService(transactions, identities, users, roles, auditEntries, audit, authorization);
		var securityEventService = new SecurityEventService(securityEvents, authorization);
		var session = new SessionService(authorization);
		return new TestContext(access, transactions, users, roles, identities, securityEvents, authorization, identity, securityEventService, session);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed class FakeOidcClient(OidcAuthenticationResult result) : IOpenIdConnectAuthenticationClient
	{
		public Task<OidcAuthenticationResult> AuthenticateAsync(EnterpriseIdentityProvider provider, CancellationToken cancellationToken) =>
			Task.FromResult(result);
	}

	private sealed record TestContext(
		DatabaseAccess Access,
		IDatabaseTransactionRunner Transactions,
		UserRepository Users,
		RoleRepository Roles,
		EnterpriseIdentityRepository Identities,
		SecurityEventRepository SecurityEvents,
		AuthorizationService Authorization,
		EnterpriseIdentityService Identity,
		SecurityEventService SecurityEventService,
		SessionService Session)
	{
		public EnterpriseAuthenticationService CreateAuthentication(IOpenIdConnectAuthenticationClient client) =>
			new(Identities, Identity, client, Session, Authorization, SecurityEventService, SecurityEvents);

		public async Task<User> CreateUserAsync(string email, string displayName)
		{
			var user = new User { Email = email, DisplayName = displayName, IsActive = true, CreatedUtc = DateTime.UtcNow };
			user.Id = await Users.CreateAsync(user, "unused", CancellationToken.None);
			return user;
		}

		public Task AssignRoleAsync(long userId, string code, ApplicationPermission permission) =>
			Transactions.ExecuteAsync(async (transaction, token) =>
			{
				var role = new Role { Code = code, Name = code, Description = "Enterprise auth test role", IsActive = true };
				role.Id = await RoleRepository.CreateAsync(transaction, role, token);
				await RoleRepository.ReplacePermissionsAsync(transaction, role.Id, [permission], token);
				await RoleRepository.ReplaceUserRolesAsync(transaction, userId, [role.Id], token);
				return true;
			});
	}
}
