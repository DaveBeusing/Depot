// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class RbacCatalogAtomicityTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-rbac-atomicity-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;

	public RbacCatalogAtomicityTests()
	{
		_factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(_factory).Initialize();
	}

	[Fact]
	public void FailedGrantRefreshPreservesCatalogAndAssignmentsAndCanRetry()
	{
		var expected = SnapshotCatalog();
		Execute("""
			UPDATE Permissions SET Name = 'Previous permission name' WHERE Id = (SELECT MIN(Id) FROM Permissions);
			UPDATE Roles SET Name = 'Previous role name' WHERE Id = (SELECT MIN(Id) FROM Roles);
			CREATE TRIGGER RejectRoleGrant BEFORE INSERT ON RolePermissions
			BEGIN
				SELECT RAISE(ABORT, 'injected role grant failure');
			END;
			""");
		var before = SnapshotCatalog();

		var exception = Assert.Throws<SqliteException>(() => new DepotDatabase(_factory).Initialize());

		Assert.Contains("injected role grant failure", exception.Message, StringComparison.Ordinal);
		Assert.Equal(before, SnapshotCatalog());
		Assert.Equal(DatabaseVersion.CurrentVersion, ReadVersion());

		Execute("DROP TRIGGER RejectRoleGrant;");
		new DepotDatabase(_factory).Initialize();

		Assert.Equal(expected, SnapshotCatalog());
	}

	[Fact]
	public void FailedLegacyAssignmentRollsBackCatalogAndLeavesMigrationRetryable()
	{
		var expected = SnapshotCatalog();
		Execute("""
			DELETE FROM UserRoles;
			UPDATE DatabaseInfo SET Version = 27;
			UPDATE Permissions SET Name = 'Previous permission name' WHERE Id = (SELECT MIN(Id) FROM Permissions);
			UPDATE Roles SET Name = 'Previous role name' WHERE Id = (SELECT MIN(Id) FROM Roles);
			CREATE TRIGGER RejectLegacyAssignment BEFORE INSERT ON UserRoles
			BEGIN
				SELECT RAISE(ABORT, 'injected user assignment failure');
			END;
			""");
		var before = SnapshotCatalog();

		var exception = Assert.Throws<SqliteException>(() => new DepotDatabase(_factory).Initialize());

		Assert.Contains("injected user assignment failure", exception.Message, StringComparison.Ordinal);
		Assert.Equal(before, SnapshotCatalog());
		Assert.Equal(27, ReadVersion());

		Execute("DROP TRIGGER RejectLegacyAssignment;");
		new DepotDatabase(_factory).Initialize();

		Assert.Equal(DatabaseVersion.CurrentVersion, ReadVersion());
		Assert.Equal(expected, SnapshotCatalog());
	}

	private string[] SnapshotCatalog()
	{
		var rows = new List<string>();
		using var connection = _factory.CreateConnection();
		connection.Open();
		foreach (var query in new[]
		{
			"SELECT Id, Code, Name, Module, Action FROM Permissions ORDER BY Id;",
			"SELECT Id, Code, Name, Description, IsSystem, IsActive, Version FROM Roles ORDER BY Id;",
			"SELECT RoleId, PermissionId FROM RolePermissions ORDER BY RoleId, PermissionId;",
			"SELECT UserId, RoleId FROM UserRoles ORDER BY UserId, RoleId;"
		})
		{
			rows.Add(query);
			using var command = connection.CreateCommand();
			command.CommandText = query;
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				rows.Add(string.Join("\u001f", Enumerable.Range(0, reader.FieldCount).Select(index =>
					reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
			}
		}
		return rows.ToArray();
	}

	private int ReadVersion()
	{
		using var connection = _factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DatabaseInfo;";
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private void Execute(string sql)
	{
		using var connection = _factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
