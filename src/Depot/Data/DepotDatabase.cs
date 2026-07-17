// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Depot.Diagnostics;
using Depot.Models;

namespace Depot.Data;

public sealed class DepotDatabase : IDatabaseInitializer
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public DepotDatabase(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public void Initialize()
	{
		using var connection =
			_connectionFactory.CreateConnection();

		DatabaseDiagnostics.ConnectionOpening(DatabaseProvider.Local, "local SQLite schema");
		try
		{
			connection.Open();
			DatabaseDiagnostics.ConnectionOpened(DatabaseProvider.Local, "local SQLite schema");
		}
		catch (Exception exception)
		{
			DatabaseDiagnostics.ConnectionFailed(DatabaseProvider.Local, "local SQLite schema", exception);
			throw;
		}

		CreateDatabaseInfoTable(
			connection);

		var version =
			GetDatabaseVersion(
				connection);

		if (version == 0)
		{
			CreateCurrentSchema(
				connection);

			SetDatabaseVersion(
				connection,
				DatabaseVersion.CurrentVersion);

			return;
		}

		ApplyMigrations(
			connection,
			version);
	}

	private static void CreateDatabaseInfoTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS DatabaseInfo
		(
			Version INTEGER NOT NULL
		);
		""";

		command.ExecuteNonQuery();
	}

	private static int GetDatabaseVersion(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		SELECT Version
		FROM DatabaseInfo
		LIMIT 1;
		""";

		var result =
			command.ExecuteScalar();

		if (result is null)
		{
			return 0;
		}

		return Convert.ToInt32(
			result);
	}

	private static void SetDatabaseVersion(
		SqliteConnection connection,
		int version)
	{
		using var deleteCommand =
			connection.CreateCommand();

		deleteCommand.CommandText =
		"""
		DELETE FROM DatabaseInfo;
		""";

		deleteCommand.ExecuteNonQuery();

		using var insertCommand =
			connection.CreateCommand();

		insertCommand.CommandText =
		"""
		INSERT INTO DatabaseInfo
		(
			Version
		)
		VALUES
		(
			$Version
		);
		""";

		insertCommand.Parameters.AddWithValue(
			"$Version",
			version);

		insertCommand.ExecuteNonQuery();
	}

	private static void CreateCurrentSchema(
		SqliteConnection connection)
	{
		CreateItemsTable(
			connection);

		CreatePurposesTable(
			connection);

		CreateWarehousesTable(
			connection);

		CreateStorageLocationsTable(
			connection);

		CreateInventoriesTable(
			connection);

		CreateStockMovementsTable(
			connection);

		CreateStockMovementIndexes(
			connection);

		CreateUsersTable(
			connection);

		CreateAuditEntriesTable(
			connection);

		CreateDefaultPurpose(
			connection);

		CreateDefaultWarehouseStructure(
			connection);

		CreateDefaultAdministrator(
			connection);
	}

	private static void CreateItemsTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Items
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			PartNumber      TEXT NOT NULL UNIQUE,
			Description     TEXT NOT NULL,
			Manufacturer    TEXT,
			Category        TEXT,
			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreatePurposesTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Purposes
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			Name            TEXT NOT NULL UNIQUE,
			Description     TEXT,
			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateInventoriesTable(SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Inventories
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,

			ItemId          INTEGER NOT NULL,

			PurposeId       INTEGER NOT NULL,

			StorageLocationId INTEGER NOT NULL,

			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1,

			UNIQUE
			(
				ItemId,
				PurposeId,
				StorageLocationId
			),

			FOREIGN KEY(ItemId)
				REFERENCES Items(Id),

			FOREIGN KEY(PurposeId)
				REFERENCES Purposes(Id),

			FOREIGN KEY(StorageLocationId)
				REFERENCES StorageLocations(Id)
		);
		""";

		command.ExecuteNonQuery();
	}


	private static void CreateLocationsTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Locations
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			Name            TEXT NOT NULL UNIQUE,
			Description     TEXT,
			IsActive        INTEGER NOT NULL DEFAULT 1,
			Version         INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateDefaultLocation(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT OR IGNORE INTO Locations
		(
			Name,
			Description,
			IsActive
		)
		VALUES
		(
			'Warehouse',
			'Default warehouse location',
			1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateStockMovementsTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS StockMovements
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,

			InventoryId         INTEGER NOT NULL,

			MovementType        INTEGER NOT NULL,

			TimestampUtc        TEXT NOT NULL,

			Quantity            INTEGER NOT NULL,

			UnitPrice           REAL NULL,

			Reference           TEXT NULL,

			Notes               TEXT NULL,

			FOREIGN KEY(InventoryId)
				REFERENCES Inventories(Id)
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateWarehousesTable(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Warehouses
		(
			Id          INTEGER PRIMARY KEY AUTOINCREMENT,
			Name        TEXT NOT NULL UNIQUE,
			Description TEXT NULL,
			IsActive    INTEGER NOT NULL DEFAULT 1,
			Version     INTEGER NOT NULL DEFAULT 1
		);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateStorageLocationsTable(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS StorageLocations
		(
			Id          INTEGER PRIMARY KEY AUTOINCREMENT,
			WarehouseId INTEGER NOT NULL,
			Name        TEXT NOT NULL,
			Description TEXT NULL,
			IsActive    INTEGER NOT NULL DEFAULT 1,
			Version     INTEGER NOT NULL DEFAULT 1,
			UNIQUE(WarehouseId, Name),
			FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id)
		);
		CREATE INDEX IF NOT EXISTS IX_StorageLocations_WarehouseId_Name
			ON StorageLocations(WarehouseId, Name);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateDefaultWarehouseStructure(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		INSERT OR IGNORE INTO Warehouses (Name, Description, IsActive)
		VALUES ('Main Warehouse', 'Default Depot warehouse', 1);

		INSERT OR IGNORE INTO StorageLocations (WarehouseId, Name, Description, IsActive)
		SELECT Id, 'Default', 'Default storage location', 1
		FROM Warehouses
		WHERE Name = 'Main Warehouse';
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateStockMovementIndexes(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE INDEX IF NOT EXISTS IX_StockMovements_InventoryId_TimestampUtc
			ON StockMovements
			(
				InventoryId,
				TimestampUtc
			);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateUsersTable(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS Users
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			Email               TEXT NOT NULL COLLATE NOCASE UNIQUE,
			DisplayName         TEXT NOT NULL,
			PasswordHash        TEXT NOT NULL,
			IsAdministrator     INTEGER NOT NULL DEFAULT 0,
			IsActive            INTEGER NOT NULL DEFAULT 1,
			CreatedUtc          TEXT NOT NULL,
			Version             INTEGER NOT NULL DEFAULT 1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateAuditEntriesTable(
		SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		CREATE TABLE IF NOT EXISTS AuditEntries
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			TimestampUtc    TEXT NOT NULL,
			UserId          INTEGER NULL,
			UserEmail       TEXT NOT NULL,
			EntityType      TEXT NOT NULL,
			EntityId        INTEGER NOT NULL,
			Action          TEXT NOT NULL,
			BeforeJson      TEXT NULL,
			AfterJson       TEXT NULL,
			FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE SET NULL
		);

		CREATE INDEX IF NOT EXISTS IX_AuditEntries_TimestampUtc
			ON AuditEntries(TimestampUtc DESC);

		CREATE INDEX IF NOT EXISTS IX_AuditEntries_Entity
			ON AuditEntries(EntityType, EntityId, TimestampUtc DESC);
		""";
		command.ExecuteNonQuery();
	}

	private static void CreateDefaultPurpose(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT OR IGNORE INTO Purposes
		(
			Name,
			Description,
			IsActive
		)
		VALUES
		(
			'Stock',
			'Default stock purpose',
			1
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void CreateDefaultAdministrator(
		SqliteConnection connection)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT OR IGNORE INTO Users
		(
			Email,
			DisplayName,
			PasswordHash,
			IsAdministrator,
			IsActive,
			CreatedUtc
		)
		VALUES
		(
			'admin@depot.local',
			'Administrator',
			'pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=',
			1,
			1,
			strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
		);
		""";

		command.ExecuteNonQuery();
	}

	private static void ApplyMigrations(
		SqliteConnection connection,
		int version)
	{
		var migratedVersion =
			version;

		if (migratedVersion < 3)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is older than the supported migration baseline '3'. Delete depot.db and import the Excel file again.");
		}

		if (migratedVersion is 3 or 4 or 5)
		{
			CreateUsersTable(
				connection);

			if (TableHasColumn(
				connection,
				"Users",
				"UserName"))
			{
				MigrateUsersToEmailAuthentication(
					connection);
			}

			CreateDefaultAdministrator(
				connection);

			SetDatabaseVersion(
				connection,
				6);

			migratedVersion =
				6;
		}

		if (migratedVersion == 6)
		{
			MigrateStockMovementsToInventory(connection);

			SetDatabaseVersion(
				connection,
				7);

			migratedVersion =
				7;
		}

		if (migratedVersion == 7)
		{
			MigrateToAuditAndConcurrency(connection);
			SetDatabaseVersion(connection, 8);
			migratedVersion = 8;
		}

		if (migratedVersion == 8)
		{
			MigrateToWarehouseStructure(connection);
			SetDatabaseVersion(connection, 9);
			migratedVersion = 9;
		}

		if (migratedVersion < DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"Database version '{migratedVersion}' is older than the current schema version '{DatabaseVersion.CurrentVersion}'. Delete depot.db and import the Excel file again.");
		}

		if (migratedVersion > DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is newer than the supported schema version '{DatabaseVersion.CurrentVersion}'.");
		}
	}

	private static void MigrateToWarehouseStructure(SqliteConnection connection)
	{
		using (var disableForeignKeys = connection.CreateCommand())
		{
			disableForeignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
			disableForeignKeys.ExecuteNonQuery();
		}

		try
		{
			using var transaction = connection.BeginTransaction();
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText =
			"""
			CREATE TABLE Warehouses
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				Name TEXT NOT NULL UNIQUE,
				Description TEXT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1
			);

			INSERT INTO Warehouses (Name, Description, IsActive)
			VALUES ('Main Warehouse', 'Migrated default warehouse', 1);

			CREATE TABLE StorageLocations
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				WarehouseId INTEGER NOT NULL,
				Name TEXT NOT NULL,
				Description TEXT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1,
				UNIQUE(WarehouseId, Name),
				FOREIGN KEY(WarehouseId) REFERENCES Warehouses(Id)
			);

			INSERT INTO StorageLocations (Id, WarehouseId, Name, Description, IsActive, Version)
			SELECT l.Id, w.Id, l.Name, l.Description, l.IsActive, l.Version
			FROM Locations l
			CROSS JOIN Warehouses w
			WHERE w.Name = 'Main Warehouse';

			ALTER TABLE StockMovements RENAME TO StockMovementsWarehouseMigration;
			ALTER TABLE Inventories RENAME TO InventoriesWarehouseMigration;

			CREATE TABLE Inventories
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				ItemId INTEGER NOT NULL,
				PurposeId INTEGER NOT NULL,
				StorageLocationId INTEGER NOT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1,
				UNIQUE(ItemId, PurposeId, StorageLocationId),
				FOREIGN KEY(ItemId) REFERENCES Items(Id),
				FOREIGN KEY(PurposeId) REFERENCES Purposes(Id),
				FOREIGN KEY(StorageLocationId) REFERENCES StorageLocations(Id)
			);

			INSERT INTO Inventories (Id, ItemId, PurposeId, StorageLocationId, IsActive, Version)
			SELECT Id, ItemId, PurposeId, LocationId, IsActive, Version
			FROM InventoriesWarehouseMigration;

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
			FROM StockMovementsWarehouseMigration;

			DROP TABLE StockMovementsWarehouseMigration;
			DROP TABLE InventoriesWarehouseMigration;
			DROP TABLE Locations;

			CREATE INDEX IX_StorageLocations_WarehouseId_Name
				ON StorageLocations(WarehouseId, Name);
			CREATE INDEX IX_StockMovements_InventoryId_TimestampUtc
				ON StockMovements(InventoryId, TimestampUtc);
			""";
			command.ExecuteNonQuery();
			transaction.Commit();
		}
		finally
		{
			using var enableForeignKeys = connection.CreateCommand();
			enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
			enableForeignKeys.ExecuteNonQuery();
		}
	}

	private static void MigrateUsersToEmailAuthentication(SqliteConnection connection)
	{
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
		"""
		ALTER TABLE Users RENAME TO UsersLegacy;

		CREATE TABLE Users
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			Email               TEXT NOT NULL COLLATE NOCASE UNIQUE,
			DisplayName         TEXT NOT NULL,
			PasswordHash        TEXT NOT NULL,
			IsAdministrator     INTEGER NOT NULL DEFAULT 0,
			IsActive            INTEGER NOT NULL DEFAULT 1,
			CreatedUtc          TEXT NOT NULL
		);

		INSERT INTO Users
		(
			Id,
			Email,
			DisplayName,
			PasswordHash,
			IsAdministrator,
			IsActive,
			CreatedUtc
		)
		SELECT
			Id,
			CASE
				WHEN instr(UserName, '@') > 0 THEN lower(trim(UserName))
				ELSE lower(trim(UserName)) || '@depot.local'
			END,
			DisplayName,
			'pbkdf2-sha256$210000$9vL0kVt/HZBUCpsJYjPW6Q==$B1lZ+NRxxR/E8kwIE5PK0wXR2BPDmFTeLiKYyAEuhaE=',
			IsAdministrator,
			IsActive,
			CreatedUtc
		FROM UsersLegacy;

		DROP TABLE UsersLegacy;
		""";
		command.ExecuteNonQuery();
		transaction.Commit();
	}

	private static void MigrateStockMovementsToInventory(
		SqliteConnection connection)
	{
		var hasLegacyItemId =
			TableHasColumn(
				connection,
				"StockMovements",
				"ItemId");

		using var transaction =
			connection.BeginTransaction();

		using var command =
			connection.CreateCommand();

		command.Transaction =
			transaction;

		command.CommandText =
			hasLegacyItemId
				? GetLegacyStockMovementMigrationSql()
				: GetCurrentStockMovementMigrationSql();

		command.ExecuteNonQuery();
		transaction.Commit();

		CreateStockMovementIndexes(
			connection);
	}

	private static void MigrateToAuditAndConcurrency(
		SqliteConnection connection)
	{
		var tables =
			new[]
			{
				"Items",
				"Purposes",
				"Locations",
				"Inventories",
				"Users"
			};

		var tablesWithoutVersion =
			tables
				.Where(table => !TableHasColumn(connection, table, "Version"))
				.ToList();

		using var transaction = connection.BeginTransaction();

		foreach (var table in tablesWithoutVersion)
		{
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText =
				$"ALTER TABLE {table} ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;";
			command.ExecuteNonQuery();
		}

		transaction.Commit();
		CreateAuditEntriesTable(connection);
	}

	private static bool TableHasColumn(
		SqliteConnection connection,
		string tableName,
		string columnName)
	{
		using var command =
			connection.CreateCommand();

		command.CommandText =
			$"PRAGMA table_info({tableName});";

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			if (string.Equals(
				reader.GetString(1),
				columnName,
				StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static string GetLegacyStockMovementMigrationSql()
	{
		return
		"""
		INSERT OR IGNORE INTO Purposes (Name, Description, IsActive)
		VALUES ('Stock', 'Default stock purpose', 1);

		INSERT OR IGNORE INTO Locations (Name, Description, IsActive)
		VALUES ('Warehouse', 'Default warehouse location', 1);

		INSERT OR IGNORE INTO Inventories
		(
			ItemId,
			PurposeId,
			LocationId,
			IsActive
		)
		SELECT DISTINCT
			sm.ItemId,
			p.Id,
			l.Id,
			1
		FROM StockMovements sm
		INNER JOIN Purposes p
			ON p.Name = 'Stock'
		INNER JOIN Locations l
			ON l.Name = 'Warehouse'
		WHERE
			sm.InventoryId IS NULL
			AND NOT EXISTS
			(
				SELECT 1
				FROM Inventories inv
				WHERE inv.ItemId = sm.ItemId
			);

		ALTER TABLE StockMovements RENAME TO StockMovementsLegacy;

		CREATE TABLE StockMovements
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			InventoryId         INTEGER NOT NULL,
			MovementType        INTEGER NOT NULL,
			TimestampUtc        TEXT NOT NULL,
			Quantity            INTEGER NOT NULL,
			UnitPrice           REAL NULL,
			Reference           TEXT NULL,
			Notes               TEXT NULL,
			FOREIGN KEY(InventoryId)
				REFERENCES Inventories(Id)
		);

		INSERT INTO StockMovements
		(
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		)
		SELECT
			sm.Id,
			COALESCE
			(
				(
					SELECT inv.Id
					FROM Inventories inv
					WHERE
						inv.Id = sm.InventoryId
						AND inv.ItemId = sm.ItemId
				),
				(
					SELECT inv.Id
					FROM Inventories inv
					LEFT JOIN Purposes p
						ON p.Id = inv.PurposeId
					LEFT JOIN Locations l
						ON l.Id = inv.LocationId
					WHERE inv.ItemId = sm.ItemId
					ORDER BY
						CASE
							WHEN p.Name = 'Stock' AND l.Name = 'Warehouse' THEN 0
							ELSE 1
						END,
						inv.Id
					LIMIT 1
				)
			),
			sm.MovementType,
			sm.TimestampUtc,
			sm.Quantity,
			sm.UnitPrice,
			sm.Reference,
			sm.Notes
		FROM StockMovementsLegacy sm;

		DROP TABLE StockMovementsLegacy;
		""";
	}

	private static string GetCurrentStockMovementMigrationSql()
	{
		return
		"""
		ALTER TABLE StockMovements RENAME TO StockMovementsLegacy;

		CREATE TABLE StockMovements
		(
			Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
			InventoryId         INTEGER NOT NULL,
			MovementType        INTEGER NOT NULL,
			TimestampUtc        TEXT NOT NULL,
			Quantity            INTEGER NOT NULL,
			UnitPrice           REAL NULL,
			Reference           TEXT NULL,
			Notes               TEXT NULL,
			FOREIGN KEY(InventoryId)
				REFERENCES Inventories(Id)
		);

		INSERT INTO StockMovements
		(
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		)
		SELECT
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		FROM StockMovementsLegacy;

		DROP TABLE StockMovementsLegacy;
		""";
	}
}
