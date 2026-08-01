// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class MovementReversalMigrationTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-reversal-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionNineteenAddsReversalMetadataWithoutReplacingExistingTables()
	{
		using (var connection = new SqliteConnection($"Data Source={_path}"))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText =
			"""
			CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL);
			INSERT INTO DatabaseInfo (Version) VALUES (19);
			CREATE TABLE Users (Id INTEGER PRIMARY KEY);
			CREATE TABLE StockMovements (Id INTEGER PRIMARY KEY, InventoryId INTEGER NOT NULL, ReasonCodeId INTEGER NULL, MovementType INTEGER NOT NULL, TimestampUtc TEXT NOT NULL, Quantity INTEGER NOT NULL, UnitPrice REAL NULL, Reference TEXT NULL, Notes TEXT NULL);
			CREATE TABLE GoodsReceipts (Id INTEGER PRIMARY KEY);
			CREATE TABLE StockTransfers (Id INTEGER PRIMARY KEY);
			CREATE TABLE InventoryCounts (Id INTEGER PRIMARY KEY);
			""";
			command.ExecuteNonQuery();
		}

		new DepotDatabase(new SqliteConnectionFactory(_path)).Initialize();

		using var migrated = new SqliteConnection($"Data Source={_path}");
		migrated.Open();
		Assert.Equal((long)DatabaseVersion.CurrentVersion, Scalar(migrated, "SELECT Version FROM DatabaseInfo;"));
		Assert.True(HasColumn(migrated, "StockMovements", "ReversalOfMovementId"));
		Assert.True(HasColumn(migrated, "StockMovements", "ReversedByUserId"));
		Assert.True(HasColumn(migrated, "GoodsReceipts", "Version"));
		Assert.True(HasColumn(migrated, "StockTransfers", "ReversalReason"));
		Assert.True(HasColumn(migrated, "InventoryCounts", "ReversedAtUtc"));
		Assert.Equal(1L, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'UX_StockMovements_ReversalOfMovementId';"));
	}

	private static bool HasColumn(SqliteConnection connection, string table, string column)
	{
		using var command = connection.CreateCommand();
		command.CommandText = $"PRAGMA table_info({table});";
		using var reader = command.ExecuteReader();
		while (reader.Read()) if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal)) return true;
		return false;
	}

	private static long Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt64(command.ExecuteScalar());
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
