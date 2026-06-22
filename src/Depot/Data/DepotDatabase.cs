// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace Depot.Data;

public sealed class DepotDatabase
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public DepotDatabase(SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public void Initialize()
	{
		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		CreateTables(connection);
	}

	private static void CreateTables(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();

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

		CREATE TABLE IF NOT EXISTS StockMovements
		(
			Id              INTEGER PRIMARY KEY AUTOINCREMENT,
			ItemId          INTEGER NOT NULL,
			MovementType    INTEGER NOT NULL,
			MovementDate    TEXT NOT NULL,
			Quantity        INTEGER NOT NULL,
			UnitPrice       REAL,
			Vendor          TEXT,
			InvoiceNumber   TEXT,
			Notes           TEXT,

			FOREIGN KEY(ItemId)
				REFERENCES Items(Id)
		);
		""";

		command.ExecuteNonQuery();
	}

}