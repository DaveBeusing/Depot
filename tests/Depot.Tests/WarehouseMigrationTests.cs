// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class WarehouseMigrationTests : IDisposable
{
	private readonly string _databasePath =
		Path.Combine(Path.GetTempPath(), $"depot-warehouse-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionEightDataMigratesToWarehouseStructureWithoutLosingReferences()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		CreateVersionEightFixture(factory);

		new DepotDatabase(factory).Initialize();

		using var connection = factory.CreateConnection();
		connection.Open();

		Assert.Equal(10L, ExecuteInt64(connection, "SELECT Version FROM DatabaseInfo;"));
		Assert.Equal("Main Warehouse", ExecuteString(connection, "SELECT Name FROM Warehouses;"));
		Assert.Equal("A-01", ExecuteString(connection, "SELECT Name FROM StorageLocations WHERE Id = 1;"));
		Assert.Equal(1L, ExecuteInt64(connection, "SELECT WarehouseId FROM StorageLocations WHERE Id = 1;"));
		Assert.Equal(1L, ExecuteInt64(connection, "SELECT StorageLocationId FROM Inventories WHERE Id = 1;"));
		Assert.Equal(1L, ExecuteInt64(connection, "SELECT InventoryId FROM StockMovements WHERE Id = 1;"));
		Assert.Equal(10L, ExecuteInt64(connection, "SELECT COUNT(*) FROM ReasonCodes;"));
		Assert.Equal(0L, ExecuteInt64(connection, "SELECT COUNT(*) FROM StockMovements WHERE ReasonCodeId IS NOT NULL;"));
		Assert.Equal(0L, ExecuteInt64(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Locations';"));
		Assert.Equal("ok", ExecuteString(connection, "PRAGMA integrity_check;"));
	}

	private static void CreateVersionEightFixture(SqliteConnectionFactory factory)
	{
		using var connection = factory.CreateConnection();
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		PRAGMA foreign_keys = OFF;

		UPDATE StorageLocations
		SET Name = 'A-01', Description = 'Migrated storage location'
		WHERE Id = 1;

		INSERT INTO Items (Id, PartNumber, Description, IsActive, Version)
		VALUES (1, 'MIG-001', 'Migration test item', 1, 1);

		INSERT INTO Inventories (Id, ItemId, PurposeId, StorageLocationId, IsActive, Version)
		VALUES (1, 1, 1, 1, 1, 1);

		INSERT INTO StockMovements
			(Id, InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes)
		VALUES
			(1, 1, 0, '2026-07-17T10:00:00.0000000Z', 12, 3.5, 'MIGRATION', NULL);

		ALTER TABLE StockMovements RENAME TO StockMovementsVersionNine;
		ALTER TABLE Inventories RENAME TO InventoriesVersionNine;

		CREATE TABLE Locations
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			Name TEXT NOT NULL UNIQUE,
			Description TEXT,
			IsActive INTEGER NOT NULL DEFAULT 1,
			Version INTEGER NOT NULL DEFAULT 1
		);

		INSERT INTO Locations (Id, Name, Description, IsActive, Version)
		SELECT Id, Name, Description, IsActive, Version
		FROM StorageLocations;

		CREATE TABLE Inventories
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			ItemId INTEGER NOT NULL,
			PurposeId INTEGER NOT NULL,
			LocationId INTEGER NOT NULL,
			IsActive INTEGER NOT NULL DEFAULT 1,
			Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(ItemId, PurposeId, LocationId),
			FOREIGN KEY(ItemId) REFERENCES Items(Id),
			FOREIGN KEY(PurposeId) REFERENCES Purposes(Id),
			FOREIGN KEY(LocationId) REFERENCES Locations(Id)
		);

		INSERT INTO Inventories (Id, ItemId, PurposeId, LocationId, IsActive, Version)
		SELECT Id, ItemId, PurposeId, StorageLocationId, IsActive, Version
		FROM InventoriesVersionNine;

		CREATE TABLE StockMovements
		(
			Id INTEGER PRIMARY KEY AUTOINCREMENT,
			InventoryId INTEGER NOT NULL,
			MovementType INTEGER NOT NULL,
			TimestampUtc TEXT NOT NULL,
			Quantity INTEGER NOT NULL,
			UnitPrice REAL NULL,
			Reference TEXT NULL,
			Notes TEXT NULL,
			FOREIGN KEY(InventoryId) REFERENCES Inventories(Id)
		);

		INSERT INTO StockMovements
			(Id, InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes)
		SELECT Id, InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes
		FROM StockMovementsVersionNine;

		DROP TABLE StockMovementsVersionNine;
		DROP TABLE InventoriesVersionNine;
		DROP TABLE StorageLocations;
		DROP TABLE Warehouses;

		CREATE INDEX IX_StockMovements_InventoryId_TimestampUtc
			ON StockMovements(InventoryId, TimestampUtc);

		UPDATE DatabaseInfo SET Version = 8;
		PRAGMA foreign_keys = ON;
		""";
		command.ExecuteNonQuery();
	}

	private static long ExecuteInt64(SqliteConnection connection, string commandText)
	{
		using var command = connection.CreateCommand();
		command.CommandText = commandText;
		return Convert.ToInt64(command.ExecuteScalar());
	}

	private static string ExecuteString(SqliteConnection connection, string commandText)
	{
		using var command = connection.CreateCommand();
		command.CommandText = commandText;
		return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
			?? string.Empty;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath))
		{
			File.Delete(_databasePath);
		}
	}
}
