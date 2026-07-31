// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ReasonCodeMigrationTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-reason-code-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionFifteenReasonCodesReceiveStableKeysWithoutChangingReferences()
	{
		CreateVersionFifteenDatabase();

		new DepotDatabase(new SqliteConnectionFactory(_databasePath)).Initialize();

		using var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
		connection.Open();
		Assert.Equal(DatabaseVersion.CurrentVersion, Scalar(connection, "SELECT Version FROM DatabaseInfo;"));
		Assert.Equal(7L, Scalar(connection, "SELECT ReasonCodeId FROM StockMovements WHERE Id = 100;"));
		Assert.Equal(8L, Scalar(connection, "SELECT ReasonCodeId FROM StockMovements WHERE Id = 101;"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM ReasonCodes WHERE Id = 7 AND Code = 'GOODS_RECEIPT' AND IsSystem = 1 AND IsActive = 1;"));
		Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM ReasonCodes WHERE Id = 8 AND Code = 'LEGACY_000008' AND IsSystem = 0;"));
		Assert.Equal(10L, Scalar(connection, "SELECT COUNT(*) FROM ReasonCodes WHERE IsSystem = 1;"));
		Assert.Equal(10L, Scalar(connection, "SELECT COUNT(DISTINCT Code) FROM ReasonCodes WHERE IsSystem = 1;"));
	}

	[Fact]
	public void CurrentSchemaContainsUniqueSystemCodes()
	{
		new DepotDatabase(new SqliteConnectionFactory(_databasePath)).Initialize();

		using var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
		connection.Open();
		Assert.Equal(10L, Scalar(connection, "SELECT COUNT(*) FROM ReasonCodes WHERE IsSystem = 1 AND IsActive = 1;"));
		Assert.Equal(10L, Scalar(connection, "SELECT COUNT(DISTINCT Code) FROM ReasonCodes;"));
		Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM ReasonCodes WHERE Code = '{ReasonCodeSystemCodes.GoodsReceipt}';"));
	}

	private void CreateVersionFifteenDatabase()
	{
		using var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
			"""
			CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL);
			INSERT INTO DatabaseInfo (Version) VALUES (15);
			CREATE TABLE ReasonCodes
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				Name TEXT NOT NULL UNIQUE,
				Description TEXT NULL,
				IsActive INTEGER NOT NULL DEFAULT 1,
				Version INTEGER NOT NULL DEFAULT 1
			);
			INSERT INTO ReasonCodes (Id, Name, IsActive) VALUES (7, 'Goods Receipt', 0);
			INSERT INTO ReasonCodes (Id, Name, IsActive) VALUES (8, 'Customer-specific reason', 1);
			CREATE TABLE StockMovements
			(
				Id INTEGER PRIMARY KEY,
				ReasonCodeId INTEGER NULL,
				FOREIGN KEY(ReasonCodeId) REFERENCES ReasonCodes(Id)
			);
			INSERT INTO StockMovements (Id, ReasonCodeId) VALUES (100, 7), (101, 8);
			""";
		command.ExecuteNonQuery();
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
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
