// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Models;

namespace Depot.Data;

internal enum DatabaseProvisioningPath
{
	FastPath,
	FullProvisioning
}

public static class DatabaseProvisioningService
{
	public static IDatabaseConnectionFactory Initialize(DatabaseConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		var connectionFactory = DatabaseProviderFactory.CreateConnectionFactory(settings);
		Initialize(connectionFactory);
		return connectionFactory;
	}

	public static void Initialize(IDatabaseConnectionFactory connectionFactory) =>
		_ = InitializeCore(connectionFactory);

	internal static DatabaseProvisioningPath InitializeCore(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		if (DatabaseSchemaStateInspector.IsCurrent(connectionFactory))
			return DatabaseProvisioningPath.FastPath;

		using var provisioningLock = DatabaseProvisioningLock.Acquire(connectionFactory);
		if (DatabaseSchemaStateInspector.IsCurrent(connectionFactory))
			return DatabaseProvisioningPath.FastPath;

		DatabaseProviderFactory.CreateInitializer(connectionFactory).Initialize();
		FeatureVersionMetadataRecovery.RestoreIfCurrentSchemaDetected(connectionFactory);
		SalesSchemaMigration.Migrate(connectionFactory);
		FinanceInventoryAccountingSchemaMigration.Migrate(connectionFactory);
		UserSessionSchemaMigration.Migrate(connectionFactory);
		SecurityEventSchemaMigration.Migrate(connectionFactory);
		UserPreferenceSchemaMigration.Migrate(connectionFactory);
		DocumentTemplateSchemaMigration.Migrate(connectionFactory);
		ApprovalPolicySchemaMigration.Migrate(connectionFactory);
		EnterpriseIdentitySchemaMigration.Migrate(connectionFactory);
		BusinessAttachmentSchemaMigration.Migrate(connectionFactory);
		return DatabaseProvisioningPath.FullProvisioning;
	}
}


internal static class FeatureVersionMetadataRecovery
{
	private static readonly IReadOnlyDictionary<string, int> CurrentVersions =
		new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["Sales"] = SalesSchemaMigration.CurrentVersion,
			["Finance"] = FinanceInventoryAccountingSchemaMigration.CurrentVersion,
			["UserSessions"] = UserSessionSchemaMigration.CurrentVersion,
			["SecurityEvents"] = SecurityEventSchemaMigration.CurrentVersion,
			["UserPreferences"] = UserPreferenceSchemaMigration.CurrentVersion,
			["DocumentTemplates"] = DocumentTemplateSchemaMigration.CurrentVersion,
			["ApprovalPolicies"] = ApprovalPolicySchemaMigration.CurrentVersion,
			["EnterpriseIdentity"] = EnterpriseIdentitySchemaMigration.CurrentVersion,
			["BusinessAttachments"] = BusinessAttachmentSchemaMigration.CurrentVersion
		};

	public static void RestoreIfCurrentSchemaDetected(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		if (!HasCurrentFeatureSchema(connection, connectionFactory.Provider)) return;

		EnsureVersionTable(connection, connectionFactory.Provider);
		if (HasFutureFeatureVersion(connection)) return;

		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		foreach (var feature in CurrentVersions)
		{
			command.CommandText = UpsertVersionSql(connectionFactory.Provider, feature.Key, feature.Value);
			command.Parameters.Clear();
			command.ExecuteNonQuery();
		}
		transaction.Commit();
	}

	private static bool HasCurrentFeatureSchema(DbConnection connection, DatabaseProvider provider) =>
		TableExists(connection, provider, "SalesHybridElectronicInvoiceArtifacts") &&
		TableExists(connection, provider, "PricingExchangeRates") &&
		ColumnExists(connection, provider, "Shipments", "ReversedAtUtc") &&
		ColumnExists(connection, provider, "Shipments", "PackingStatus") &&
		ColumnExists(connection, provider, "SalesInvoiceLines", "TaxCategoryCode") &&
		TableExists(connection, provider, "FinanceLocalizationPacks") &&
		ColumnExists(connection, provider, "UserSessionPolicy", "ConcurrentSessionMode") &&
		TableExists(connection, provider, "SecurityEventExportTargets") &&
		TableExists(connection, provider, "UserWorkspacePreferences") &&
		TableExists(connection, provider, "DocumentTemplates") &&
		ColumnExists(connection, provider, "EnterpriseIdentityProviders", "MaximumAuthenticationAgeMinutes") &&
		TableExists(connection, provider, "BusinessAttachments") &&
		TableExists(connection, provider, "BusinessAttachmentRevisions") &&
		TableExists(connection, provider, "BusinessAttachmentContents");

	private static bool HasFutureFeatureVersion(DbConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Name, Version FROM DepotFeatureVersions;";
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			var name = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
			if (name is null || !CurrentVersions.TryGetValue(name, out var currentVersion)) continue;
			if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) > currentVersion) return true;
		}
		return false;
	}

	private static void EnsureVersionTable(DbConnection connection, DatabaseProvider provider)
	{
		using var command = connection.CreateCommand();
		command.CommandText = provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name TEXT PRIMARY KEY, Version INTEGER NOT NULL);",
			DatabaseProvider.SqlServer => "IF OBJECT_ID(N'DepotFeatureVersions', N'U') IS NULL CREATE TABLE DepotFeatureVersions (Name nvarchar(100) NOT NULL PRIMARY KEY, Version int NOT NULL);",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name VARCHAR(100) NOT NULL PRIMARY KEY, Version INT NOT NULL);",
			_ => throw new NotSupportedException($"Feature-version metadata recovery is not supported for provider '{provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private static bool TableExists(DbConnection connection, DatabaseProvider provider, string table)
	{
		using var command = connection.CreateCommand();
		command.CommandText = provider switch
		{
			DatabaseProvider.Local => $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';",
			DatabaseProvider.SqlServer => $"SELECT CASE WHEN OBJECT_ID(N'{table}', N'U') IS NULL THEN 0 ELSE 1 END;",
			DatabaseProvider.MySql => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='{table}';",
			_ => throw new NotSupportedException($"Feature-schema inspection is not supported for provider '{provider}'.")
		};
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}

	private static bool ColumnExists(DbConnection connection, DatabaseProvider provider, string table, string column)
	{
		using var command = connection.CreateCommand();
		command.CommandText = provider switch
		{
			DatabaseProvider.Local => $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';",
			DatabaseProvider.SqlServer => $"SELECT CASE WHEN COL_LENGTH(N'{table}', N'{column}') IS NULL THEN 0 ELSE 1 END;",
			DatabaseProvider.MySql => $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='{table}' AND column_name='{column}';",
			_ => throw new NotSupportedException($"Feature-schema inspection is not supported for provider '{provider}'.")
		};
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}

	private static string UpsertVersionSql(DatabaseProvider provider, string feature, int version) => provider switch
	{
		DatabaseProvider.Local =>
			$"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{feature}', {version}) ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;",
		DatabaseProvider.SqlServer =>
			$"IF EXISTS (SELECT 1 FROM DepotFeatureVersions WHERE Name=N'{feature}') UPDATE DepotFeatureVersions SET Version={version} WHERE Name=N'{feature}'; ELSE INSERT INTO DepotFeatureVersions (Name, Version) VALUES (N'{feature}', {version});",
		DatabaseProvider.MySql =>
			$"INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('{feature}', {version}) ON DUPLICATE KEY UPDATE Version=VALUES(Version);",
		_ => throw new NotSupportedException($"Feature-version metadata recovery is not supported for provider '{provider}'.")
	};
}
