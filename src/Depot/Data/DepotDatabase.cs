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
			CreateVersion3Schema(
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

	private static void CreateVersion3Schema(
		SqliteConnection connection)
	{
		CreateItemsTable(
			connection);

		CreatePurposesTable(
			connection);

		CreateInventoriesTable(
			connection);

		CreateStockMovementsTable(
			connection);

		CreateDefaultPurpose(
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

	private static void CreateInventoriesTable(
		SqliteConnection connection)
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
			IsActive        INTEGER NOT NULL DEFAULT 1,

			UNIQUE(ItemId, PurposeId),

			FOREIGN KEY(ItemId)
				REFERENCES Items(Id),

			FOREIGN KEY(PurposeId)
				REFERENCES Purposes(Id)
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

			ItemId              INTEGER NOT NULL,

			MovementType        INTEGER NOT NULL,

			TimestampUtc        TEXT NOT NULL,

			Quantity            INTEGER NOT NULL,

			UnitPrice           REAL NULL,

			Reference           TEXT NULL,

			Notes               TEXT NULL,

			FOREIGN KEY(ItemId)
				REFERENCES Items(Id)
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

	private static void ApplyMigrations(
		SqliteConnection connection,
		int version)
	{
		if (version < DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is older than the current schema version '{DatabaseVersion.CurrentVersion}'. Delete depot.db and import the Excel file again.");
		}

		if (version > DatabaseVersion.CurrentVersion)
		{
			throw new InvalidOperationException(
				$"Database version '{version}' is newer than the supported schema version '{DatabaseVersion.CurrentVersion}'.");
		}
	}
}