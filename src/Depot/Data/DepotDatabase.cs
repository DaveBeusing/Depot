// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace Depot.Data;

public sealed class DepotDatabase
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

		connection.Open();

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

		CreateLocationsTable(
			connection);

		CreateInventoriesTable(
			connection);

		CreateStockMovementsTable(
			connection);

		CreateUsersTable(
			connection);

		CreateDefaultPurpose(
			connection);

		CreateDefaultLocation(
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
			IsActive        INTEGER NOT NULL DEFAULT 1
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
			IsActive        INTEGER NOT NULL DEFAULT 1
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

			LocationId      INTEGER NOT NULL,

			IsActive        INTEGER NOT NULL DEFAULT 1,

			UNIQUE
			(
				ItemId,
				PurposeId,
				LocationId
			),

			FOREIGN KEY(ItemId)
				REFERENCES Items(Id),

			FOREIGN KEY(PurposeId)
				REFERENCES Purposes(Id),

			FOREIGN KEY(LocationId)
				REFERENCES Locations(Id)
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
			IsActive        INTEGER NOT NULL DEFAULT 1
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
			CreatedUtc          TEXT NOT NULL
		);
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

		if (migratedVersion < 4)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is older than the supported migration baseline '4'. Delete depot.db and import the Excel file again.");
		}

		if (migratedVersion == 4)
		{
			CreateUsersTable(
				connection);

			CreateDefaultAdministrator(
				connection);

			SetDatabaseVersion(
				connection,
				5);

			migratedVersion =
				5;
		}

		if (migratedVersion == 5)
		{
			MigrateUsersToEmailAuthentication(connection);

			SetDatabaseVersion(
				connection,
				6);

			migratedVersion =
				6;
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
}
