// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public static class ApprovalPolicySchemaMigration
{
	public const int CurrentVersion = 1;
	private const string FeatureName = "ApprovalPolicies";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		EnsureVersionTable(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion)
			throw new InvalidOperationException($"Approval-policy schema version '{version}' is newer than the supported version '{CurrentVersion}'.");

		if (version == 0)
		{
			ApprovalPolicySchema.Ensure(connectionFactory);
			ApprovalPolicyDefaultSeeder.Seed(connectionFactory);
			WriteVersion(connectionFactory, CurrentVersion);
			version = CurrentVersion;
		}

		if (version != CurrentVersion)
			throw new InvalidOperationException($"Approval-policy schema version '{version}' is not supported. Expected '{CurrentVersion}'.");

		ApprovalPolicyDefaultSeeder.Seed(connectionFactory);
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
			_ => throw new NotSupportedException($"Approval-policy migrations are not supported for provider '{connectionFactory.Provider}'.")
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
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;",
			DatabaseProvider.SqlServer => $"IF EXISTS (SELECT 1 FROM DepotFeatureVersions WHERE Name=N'{FeatureName}') UPDATE DepotFeatureVersions SET Version={version} WHERE Name=N'{FeatureName}'; ELSE INSERT INTO DepotFeatureVersions (Name, Version) VALUES (N'{FeatureName}', {version});",
			DatabaseProvider.MySql => $"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{FeatureName}', {version}) ON DUPLICATE KEY UPDATE Version=VALUES(Version);",
			_ => throw new NotSupportedException($"Approval-policy migrations are not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
