// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class UserSessionSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		var statements = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"User session schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach (var statement in statements)
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
		transaction.Commit();
	}

	public static IReadOnlyList<string> GetPolicyMigrationStatements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => SqlitePolicy,
		DatabaseProvider.SqlServer => SqlServerPolicy,
		DatabaseProvider.MySql => MySqlPolicy,
		_ => throw new NotSupportedException($"User session policy schema is not supported for provider '{provider}'.")
	};

	private static readonly string[] Sqlite =
	[
		"CREATE TABLE IF NOT EXISTS UserSessions (Id INTEGER PRIMARY KEY AUTOINCREMENT, SessionId TEXT NOT NULL UNIQUE, UserId INTEGER NOT NULL, StartedUtc TEXT NOT NULL, LastSeenUtc TEXT NOT NULL, LastActivityUtc TEXT NULL, EndedUtc TEXT NULL, EndReason INTEGER NULL, ClientInstanceId TEXT NOT NULL, MachineName TEXT NULL, AppVersion TEXT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(UserId) REFERENCES Users(Id));",
		"CREATE INDEX IF NOT EXISTS IX_UserSessions_UserId ON UserSessions(UserId);",
		"CREATE INDEX IF NOT EXISTS IX_UserSessions_Presence ON UserSessions(EndedUtc, LastSeenUtc);"
	];

	private static readonly string[] SqlServer =
	[
		"IF OBJECT_ID(N'UserSessions', N'U') IS NULL CREATE TABLE UserSessions (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY, SessionId nvarchar(36) NOT NULL CONSTRAINT UQ_UserSessions_SessionId UNIQUE, UserId bigint NOT NULL, StartedUtc nvarchar(40) NOT NULL, LastSeenUtc nvarchar(40) NOT NULL, LastActivityUtc nvarchar(40) NULL, EndedUtc nvarchar(40) NULL, EndReason int NULL, ClientInstanceId nvarchar(36) NOT NULL, MachineName nvarchar(255) NULL, AppVersion nvarchar(100) NULL, Version bigint NOT NULL CONSTRAINT DF_UserSessions_Version DEFAULT 1, CONSTRAINT FK_UserSessions_Users FOREIGN KEY(UserId) REFERENCES Users(Id));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserSessions_UserId' AND object_id = OBJECT_ID(N'UserSessions')) CREATE INDEX IX_UserSessions_UserId ON UserSessions(UserId);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserSessions_Presence' AND object_id = OBJECT_ID(N'UserSessions')) CREATE INDEX IX_UserSessions_Presence ON UserSessions(EndedUtc, LastSeenUtc);"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE IF NOT EXISTS UserSessions (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, SessionId VARCHAR(36) NOT NULL UNIQUE, UserId BIGINT NOT NULL, StartedUtc VARCHAR(40) NOT NULL, LastSeenUtc VARCHAR(40) NOT NULL, LastActivityUtc VARCHAR(40) NULL, EndedUtc VARCHAR(40) NULL, EndReason INT NULL, ClientInstanceId VARCHAR(36) NOT NULL, MachineName VARCHAR(255) NULL, AppVersion VARCHAR(100) NULL, Version BIGINT NOT NULL DEFAULT 1, INDEX IX_UserSessions_UserId(UserId), INDEX IX_UserSessions_Presence(EndedUtc, LastSeenUtc), CONSTRAINT FK_UserSessions_Users FOREIGN KEY(UserId) REFERENCES Users(Id)) ENGINE=InnoDB;"
	];

	private static readonly string[] SqlitePolicy =
	[
		"CREATE TABLE IF NOT EXISTS UserSessionPolicy (Id INTEGER PRIMARY KEY CHECK(Id = 1), IdleTimeoutMinutes INTEGER NOT NULL, MaximumSessionAgeHours INTEGER NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1);",
		"INSERT OR IGNORE INTO UserSessionPolicy (Id, IdleTimeoutMinutes, MaximumSessionAgeHours, UpdatedUtc, Version) VALUES (1, 30, 12, '1970-01-01T00:00:00.0000000Z', 1);"
	];

	private static readonly string[] SqlServerPolicy =
	[
		"IF OBJECT_ID(N'UserSessionPolicy', N'U') IS NULL CREATE TABLE UserSessionPolicy (Id bigint NOT NULL CONSTRAINT PK_UserSessionPolicy PRIMARY KEY CONSTRAINT CK_UserSessionPolicy_Singleton CHECK (Id = 1), IdleTimeoutMinutes int NOT NULL, MaximumSessionAgeHours int NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_UserSessionPolicy_Version DEFAULT 1);",
		"IF NOT EXISTS (SELECT 1 FROM UserSessionPolicy WHERE Id = 1) INSERT INTO UserSessionPolicy (Id, IdleTimeoutMinutes, MaximumSessionAgeHours, UpdatedUtc, Version) VALUES (1, 30, 12, N'1970-01-01T00:00:00.0000000Z', 1);"
	];

	private static readonly string[] MySqlPolicy =
	[
		"CREATE TABLE IF NOT EXISTS UserSessionPolicy (Id BIGINT NOT NULL PRIMARY KEY, IdleTimeoutMinutes INT NOT NULL, MaximumSessionAgeHours INT NOT NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT CK_UserSessionPolicy_Singleton CHECK (Id = 1)) ENGINE=InnoDB;",
		"INSERT IGNORE INTO UserSessionPolicy (Id, IdleTimeoutMinutes, MaximumSessionAgeHours, UpdatedUtc, Version) VALUES (1, 30, 12, '1970-01-01T00:00:00.0000000Z', 1);"
	];
}
