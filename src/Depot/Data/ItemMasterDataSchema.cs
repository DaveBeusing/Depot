// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Models;

namespace Depot.Data;

internal static class ItemMasterDataSchema
{
	private static readonly string[] Columns =
	[
		"Gtin",
		"ItemType",
		"LifecycleStatus",
		"Revision",
		"Model",
		"ProductFamily",
		"CountryOfOrigin",
		"CustomsTariffNumber",
		"Eccn",
		"TrackingMode",
		"NetWeightKg",
		"GrossWeightKg",
		"LengthMm",
		"WidthMm",
		"HeightMm",
		"IsDangerousGoods",
		"UnNumber",
		"ContainsBattery",
		"RohsStatus",
		"ReachStatus",
		"IntroductionDate",
		"EndOfLifeDate",
		"LastBuyDate",
		"EndOfSupportDate",
		"ReplacementItemId",
		"Notes"
	];

	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		foreach (var column in Columns)
		{
			if (ColumnExists(connection, connectionFactory.Provider, column)) continue;
			using var command = connection.CreateCommand();
			command.CommandText = $"ALTER TABLE Items ADD {GetDefinition(connectionFactory.Provider, column)};";
			command.ExecuteNonQuery();
		}

		MigrateLegacyPhysicalColumns(connection, connectionFactory.Provider);
		EnsureUniqueGtinIndex(connection, connectionFactory.Provider);
	}

	private static bool ColumnExists(DbConnection connection, DatabaseProvider provider, string column)
	{
		using var command = connection.CreateCommand();
		command.CommandText = provider switch
		{
			DatabaseProvider.Local => $"SELECT COUNT(*) FROM pragma_table_info('Items') WHERE name = '{column}';",
			DatabaseProvider.SqlServer => $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'Items') AND name = N'{column}';",
			DatabaseProvider.MySql => $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'Items' AND column_name = '{column}';",
			_ => throw new NotSupportedException($"Item master data schema is not supported for provider '{provider}'.")
		};
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}

	private static void MigrateLegacyPhysicalColumns(DbConnection connection, DatabaseProvider provider)
	{
		CopyLegacyColumn(connection, provider, "NetWeight", "NetWeightKg");
		CopyLegacyColumn(connection, provider, "Length", "LengthMm");
		CopyLegacyColumn(connection, provider, "Width", "WidthMm");
		CopyLegacyColumn(connection, provider, "Height", "HeightMm");
	}

	private static void CopyLegacyColumn(DbConnection connection, DatabaseProvider provider, string source, string target)
	{
		if (!ColumnExists(connection, provider, source) || !ColumnExists(connection, provider, target)) return;
		using var command = connection.CreateCommand();
		command.CommandText = $"UPDATE Items SET {target} = {source} WHERE {target} IS NULL AND {source} IS NOT NULL;";
		command.ExecuteNonQuery();
	}

	private static void EnsureUniqueGtinIndex(DbConnection connection, DatabaseProvider provider)
	{
		using var command = connection.CreateCommand();
		command.CommandText = provider switch
		{
			DatabaseProvider.Local => "CREATE UNIQUE INDEX IF NOT EXISTS IX_Items_Gtin ON Items(Gtin) WHERE Gtin IS NOT NULL;",
			DatabaseProvider.SqlServer => "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'Items') AND name = N'IX_Items_Gtin') CREATE UNIQUE INDEX IX_Items_Gtin ON Items(Gtin) WHERE Gtin IS NOT NULL;",
			DatabaseProvider.MySql => "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'Items' AND index_name = 'IX_Items_Gtin';",
			_ => throw new NotSupportedException($"GTIN index is not supported for provider '{provider}'.")
		};
		if (provider != DatabaseProvider.MySql)
		{
			command.ExecuteNonQuery();
			return;
		}

		var exists = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
		if (exists) return;
		command.CommandText = "CREATE UNIQUE INDEX IX_Items_Gtin ON Items(Gtin);";
		command.ExecuteNonQuery();
	}

	private static string GetDefinition(DatabaseProvider provider, string column) =>
		(provider, column) switch
		{
			(DatabaseProvider.Local, "Gtin") => "Gtin TEXT NULL",
			(DatabaseProvider.Local, "ItemType") => "ItemType INTEGER NOT NULL DEFAULT 1",
			(DatabaseProvider.Local, "LifecycleStatus") => "LifecycleStatus INTEGER NOT NULL DEFAULT 2",
			(DatabaseProvider.Local, "Revision") => "Revision TEXT NULL",
			(DatabaseProvider.Local, "Model") => "Model TEXT NULL",
			(DatabaseProvider.Local, "ProductFamily") => "ProductFamily TEXT NULL",
			(DatabaseProvider.Local, "CountryOfOrigin") => "CountryOfOrigin TEXT NULL",
			(DatabaseProvider.Local, "CustomsTariffNumber") => "CustomsTariffNumber TEXT NULL",
			(DatabaseProvider.Local, "Eccn") => "Eccn TEXT NULL",
			(DatabaseProvider.Local, "TrackingMode") => "TrackingMode INTEGER NOT NULL DEFAULT 0",
			(DatabaseProvider.Local, "NetWeightKg") => "NetWeightKg REAL NULL",
			(DatabaseProvider.Local, "GrossWeightKg") => "GrossWeightKg REAL NULL",
			(DatabaseProvider.Local, "LengthMm") => "LengthMm REAL NULL",
			(DatabaseProvider.Local, "WidthMm") => "WidthMm REAL NULL",
			(DatabaseProvider.Local, "HeightMm") => "HeightMm REAL NULL",
			(DatabaseProvider.Local, "IsDangerousGoods") => "IsDangerousGoods INTEGER NOT NULL DEFAULT 0",
			(DatabaseProvider.Local, "UnNumber") => "UnNumber TEXT NULL",
			(DatabaseProvider.Local, "ContainsBattery") => "ContainsBattery INTEGER NOT NULL DEFAULT 0",
			(DatabaseProvider.Local, "RohsStatus") => "RohsStatus INTEGER NOT NULL DEFAULT 0",
			(DatabaseProvider.Local, "ReachStatus") => "ReachStatus INTEGER NOT NULL DEFAULT 0",
			(DatabaseProvider.Local, "IntroductionDate") => "IntroductionDate TEXT NULL",
			(DatabaseProvider.Local, "EndOfLifeDate") => "EndOfLifeDate TEXT NULL",
			(DatabaseProvider.Local, "LastBuyDate") => "LastBuyDate TEXT NULL",
			(DatabaseProvider.Local, "EndOfSupportDate") => "EndOfSupportDate TEXT NULL",
			(DatabaseProvider.Local, "ReplacementItemId") => "ReplacementItemId INTEGER NULL REFERENCES Items(Id)",
			(DatabaseProvider.Local, "Notes") => "Notes TEXT NULL",

			(DatabaseProvider.SqlServer, "Gtin") => "Gtin nvarchar(14) NULL",
			(DatabaseProvider.SqlServer, "ItemType") => "ItemType int NOT NULL DEFAULT (1)",
			(DatabaseProvider.SqlServer, "LifecycleStatus") => "LifecycleStatus int NOT NULL DEFAULT (2)",
			(DatabaseProvider.SqlServer, "Revision") => "Revision nvarchar(64) NULL",
			(DatabaseProvider.SqlServer, "Model") => "Model nvarchar(128) NULL",
			(DatabaseProvider.SqlServer, "ProductFamily") => "ProductFamily nvarchar(128) NULL",
			(DatabaseProvider.SqlServer, "CountryOfOrigin") => "CountryOfOrigin nchar(2) NULL",
			(DatabaseProvider.SqlServer, "CustomsTariffNumber") => "CustomsTariffNumber nvarchar(32) NULL",
			(DatabaseProvider.SqlServer, "Eccn") => "Eccn nvarchar(32) NULL",
			(DatabaseProvider.SqlServer, "TrackingMode") => "TrackingMode int NOT NULL DEFAULT (0)",
			(DatabaseProvider.SqlServer, "NetWeightKg") => "NetWeightKg decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "GrossWeightKg") => "GrossWeightKg decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "LengthMm") => "LengthMm decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "WidthMm") => "WidthMm decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "HeightMm") => "HeightMm decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "IsDangerousGoods") => "IsDangerousGoods bit NOT NULL DEFAULT (0)",
			(DatabaseProvider.SqlServer, "UnNumber") => "UnNumber nvarchar(6) NULL",
			(DatabaseProvider.SqlServer, "ContainsBattery") => "ContainsBattery bit NOT NULL DEFAULT (0)",
			(DatabaseProvider.SqlServer, "RohsStatus") => "RohsStatus int NOT NULL DEFAULT (0)",
			(DatabaseProvider.SqlServer, "ReachStatus") => "ReachStatus int NOT NULL DEFAULT (0)",
			(DatabaseProvider.SqlServer, "IntroductionDate") => "IntroductionDate date NULL",
			(DatabaseProvider.SqlServer, "EndOfLifeDate") => "EndOfLifeDate date NULL",
			(DatabaseProvider.SqlServer, "LastBuyDate") => "LastBuyDate date NULL",
			(DatabaseProvider.SqlServer, "EndOfSupportDate") => "EndOfSupportDate date NULL",
			(DatabaseProvider.SqlServer, "ReplacementItemId") => "ReplacementItemId bigint NULL REFERENCES Items(Id)",
			(DatabaseProvider.SqlServer, "Notes") => "Notes nvarchar(max) NULL",

			(DatabaseProvider.MySql, "Gtin") => "Gtin VARCHAR(14) NULL",
			(DatabaseProvider.MySql, "ItemType") => "ItemType INT NOT NULL DEFAULT 1",
			(DatabaseProvider.MySql, "LifecycleStatus") => "LifecycleStatus INT NOT NULL DEFAULT 2",
			(DatabaseProvider.MySql, "Revision") => "Revision VARCHAR(64) NULL",
			(DatabaseProvider.MySql, "Model") => "Model VARCHAR(128) NULL",
			(DatabaseProvider.MySql, "ProductFamily") => "ProductFamily VARCHAR(128) NULL",
			(DatabaseProvider.MySql, "CountryOfOrigin") => "CountryOfOrigin CHAR(2) NULL",
			(DatabaseProvider.MySql, "CustomsTariffNumber") => "CustomsTariffNumber VARCHAR(32) NULL",
			(DatabaseProvider.MySql, "Eccn") => "Eccn VARCHAR(32) NULL",
			(DatabaseProvider.MySql, "TrackingMode") => "TrackingMode INT NOT NULL DEFAULT 0",
			(DatabaseProvider.MySql, "NetWeightKg") => "NetWeightKg DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "GrossWeightKg") => "GrossWeightKg DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "LengthMm") => "LengthMm DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "WidthMm") => "WidthMm DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "HeightMm") => "HeightMm DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "IsDangerousGoods") => "IsDangerousGoods BOOLEAN NOT NULL DEFAULT FALSE",
			(DatabaseProvider.MySql, "UnNumber") => "UnNumber VARCHAR(6) NULL",
			(DatabaseProvider.MySql, "ContainsBattery") => "ContainsBattery BOOLEAN NOT NULL DEFAULT FALSE",
			(DatabaseProvider.MySql, "RohsStatus") => "RohsStatus INT NOT NULL DEFAULT 0",
			(DatabaseProvider.MySql, "ReachStatus") => "ReachStatus INT NOT NULL DEFAULT 0",
			(DatabaseProvider.MySql, "IntroductionDate") => "IntroductionDate DATE NULL",
			(DatabaseProvider.MySql, "EndOfLifeDate") => "EndOfLifeDate DATE NULL",
			(DatabaseProvider.MySql, "LastBuyDate") => "LastBuyDate DATE NULL",
			(DatabaseProvider.MySql, "EndOfSupportDate") => "EndOfSupportDate DATE NULL",
			(DatabaseProvider.MySql, "ReplacementItemId") => "ReplacementItemId BIGINT NULL REFERENCES Items(Id)",
			(DatabaseProvider.MySql, "Notes") => "Notes TEXT NULL",
			_ => throw new NotSupportedException($"Column '{column}' is not supported for provider '{provider}'.")
		};
}
