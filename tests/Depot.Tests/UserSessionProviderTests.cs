// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
public sealed class UserSessionProviderTests
{
	[SqlServerProcurementFact]
	public async Task SqlServerMigratesAndPersistsUserSessions()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new SqlServerDatabase(factory));
	}

	[MySqlProcurementFact]
	public async Task MySqlOrMariaDbMigratesAndPersistsUserSessions()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyProviderContractAsync(
		IDatabaseConnectionFactory factory,
		IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		var data = new DatabaseAccess(factory);
		var users = new UserRepository(data);
		var sessions = new UserSessionRepository(data);
		var now = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);
		var user = new User
		{
			Email = $"session-provider-{factory.Provider}-{Guid.NewGuid():N}@test.local",
			DisplayName = "Provider Session Test",
			IsActive = true,
			CreatedUtc = now
		};
		var userId = await users.CreateAsync(user, "unused", CancellationToken.None);
		var session = new UserSession
		{
			SessionId = Guid.NewGuid(),
			UserId = userId,
			StartedUtc = now,
			LastSeenUtc = now,
			ClientInstanceId = Guid.NewGuid(),
			MachineName = $"{factory.Provider}-CLIENT",
			AppVersion = "0.15.82-preview"
		};

		await sessions.CreateAsync(session, CancellationToken.None);
		var persisted = await sessions.GetBySessionIdAsync(session.SessionId, CancellationToken.None);
		Assert.NotNull(persisted);
		Assert.Equal(session.SessionId, persisted!.SessionId);
		Assert.Equal(userId, persisted.UserId);

		Assert.True(await sessions.UpdateHeartbeatAsync(session.SessionId, now.AddSeconds(30), CancellationToken.None));
		Assert.True(await sessions.EndAsync(session.SessionId, now.AddSeconds(40), UserSessionEndReason.LoggedOut, CancellationToken.None));
		Assert.False(await sessions.UpdateHeartbeatAsync(session.SessionId, now.AddSeconds(60), CancellationToken.None));
		var ended = await sessions.GetBySessionIdAsync(session.SessionId, CancellationToken.None);
		Assert.Equal(UserSessionEndReason.LoggedOut, ended!.EndReason);
	}
}
