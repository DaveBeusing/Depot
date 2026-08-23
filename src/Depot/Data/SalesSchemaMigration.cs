// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Models;

namespace Depot.Data;

public static class SalesSchemaMigration
{
	public const int CurrentVersion = 7;
	private const string FeatureName = "Sales";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		EnsureVersionTable(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion)
			throw new InvalidOperationException($"Sales schema version '{version}' is newer than the supported version '{CurrentVersion}'.");

		if (version == 0)
		{
			SalesSchemaInitializer.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 1);
			version = 1;
		}

		if (version == 1)
		{
			SalesCorrectionSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 2);
			version = 2;
		}

		if (version == 2)
		{
			CustomerAddressSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 3);
			version = 3;
		}

		if (version == 3)
		{
			SalesReservationSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 4);
			version = 4;
		}

		if (version == 4)
		{
			SalesOrderAddressSnapshotSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 5);
			version = 5;
		}

		if (version == 5)
		{
			SalesCommercialSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 6);
			version = 6;
		}

		if (version == 6)
		{
			SalesDocumentIssuerSnapshotSchema.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 7);
			version = 7;
		}

		if (version != CurrentVersion)
			throw new InvalidOperationException($"Sales schema version '{version}' is not supported. Expected '{CurrentVersion}'.");
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
			_ => throw new NotSupportedException($"Sales migrations are not supported for provider '{connectionFactory.Provider}'.")
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
			_ => throw new NotSupportedException($"Sales migrations are not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
