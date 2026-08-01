// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class MaterialReturnMigrationTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-material-return-migration-{Guid.NewGuid():N}.db");
	[Fact]
	public void Version23MigratesMaterialReturnSchema()
	{
		using (var connection = new SqliteConnection($"Data Source={_path}")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL); INSERT INTO DatabaseInfo (Version) VALUES (23); CREATE TABLE MaterialIssues (Id INTEGER PRIMARY KEY);"; command.ExecuteNonQuery(); }
		new DepotDatabase(new SqliteConnectionFactory(_path)).Initialize();
		using var migrated = new SqliteConnection($"Data Source={_path}"); migrated.Open();
		Assert.Equal(DatabaseVersion.CurrentVersion, Scalar(migrated, "SELECT Version FROM DatabaseInfo;"));
		Assert.Equal(1, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MaterialReturns';"));
		Assert.Equal(1, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MaterialReturnLines';"));
	}
	private static int Scalar(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture); }
	public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); }
}
