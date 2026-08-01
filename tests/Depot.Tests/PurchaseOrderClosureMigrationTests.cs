// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class PurchaseOrderClosureMigrationTests
{
	[Fact]
	public void Version21DatabaseMigratesPurchaseOrderClosureMetadata()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-po-closure-migration-{Guid.NewGuid():N}.db");
		try
		{
			CreateVersion21Database(path);
			new DepotDatabase(new SqliteConnectionFactory(path)).Initialize();

			using var connection = new SqliteConnection($"Data Source={path}");
			connection.Open();
			Assert.Equal(DatabaseVersion.CurrentVersion, Scalar(connection, "SELECT Version FROM DatabaseInfo;"));
			Assert.Equal(3, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('PurchaseOrders') WHERE name IN ('ClosedByUserId', 'ClosedAtUtc', 'CloseReason');"));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	private static void CreateVersion21Database(string path)
	{
		using var connection = new SqliteConnection($"Data Source={path}");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
			"""
			CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL);
			INSERT INTO DatabaseInfo (Version) VALUES (21);
			CREATE TABLE Users
			(
				Id INTEGER PRIMARY KEY, Email TEXT NOT NULL, DisplayName TEXT NOT NULL,
				PasswordHash TEXT NOT NULL, IsAdministrator INTEGER NOT NULL DEFAULT 0,
				CanApprovePurchaseOrders INTEGER NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1,
				CreatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1
			);
			CREATE TABLE PurchaseOrders
			(
				Id INTEGER PRIMARY KEY, OrderNumber TEXT NOT NULL, SupplierId INTEGER NOT NULL,
				OrderDate TEXT NOT NULL, Status INTEGER NOT NULL, CreatedByUserId INTEGER NULL,
				SubmittedByUserId INTEGER NULL, SubmittedAtUtc TEXT NULL, ApprovalDecisionByUserId INTEGER NULL,
				ApprovalDecisionAtUtc TEXT NULL, ApprovalComment TEXT NULL, Version INTEGER NOT NULL DEFAULT 1
			);
			""";
		command.ExecuteNonQuery();
	}

	private static int Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
	}
}
