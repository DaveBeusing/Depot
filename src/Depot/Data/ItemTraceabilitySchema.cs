// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Models;

namespace Depot.Data;

internal static class ItemTraceabilitySchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		EnsureTables(connection, connectionFactory.Provider);
		EnsureIndexes(connection, connectionFactory.Provider);
	}

	private static void EnsureTables(DbConnection connection, DatabaseProvider provider)
	{
		foreach (var sql in provider switch
		{
			DatabaseProvider.Local => SqliteTables,
			DatabaseProvider.SqlServer => SqlServerTables,
			DatabaseProvider.MySql => MySqlTables,
			_ => throw new NotSupportedException($"Item traceability schema is not supported for provider '{provider}'.")
		})
		{
			Execute(connection, sql);
		}
	}

	private static void EnsureIndexes(DbConnection connection, DatabaseProvider provider)
	{
		if (provider == DatabaseProvider.Local)
		{
			Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_ItemTrackingUnits_Item_Mode_Code ON ItemTrackingUnits(ItemId, TrackingMode, Code);");
			Execute(connection, "CREATE INDEX IF NOT EXISTS IX_StockMovementTracking_TrackingUnit ON StockMovementTracking(TrackingUnitId);");
			return;
		}
		if (provider == DatabaseProvider.SqlServer)
		{
			Execute(connection, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ItemTrackingUnits_Item_Mode_Code' AND object_id = OBJECT_ID(N'ItemTrackingUnits')) CREATE UNIQUE INDEX UX_ItemTrackingUnits_Item_Mode_Code ON ItemTrackingUnits(ItemId, TrackingMode, Code);");
			Execute(connection, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StockMovementTracking_TrackingUnit' AND object_id = OBJECT_ID(N'StockMovementTracking')) CREATE INDEX IX_StockMovementTracking_TrackingUnit ON StockMovementTracking(TrackingUnitId);");
			return;
		}
		if (provider == DatabaseProvider.MySql)
		{
			if (!IndexExists(connection, "ItemTrackingUnits", "UX_ItemTrackingUnits_Item_Mode_Code")) Execute(connection, "CREATE UNIQUE INDEX UX_ItemTrackingUnits_Item_Mode_Code ON ItemTrackingUnits(ItemId, TrackingMode, Code);");
			if (!IndexExists(connection, "StockMovementTracking", "IX_StockMovementTracking_TrackingUnit")) Execute(connection, "CREATE INDEX IX_StockMovementTracking_TrackingUnit ON StockMovementTracking(TrackingUnitId);");
			return;
		}
		throw new NotSupportedException($"Item traceability indexes are not supported for provider '{provider}'.");
	}

	private static bool IndexExists(DbConnection connection, string table, string index)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = @table AND index_name = @index;";
		var tableParameter = command.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = table; command.Parameters.Add(tableParameter);
		var indexParameter = command.CreateParameter(); indexParameter.ParameterName = "@index"; indexParameter.Value = index; command.Parameters.Add(indexParameter);
		return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
	}

	private static void Execute(DbConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	private static readonly string[] SqliteTables =
	[
		"""
		CREATE TABLE IF NOT EXISTS ItemTrackingUnits (
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ItemId INTEGER NOT NULL REFERENCES Items(Id),
			TrackingMode INTEGER NOT NULL,
			Code TEXT NOT NULL,
			ExpiryDate TEXT NULL,
			IsBlocked INTEGER NOT NULL DEFAULT 0,
			BlockReason TEXT NULL,
			CreatedAtUtc TEXT NOT NULL,
			Version INTEGER NOT NULL DEFAULT 1
		);
		""",
		"""
		CREATE TABLE IF NOT EXISTS StockMovementTracking (
			StockMovementId INTEGER NOT NULL REFERENCES StockMovements(Id),
			TrackingUnitId INTEGER NOT NULL REFERENCES ItemTrackingUnits(Id),
			Quantity INTEGER NOT NULL,
			PRIMARY KEY (StockMovementId, TrackingUnitId)
		);
		"""
	];

	private static readonly string[] SqlServerTables =
	[
		"""
		IF OBJECT_ID(N'ItemTrackingUnits', N'U') IS NULL
		BEGIN
			CREATE TABLE ItemTrackingUnits (
				Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
				ItemId bigint NOT NULL REFERENCES Items(Id),
				TrackingMode int NOT NULL,
				Code nvarchar(128) NOT NULL,
				ExpiryDate date NULL,
				IsBlocked bit NOT NULL CONSTRAINT DF_ItemTrackingUnits_IsBlocked DEFAULT (0),
				BlockReason nvarchar(500) NULL,
				CreatedAtUtc datetime2 NOT NULL,
				Version bigint NOT NULL CONSTRAINT DF_ItemTrackingUnits_Version DEFAULT (1)
			);
		END
		""",
		"""
		IF OBJECT_ID(N'StockMovementTracking', N'U') IS NULL
		BEGIN
			CREATE TABLE StockMovementTracking (
				StockMovementId bigint NOT NULL REFERENCES StockMovements(Id),
				TrackingUnitId bigint NOT NULL REFERENCES ItemTrackingUnits(Id),
				Quantity int NOT NULL,
				CONSTRAINT PK_StockMovementTracking PRIMARY KEY (StockMovementId, TrackingUnitId)
			);
		END
		"""
	];

	private static readonly string[] MySqlTables =
	[
		"""
		CREATE TABLE IF NOT EXISTS ItemTrackingUnits (
			Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
			ItemId BIGINT NOT NULL,
			TrackingMode INT NOT NULL,
			Code VARCHAR(128) NOT NULL,
			ExpiryDate DATE NULL,
			IsBlocked TINYINT(1) NOT NULL DEFAULT 0,
			BlockReason VARCHAR(500) NULL,
			CreatedAtUtc DATETIME(6) NOT NULL,
			Version BIGINT NOT NULL DEFAULT 1,
			CONSTRAINT FK_ItemTrackingUnits_Items FOREIGN KEY (ItemId) REFERENCES Items(Id)
		) ENGINE=InnoDB;
		""",
		"""
		CREATE TABLE IF NOT EXISTS StockMovementTracking (
			StockMovementId BIGINT NOT NULL,
			TrackingUnitId BIGINT NOT NULL,
			Quantity INT NOT NULL,
			PRIMARY KEY (StockMovementId, TrackingUnitId),
			CONSTRAINT FK_StockMovementTracking_Movements FOREIGN KEY (StockMovementId) REFERENCES StockMovements(Id),
			CONSTRAINT FK_StockMovementTracking_Units FOREIGN KEY (TrackingUnitId) REFERENCES ItemTrackingUnits(Id)
		) ENGINE=InnoDB;
		"""
	];
}
