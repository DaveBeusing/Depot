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

		CreateDatabaseInfoTable(connection);

		var version =
			GetDatabaseVersion(connection);

		if (version == 0)
		{
			CreateVersion1Schema(connection);

			SetDatabaseVersion(
				connection,
				DatabaseVersion.CurrentVersion);

			version =
				DatabaseVersion.CurrentVersion;
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

		return Convert.ToInt32(result);
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

	private static void CreateVersion1Schema(
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

	private static void ApplyMigrations(
		SqliteConnection connection,
		int version)
	{
		while (version < DatabaseVersion.CurrentVersion)
		{
			switch (version)
			{
				case 1:
				{
					version = 2;
					break;
				}

				default:
				{
					throw new InvalidOperationException(
						$"Unknown database version '{version}'.");
				}
			}
		}
	}
}