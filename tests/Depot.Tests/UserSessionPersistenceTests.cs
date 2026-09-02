// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class UserSessionPersistenceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-user-sessions-{Guid.NewGuid():N}.db");

	[Fact]
	public void NewDatabaseCreatesUserSessionFeatureSchema()
	{
		var factory = InitializeCore();
		UserSessionSchemaMigration.Migrate(factory);

		using var connection = Open();
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserSessions';"));
		Assert.Equal(UserSessionSchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='UserSessions';"));
	}

	[Fact]
	public void ExistingDatabaseMigratesUserSessionFeatureSchemaIdempotently()
	{
		var factory = InitializeCore();
		UserSessionSchemaMigration.Migrate(factory);
		UserSessionSchemaMigration.Migrate(factory);

		using var connection = Open();
		Assert.Equal(UserSessionSchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='UserSessions';"));
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_UserSessions_Presence';"));
	}

	[Fact]
	public async Task SessionCreationAndPresenceCutoffArePersistedCorrectly()
	{
		var factory = InitializeCore();
		UserSessionSchemaMigration.Migrate(factory);
		var repository = new UserSessionRepository(new DatabaseAccess(factory));
		var now = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);
		var session = NewSession(1, now);

		var id = await repository.CreateAsync(session, CancellationToken.None);
		var persisted = await repository.GetBySessionIdAsync(session.SessionId, CancellationToken.None);

		Assert.True(id > 0);
		Assert.NotNull(persisted);
		Assert.Equal(session.SessionId, persisted!.SessionId);
		Assert.Equal(session.ClientInstanceId, persisted.ClientInstanceId);
		Assert.Equal(1, await repository.CountActiveSessionsAsync(now - UserSessionPresenceOptions.Default.PresenceTimeout, CancellationToken.None));
		Assert.Equal(1, await repository.CountDistinctOnlineUsersAsync(now - UserSessionPresenceOptions.Default.PresenceTimeout, CancellationToken.None));
		Assert.Empty(await repository.GetActiveSessionsAsync(now.AddSeconds(1), null, CancellationToken.None));
	}

	[Fact]
	public async Task EndedOrExpiredSessionsAreOfflineAndMultiSessionCountsAreDistinct()
	{
		var factory = InitializeCore();
		UserSessionSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var repository = new UserSessionRepository(access);
		var now = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);
		var cutoff = now - UserSessionPresenceOptions.Default.PresenceTimeout;
		var first = NewSession(1, now);
		var second = NewSession(1, now.AddSeconds(-15));
		await repository.CreateAsync(first, CancellationToken.None);
		await repository.CreateAsync(second, CancellationToken.None);

		Assert.Equal(2, await repository.CountActiveSessionsAsync(cutoff, CancellationToken.None));
		Assert.Equal(1, await repository.CountDistinctOnlineUsersAsync(cutoff, CancellationToken.None));

		Assert.True(await repository.EndAsync(first.SessionId, now, UserSessionEndReason.LoggedOut, CancellationToken.None));
		Assert.False(await repository.UpdateHeartbeatAsync(first.SessionId, now.AddSeconds(30), CancellationToken.None));
		Assert.Equal(1, await repository.CountActiveSessionsAsync(cutoff, CancellationToken.None));

		await access.ExecuteAsync(
			"UPDATE UserSessions SET LastSeenUtc = $LastSeenUtc WHERE SessionId = $SessionId;",
			CancellationToken.None,
			new DatabaseParameter("$LastSeenUtc", now.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture)),
			new DatabaseParameter("$SessionId", second.SessionId.ToString("D", CultureInfo.InvariantCulture)));
		Assert.Equal(0, await repository.CountActiveSessionsAsync(cutoff, CancellationToken.None));
	}

	private SqliteConnectionFactory InitializeCore()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		return factory;
	}

	private UserSession NewSession(long userId, DateTime now) => new()
	{
		SessionId = Guid.NewGuid(),
		UserId = userId,
		StartedUtc = now,
		LastSeenUtc = now,
		ClientInstanceId = Guid.NewGuid(),
		MachineName = "TEST-CLIENT",
		AppVersion = "0.15.79-preview"
	};

	private SqliteConnection Open()
	{
		var connection = new SqliteConnection($"Data Source={_path}");
		connection.Open();
		return connection;
	}

	private static int Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
