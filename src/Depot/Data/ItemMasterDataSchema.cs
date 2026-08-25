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
		"CountryOfOrigin",
		"CustomsTariffNumber",
		"TrackingMode",
		"NetWeight",
		"Length",
		"Width",
		"Height",
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

	private static string GetDefinition(DatabaseProvider provider, string column) =>
		(provider, column) switch
		{
			(DatabaseProvider.Local, "Gtin") => "Gtin TEXT NULL",
			(DatabaseProvider.Local, "ItemType") => "ItemType INTEGER NOT NULL DEFAULT 1",
			(DatabaseProvider.Local, "LifecycleStatus") => "LifecycleStatus INTEGER NOT NULL DEFAULT 2",
			(DatabaseProvider.Local, "CountryOfOrigin") => "CountryOfOrigin TEXT NULL",
			(DatabaseProvider.Local, "CustomsTariffNumber") => "CustomsTariffNumber TEXT NULL",
			(DatabaseProvider.Local, "TrackingMode") => "TrackingMode INTEGER NOT NULL DEFAULT 0",
			(DatabaseProvider.Local, "NetWeight") => "NetWeight REAL NULL",
			(DatabaseProvider.Local, "Length") => "Length REAL NULL",
			(DatabaseProvider.Local, "Width") => "Width REAL NULL",
			(DatabaseProvider.Local, "Height") => "Height REAL NULL",
			(DatabaseProvider.Local, "ReplacementItemId") => "ReplacementItemId INTEGER NULL REFERENCES Items(Id)",
			(DatabaseProvider.Local, "Notes") => "Notes TEXT NULL",

			(DatabaseProvider.SqlServer, "Gtin") => "Gtin nvarchar(14) NULL",
			(DatabaseProvider.SqlServer, "ItemType") => "ItemType int NOT NULL DEFAULT (1)",
			(DatabaseProvider.SqlServer, "LifecycleStatus") => "LifecycleStatus int NOT NULL DEFAULT (2)",
			(DatabaseProvider.SqlServer, "CountryOfOrigin") => "CountryOfOrigin nchar(2) NULL",
			(DatabaseProvider.SqlServer, "CustomsTariffNumber") => "CustomsTariffNumber nvarchar(32) NULL",
			(DatabaseProvider.SqlServer, "TrackingMode") => "TrackingMode int NOT NULL DEFAULT (0)",
			(DatabaseProvider.SqlServer, "NetWeight") => "NetWeight decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "Length") => "Length decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "Width") => "Width decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "Height") => "Height decimal(18,6) NULL",
			(DatabaseProvider.SqlServer, "ReplacementItemId") => "ReplacementItemId bigint NULL REFERENCES Items(Id)",
			(DatabaseProvider.SqlServer, "Notes") => "Notes nvarchar(max) NULL",

			(DatabaseProvider.MySql, "Gtin") => "Gtin VARCHAR(14) NULL",
			(DatabaseProvider.MySql, "ItemType") => "ItemType INT NOT NULL DEFAULT 1",
			(DatabaseProvider.MySql, "LifecycleStatus") => "LifecycleStatus INT NOT NULL DEFAULT 2",
			(DatabaseProvider.MySql, "CountryOfOrigin") => "CountryOfOrigin CHAR(2) NULL",
			(DatabaseProvider.MySql, "CustomsTariffNumber") => "CustomsTariffNumber VARCHAR(32) NULL",
			(DatabaseProvider.MySql, "TrackingMode") => "TrackingMode INT NOT NULL DEFAULT 0",
			(DatabaseProvider.MySql, "NetWeight") => "NetWeight DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "Length") => "Length DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "Width") => "Width DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "Height") => "Height DECIMAL(18,6) NULL",
			(DatabaseProvider.MySql, "ReplacementItemId") => "ReplacementItemId BIGINT NULL REFERENCES Items(Id)",
			(DatabaseProvider.MySql, "Notes") => "Notes TEXT NULL",
			_ => throw new NotSupportedException($"Column '{column}' is not supported for provider '{provider}'.")
		};
}
