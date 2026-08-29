// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceMigrationCompatibilityTests
{
	[Fact]
	public void LegacyFeatureVersionTableWithoutTimestampMigratesFromThreeToCurrentAndRepeatedStartupIsIdempotent()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-finance-migration-{Guid.NewGuid():N}.db");
		try
		{
			using (var connection = new SqliteConnection($"Data Source={path}"))
			{
				connection.Open();
				using var command = connection.CreateCommand();
				command.CommandText = "CREATE TABLE DepotFeatureVersions (Name TEXT PRIMARY KEY, Version INTEGER NOT NULL); INSERT INTO DepotFeatureVersions (Name,Version) VALUES ('Finance',3);";
				command.ExecuteNonQuery();
			}

			var factory = new SqliteConnectionFactory(path);
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			Assert.Equal(FinanceInventoryAccountingSchemaMigration.CurrentVersion, ReadFinanceVersion(path));

			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			Assert.Equal(FinanceInventoryAccountingSchemaMigration.CurrentVersion, ReadFinanceVersion(path));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try { File.Delete(path); } catch (IOException) { }
		}
	}

	private static int ReadFinanceVersion(string path)
	{
		using var connection = new SqliteConnection($"Data Source={path}");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';";
		return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
	}
}
