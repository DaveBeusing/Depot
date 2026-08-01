// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class PurchaseOrderApprovalMigrationTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-approval-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionTwentyIsMigratedWithoutChangingExistingOrderStatus()
	{
		using (var connection = new SqliteConnection($"Data Source={_path}"))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				CREATE TABLE DatabaseInfo (Version INTEGER NOT NULL);
				INSERT INTO DatabaseInfo (Version) VALUES (20);
				CREATE TABLE Users
				(
					Id INTEGER PRIMARY KEY, Email TEXT NOT NULL, DisplayName TEXT NOT NULL,
					PasswordHash TEXT NOT NULL, IsAdministrator INTEGER NOT NULL DEFAULT 0,
					IsActive INTEGER NOT NULL DEFAULT 1, CreatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1
				);
				CREATE TABLE PurchaseOrders
				(
					Id INTEGER PRIMARY KEY, OrderNumber TEXT NOT NULL, SupplierId INTEGER NOT NULL,
					OrderDate TEXT NOT NULL, Status INTEGER NOT NULL, Version INTEGER NOT NULL DEFAULT 1
				);
				INSERT INTO PurchaseOrders (Id, OrderNumber, SupplierId, OrderDate, Status) VALUES (1, 'PO-000001', 1, '2026-01-01', 2);
				""";
			command.ExecuteNonQuery();
		}

		new DepotDatabase(new SqliteConnectionFactory(_path)).Initialize();

		using var migrated = new SqliteConnection($"Data Source={_path}");
		migrated.Open();
		using var versionCommand = migrated.CreateCommand();
		versionCommand.CommandText = "SELECT Version FROM DatabaseInfo;";
		Assert.Equal(21L, (long)(versionCommand.ExecuteScalar() ?? 0L));
		using var statusCommand = migrated.CreateCommand();
		statusCommand.CommandText = "SELECT Status FROM PurchaseOrders WHERE Id = 1;";
		Assert.Equal(2L, (long)(statusCommand.ExecuteScalar() ?? 0L));
		Assert.True(HasColumn(migrated, "Users", "CanApprovePurchaseOrders"));
		Assert.True(HasColumn(migrated, "PurchaseOrders", "CreatedByUserId"));
		Assert.True(HasColumn(migrated, "PurchaseOrders", "ApprovalDecisionAtUtc"));
	}

	private static bool HasColumn(SqliteConnection connection, string table, string column)
	{
		using var command = connection.CreateCommand();
		command.CommandText = $"PRAGMA table_info({table});";
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
