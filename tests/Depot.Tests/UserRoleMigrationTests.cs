// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class UserRoleMigrationTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-user-role-migration-{Guid.NewGuid():N}.db");

	[Fact]
	public void Version25MapsLegacyAdministratorAndApproverFlagsToFixedRoles()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		using (var connection = Open())
		{
			using var command = connection.CreateCommand();
			command.CommandText = "INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc) VALUES ('legacy-approver@test.local', 'Legacy Approver', 'unused', 0, 1, 0, 1, '2026-01-01T00:00:00Z'); ALTER TABLE Users DROP COLUMN Role; UPDATE DatabaseInfo SET Version = 25;";
			command.ExecuteNonQuery();
		}
		new DepotDatabase(factory).Initialize();
		using var migrated = Open();
		Assert.Equal(DatabaseVersion.CurrentVersion, Scalar(migrated, "SELECT Version FROM DatabaseInfo;"));
		Assert.Equal(1, Scalar(migrated, "SELECT Role FROM Users WHERE Email = 'admin@depot.local';"));
		Assert.Equal(3, Scalar(migrated, "SELECT Role FROM Users WHERE Email = 'legacy-approver@test.local';"));
	}

	private SqliteConnection Open() { var connection = new SqliteConnection($"Data Source={_path}"); connection.Open(); return connection; }
	private static int Scalar(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture); }
	public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); }
}
