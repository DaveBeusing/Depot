// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class UserSessionLifecycleTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-user-session-lifecycle-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task SuccessfulLoginCreatesSessionHeartbeatAdvancesAndLogoutEndsIt()
	{
		var context = await CreateContextAsync();
		Assert.True(await context.Authentication.SignInAsync(context.Email, "Correct-Password-42!", CancellationToken.None));
		var sessionId = Assert.IsType<Guid>(context.Session.CurrentSessionId);
		var created = await context.Repository.GetBySessionIdAsync(sessionId, CancellationToken.None);
		Assert.NotNull(created);

		context.Clock.UtcNow = context.Clock.UtcNow.AddSeconds(30);
		Assert.True(await context.Session.TrySendHeartbeatAsync());
		var heartbeat = await context.Repository.GetBySessionIdAsync(sessionId, CancellationToken.None);
		Assert.Equal(context.Clock.UtcNow, heartbeat!.LastSeenUtc);

		await context.Session.LogoutAsync();
		var ended = await context.Repository.GetBySessionIdAsync(sessionId, CancellationToken.None);
		Assert.Equal(UserSessionEndReason.LoggedOut, ended!.EndReason);
		Assert.NotNull(ended.EndedUtc);
		Assert.False(await context.Session.TrySendHeartbeatAsync());
		context.Session.Dispose();
	}

	[Fact]
	public async Task FailedLoginCreatesNoSessionAndGracefulShutdownEndsSuccessfulSession()
	{
		var context = await CreateContextAsync();
		Assert.False(await context.Authentication.SignInAsync(context.Email, "wrong", CancellationToken.None));
		Assert.Null(context.Session.CurrentSessionId);
		Assert.Equal(0, await context.Repository.CountActiveSessionsAsync(context.Clock.UtcNow.AddMinutes(-5), CancellationToken.None));

		Assert.True(await context.Authentication.SignInAsync(context.Email, "Correct-Password-42!", CancellationToken.None));
		var sessionId = Assert.IsType<Guid>(context.Session.CurrentSessionId);
		await context.Session.CloseApplicationAsync();
		var ended = await context.Repository.GetBySessionIdAsync(sessionId, CancellationToken.None);
		Assert.Equal(UserSessionEndReason.ApplicationClosed, ended!.EndReason);
		context.Session.Dispose();
	}

	[Fact]
	public async Task HeartbeatDatabaseFailureIsContained()
	{
		var context = await CreateContextAsync();
		Assert.True(await context.Authentication.SignInAsync(context.Email, "Correct-Password-42!", CancellationToken.None));
		using (var connection = Open())
		{
			using var command = connection.CreateCommand();
			command.CommandText = "DROP TABLE UserSessions;";
			command.ExecuteNonQuery();
		}

		Assert.False(await context.Session.TrySendHeartbeatAsync());
		context.Session.Dispose();
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var users = new UserRepository(access);
		var roles = new RoleRepository(access);
		var sessions = new UserSessionRepository(access);
		var passwordHasher = new PasswordHasher();
		var email = $"session-{Guid.NewGuid():N}@test.local";
		var user = new User
		{
			Email = email,
			DisplayName = "Session Test User",
			IsActive = true,
			CreatedUtc = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc)
		};
		await users.CreateAsync(user, passwordHasher.Hash("Correct-Password-42!"), CancellationToken.None);

		var authorization = new AuthorizationService();
		var clock = new MutableTimeProvider { UtcNow = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc) };
		var session = new SessionService(authorization);
		session.Configure(sessions, new UserSessionClientInfo(Guid.NewGuid(), "TEST-CLIENT", "0.15.80-preview"), clock);
		var authentication = new AuthenticationService(users, roles, passwordHasher, authorization);
		authentication.ConfigureSession(session);
		return new TestContext(email, authentication, session, sessions, clock);
	}

	private SqliteConnection Open()
	{
		var connection = new SqliteConnection($"Data Source={_path}");
		connection.Open();
		return connection;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		string Email,
		AuthenticationService Authentication,
		SessionService Session,
		UserSessionRepository Repository,
		MutableTimeProvider Clock);

	private sealed class MutableTimeProvider : TimeProvider
	{
		public DateTime UtcNow { get; set; }
		public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
	}
}
