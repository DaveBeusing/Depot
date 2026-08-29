// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

namespace Depot.Data;

public static class FinanceAccountsReceivableSchemaMigration
{
	public const int CurrentVersion = 3;
	private const string FeatureName = "Finance";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		SalesSchemaMigration.Migrate(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion) return;
		if (version < 2)
		{
			FinanceSchemaMigration.Migrate(connectionFactory);
			version = ReadVersion(connectionFactory);
		}
		if (version == 2)
		{
			FinanceAccountsReceivableSchemaInitializer.Ensure(connectionFactory);
			WriteVersion(connectionFactory, 3);
			version = 3;
		}
		if (version != CurrentVersion) throw new InvalidOperationException($"Finance schema version '{version}' is not supported. Expected '{CurrentVersion}'.");
	}

	private static int ReadVersion(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';";
		try
		{
			var value = command.ExecuteScalar();
			return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return 0;
		}
	}

	private static void WriteVersion(IDatabaseConnectionFactory connectionFactory, int version)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => $"INSERT INTO DepotFeatureVersions (Name,Version) VALUES ('{FeatureName}',{version}) ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;",
			DatabaseProvider.SqlServer => $"IF EXISTS (SELECT 1 FROM DepotFeatureVersions WHERE Name=N'{FeatureName}') UPDATE DepotFeatureVersions SET Version={version} WHERE Name=N'{FeatureName}'; ELSE INSERT INTO DepotFeatureVersions (Name,Version) VALUES (N'{FeatureName}',{version});",
			DatabaseProvider.MySql => $"INSERT INTO DepotFeatureVersions (Name,Version) VALUES ('{FeatureName}',{version}) ON DUPLICATE KEY UPDATE Version=VALUES(Version);",
			_ => throw new NotSupportedException($"Finance Accounts Receivable migrations are not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
