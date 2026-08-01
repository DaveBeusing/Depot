// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class InventoryCountMigrationTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-count-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionEighteenMigratesToInventoryCountSchema()
	{
		using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				PRAGMA foreign_keys = ON;
				CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL);
				INSERT INTO DatabaseInfo (Version) VALUES (18);
				CREATE TABLE Warehouses (Id INTEGER PRIMARY KEY);
				CREATE TABLE Users (Id INTEGER PRIMARY KEY);
				CREATE TABLE Inventories (Id INTEGER PRIMARY KEY);
				""";
			command.ExecuteNonQuery();
		}

		new DepotDatabase(new SqliteConnectionFactory(_databasePath)).Initialize();

		using var migrated = new SqliteConnection($"Data Source={_databasePath}");
		migrated.Open();
		Assert.Equal((long)DatabaseVersion.CurrentVersion, Scalar(migrated, "SELECT Version FROM DatabaseInfo;"));
		Assert.Equal(1L, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'InventoryCounts';"));
		Assert.Equal(1L, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'InventoryCountLines';"));
		Assert.Equal(1L, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_InventoryCounts_WarehouseId_Status';"));
		Assert.Equal(1L, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_InventoryCountLines_InventoryId';"));
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}

	private static long Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt64(command.ExecuteScalar());
	}
}
