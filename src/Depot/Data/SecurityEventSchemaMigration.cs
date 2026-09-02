// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public static class SecurityEventSchemaMigration
{
	public const int CurrentVersion = 2;
	private const string FeatureName = "SecurityEvents";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		EnsureVersionTable(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion) throw new InvalidOperationException($"Security event schema version '{version}' is newer than the supported version '{CurrentVersion}'.");
		if (version == 0) { Migrate(connectionFactory, Version1Statements(connectionFactory.Provider), 1); version = 1; }
		if (version == 1) { Migrate(connectionFactory, Version2Statements(connectionFactory.Provider), 2); version = 2; }
		if (version != CurrentVersion) throw new InvalidOperationException($"Security event schema version '{version}' is not supported. Expected '{CurrentVersion}'.");
	}

	private static void Migrate(IDatabaseConnectionFactory connectionFactory, IReadOnlyList<string> statements, int targetVersion)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		foreach (var statement in statements) { command.CommandText = statement; command.ExecuteNonQuery(); }
		command.CommandText = VersionUpsertSql(connectionFactory.Provider, targetVersion);
		command.ExecuteNonQuery();
		transaction.Commit();
	}

	private static IReadOnlyList<string> Version1Statements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => SqliteV1,
		DatabaseProvider.SqlServer => SqlServerV1,
		DatabaseProvider.MySql => MySqlV1,
		_ => throw new NotSupportedException($"Security event schema is not supported for provider '{provider}'.")
	};

	private static IReadOnlyList<string> Version2Statements(DatabaseProvider provider) => provider switch
	{
		DatabaseProvider.Local => SqliteV2,
		DatabaseProvider.SqlServer => SqlServerV2,
		DatabaseProvider.MySql => MySqlV2,
		_ => throw new NotSupportedException($"Security event schema v2 is not supported for provider '{provider}'.")
	};

	private static void EnsureVersionTable(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name TEXT PRIMARY KEY, Version INTEGER NOT NULL);",
			DatabaseProvider.SqlServer => "IF OBJECT_ID(N'DepotFeatureVersions', N'U') IS NULL CREATE TABLE DepotFeatureVersions (Name nvarchar(100) NOT NULL PRIMARY KEY, Version int NOT NULL);",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name VARCHAR(100) NOT NULL PRIMARY KEY, Version INT NOT NULL);",
			_ => throw new NotSupportedException()
		};
		command.ExecuteNonQuery();
	}

	private static int ReadVersion(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT Version FROM DepotFeatureVersions WHERE Name='{FeatureName}';";
		var value = command.ExecuteScalar();
		return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	private static string VersionUpsertSql(DatabaseProvider provider, int version) => provider switch
	{
		DatabaseProvider.Local => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;",
		DatabaseProvider.SqlServer => $"IF EXISTS (SELECT 1 FROM DepotFeatureVersions WHERE Name=N'{FeatureName}') UPDATE DepotFeatureVersions SET Version={version} WHERE Name=N'{FeatureName}'; ELSE INSERT INTO DepotFeatureVersions (Name, Version) VALUES (N'{FeatureName}', {version});",
		DatabaseProvider.MySql => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON DUPLICATE KEY UPDATE Version=VALUES(Version);",
		_ => throw new NotSupportedException()
	};

	private static readonly string[] SqliteV1 =
	[
		"CREATE TABLE IF NOT EXISTS SecurityEvents (Id INTEGER PRIMARY KEY AUTOINCREMENT, TimestampUtc TEXT NOT NULL, EventType INTEGER NOT NULL, Severity INTEGER NOT NULL, UserId INTEGER NULL, AccountIdentifier TEXT NULL, SessionId TEXT NULL, MachineName TEXT NULL, Summary TEXT NOT NULL, Details TEXT NULL, ReviewedUtc TEXT NULL, ReviewedByUserId INTEGER NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(UserId) REFERENCES Users(Id), FOREIGN KEY(ReviewedByUserId) REFERENCES Users(Id));",
		"CREATE INDEX IF NOT EXISTS IX_SecurityEvents_TimestampSeverity ON SecurityEvents(TimestampUtc, Severity);",
		"CREATE INDEX IF NOT EXISTS IX_SecurityEvents_UserId ON SecurityEvents(UserId);",
		"CREATE INDEX IF NOT EXISTS IX_SecurityEvents_Review ON SecurityEvents(ReviewedUtc, Severity);"
	];

	private static readonly string[] SqlServerV1 =
	[
		"IF OBJECT_ID(N'SecurityEvents', N'U') IS NULL CREATE TABLE SecurityEvents (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SecurityEvents PRIMARY KEY, TimestampUtc nvarchar(40) NOT NULL, EventType int NOT NULL, Severity int NOT NULL, UserId bigint NULL, AccountIdentifier nvarchar(320) NULL, SessionId nvarchar(36) NULL, MachineName nvarchar(255) NULL, Summary nvarchar(500) NOT NULL, Details nvarchar(2000) NULL, ReviewedUtc nvarchar(40) NULL, ReviewedByUserId bigint NULL, Version bigint NOT NULL CONSTRAINT DF_SecurityEvents_Version DEFAULT 1, CONSTRAINT FK_SecurityEvents_User FOREIGN KEY(UserId) REFERENCES Users(Id), CONSTRAINT FK_SecurityEvents_ReviewedBy FOREIGN KEY(ReviewedByUserId) REFERENCES Users(Id));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_SecurityEvents_TimestampSeverity' AND object_id=OBJECT_ID(N'SecurityEvents')) CREATE INDEX IX_SecurityEvents_TimestampSeverity ON SecurityEvents(TimestampUtc, Severity);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_SecurityEvents_UserId' AND object_id=OBJECT_ID(N'SecurityEvents')) CREATE INDEX IX_SecurityEvents_UserId ON SecurityEvents(UserId);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_SecurityEvents_Review' AND object_id=OBJECT_ID(N'SecurityEvents')) CREATE INDEX IX_SecurityEvents_Review ON SecurityEvents(ReviewedUtc, Severity);"
	];

	private static readonly string[] MySqlV1 =
	[
		"CREATE TABLE IF NOT EXISTS SecurityEvents (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, TimestampUtc VARCHAR(40) NOT NULL, EventType INT NOT NULL, Severity INT NOT NULL, UserId BIGINT NULL, AccountIdentifier VARCHAR(320) NULL, SessionId VARCHAR(36) NULL, MachineName VARCHAR(255) NULL, Summary VARCHAR(500) NOT NULL, Details VARCHAR(2000) NULL, ReviewedUtc VARCHAR(40) NULL, ReviewedByUserId BIGINT NULL, Version BIGINT NOT NULL DEFAULT 1, INDEX IX_SecurityEvents_TimestampSeverity(TimestampUtc, Severity), INDEX IX_SecurityEvents_UserId(UserId), INDEX IX_SecurityEvents_Review(ReviewedUtc, Severity), CONSTRAINT FK_SecurityEvents_User FOREIGN KEY(UserId) REFERENCES Users(Id), CONSTRAINT FK_SecurityEvents_ReviewedBy FOREIGN KEY(ReviewedByUserId) REFERENCES Users(Id)) ENGINE=InnoDB;"
	];

	private static readonly string[] SqliteV2 =
	[
		"ALTER TABLE SecurityEvents ADD COLUMN ClientInstanceId TEXT NULL;",
		"CREATE INDEX IF NOT EXISTS IX_SecurityEvents_SessionClient ON SecurityEvents(SessionId, ClientInstanceId);",
		"CREATE TABLE AuthenticationSecurityPolicy (Id INTEGER PRIMARY KEY CHECK(Id = 1), FailureWindowMinutes INTEGER NOT NULL, LockoutThreshold INTEGER NOT NULL, LockoutDurationMinutes INTEGER NOT NULL, SecurityEventRetentionDays INTEGER NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1);",
		"INSERT INTO AuthenticationSecurityPolicy (Id, FailureWindowMinutes, LockoutThreshold, LockoutDurationMinutes, SecurityEventRetentionDays, UpdatedUtc, Version) VALUES (1,15,5,15,365,'1970-01-01T00:00:00.0000000Z',1);",
		"CREATE TABLE AuthenticationThrottle (AccountKey TEXT PRIMARY KEY, FirstFailureUtc TEXT NOT NULL, FailureCount INTEGER NOT NULL, BlockedUntilUtc TEXT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1);",
		"CREATE INDEX IF NOT EXISTS IX_AuthenticationThrottle_Updated ON AuthenticationThrottle(UpdatedUtc);"
	];

	private static readonly string[] SqlServerV2 =
	[
		"ALTER TABLE SecurityEvents ADD ClientInstanceId nvarchar(36) NULL;",
		"CREATE INDEX IX_SecurityEvents_SessionClient ON SecurityEvents(SessionId, ClientInstanceId);",
		"CREATE TABLE AuthenticationSecurityPolicy (Id bigint NOT NULL CONSTRAINT PK_AuthenticationSecurityPolicy PRIMARY KEY CONSTRAINT CK_AuthenticationSecurityPolicy_Singleton CHECK (Id=1), FailureWindowMinutes int NOT NULL, LockoutThreshold int NOT NULL, LockoutDurationMinutes int NOT NULL, SecurityEventRetentionDays int NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_AuthenticationSecurityPolicy_Version DEFAULT 1);",
		"INSERT INTO AuthenticationSecurityPolicy (Id, FailureWindowMinutes, LockoutThreshold, LockoutDurationMinutes, SecurityEventRetentionDays, UpdatedUtc, Version) VALUES (1,15,5,15,365,N'1970-01-01T00:00:00.0000000Z',1);",
		"CREATE TABLE AuthenticationThrottle (AccountKey nvarchar(320) NOT NULL CONSTRAINT PK_AuthenticationThrottle PRIMARY KEY, FirstFailureUtc nvarchar(40) NOT NULL, FailureCount int NOT NULL, BlockedUntilUtc nvarchar(40) NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_AuthenticationThrottle_Version DEFAULT 1);",
		"CREATE INDEX IX_AuthenticationThrottle_Updated ON AuthenticationThrottle(UpdatedUtc);"
	];

	private static readonly string[] MySqlV2 =
	[
		"ALTER TABLE SecurityEvents ADD COLUMN ClientInstanceId VARCHAR(36) NULL;",
		"CREATE INDEX IX_SecurityEvents_SessionClient ON SecurityEvents(SessionId, ClientInstanceId);",
		"CREATE TABLE AuthenticationSecurityPolicy (Id BIGINT NOT NULL PRIMARY KEY, FailureWindowMinutes INT NOT NULL, LockoutThreshold INT NOT NULL, LockoutDurationMinutes INT NOT NULL, SecurityEventRetentionDays INT NOT NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT CK_AuthenticationSecurityPolicy_Singleton CHECK (Id=1)) ENGINE=InnoDB;",
		"INSERT INTO AuthenticationSecurityPolicy (Id, FailureWindowMinutes, LockoutThreshold, LockoutDurationMinutes, SecurityEventRetentionDays, UpdatedUtc, Version) VALUES (1,15,5,15,365,'1970-01-01T00:00:00.0000000Z',1);",
		"CREATE TABLE AuthenticationThrottle (AccountKey VARCHAR(320) NOT NULL PRIMARY KEY, FirstFailureUtc VARCHAR(40) NOT NULL, FailureCount INT NOT NULL, BlockedUntilUtc VARCHAR(40) NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, INDEX IX_AuthenticationThrottle_Updated(UpdatedUtc)) ENGINE=InnoDB;"
	];
}
