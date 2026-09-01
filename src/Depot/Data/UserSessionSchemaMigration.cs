// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public static class UserSessionSchemaMigration
{
	public const int CurrentVersion = 3;
	private const string FeatureName = "UserSessions";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		EnsureVersionTable(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion)
			throw new InvalidOperationException($"User session schema version '{version}' is newer than the supported version '{CurrentVersion}'.");

		if (version == 0)
		{
			UserSessionSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 1);
			version = 1;
		}

		if (version == 1)
		{
			Migrate(connectionFactory, UserSessionSchema.GetPolicyMigrationStatements(connectionFactory.Provider), 2);
			version = 2;
		}

		if (version == 2)
		{
			Migrate(connectionFactory, UserSessionSchema.GetPolicyV3MigrationStatements(connectionFactory.Provider), 3);
			version = 3;
		}

		if (version != CurrentVersion)
			throw new InvalidOperationException($"User session schema version '{version}' is not supported. Expected '{CurrentVersion}'.");
	}

	private static void Migrate(IDatabaseConnectionFactory connectionFactory, IReadOnlyList<string> statements, int targetVersion)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		foreach (var statement in statements)
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
		command.CommandText = VersionUpsertSql(connectionFactory.Provider, targetVersion);
		command.ExecuteNonQuery();
		transaction.Commit();
	}

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
			_ => throw new NotSupportedException($"User session migrations are not supported for provider '{connectionFactory.Provider}'.")
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

	private static void WriteVersion(IDatabaseConnectionFactory connectionFactory, int version)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = VersionUpsertSql(connectionFactory.Provider, version);
		command.ExecuteNonQuery();
	}

	private static string VersionUpsertSql(DatabaseProvider provider, int version) => provider switch
	{
		DatabaseProvider.Local => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;",
		DatabaseProvider.SqlServer => $"IF EXISTS (SELECT 1 FROM DepotFeatureVersions WHERE Name=N'{FeatureName}') UPDATE DepotFeatureVersions SET Version={version} WHERE Name=N'{FeatureName}'; ELSE INSERT INTO DepotFeatureVersions (Name, Version) VALUES (N'{FeatureName}', {version});",
		DatabaseProvider.MySql => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON DUPLICATE KEY UPDATE Version=VALUES(Version);",
		_ => throw new NotSupportedException($"User session migrations are not supported for provider '{provider}'.")
	};
}
