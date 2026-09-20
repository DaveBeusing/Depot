// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("SQLite schema initialization")]
public sealed class DatabaseProvisioningFastPathTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-fast-path-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;

	public DatabaseProvisioningFastPathTests()
	{
		_factory = new SqliteConnectionFactory(_path);
	}

	[Fact]
	public void CurrentSchemaUsesFastPath()
	{
		Assert.Equal(DatabaseProvisioningPath.FullProvisioning, DatabaseProvisioningService.InitializeCore(_factory));
		Assert.True(DatabaseSchemaStateInspector.IsCurrent(_factory));

		Assert.Equal(DatabaseProvisioningPath.FastPath, DatabaseProvisioningService.InitializeCore(_factory));
	}

	[Fact]
	public void OutdatedCoreSchemaUsesAuthoritativeMigrationPath()
	{
		DatabaseProvisioningService.Initialize(_factory);
		Execute("UPDATE DatabaseInfo SET Version=29;");

		Assert.False(DatabaseSchemaStateInspector.IsCurrent(_factory));
		Assert.Equal(DatabaseProvisioningPath.FullProvisioning, DatabaseProvisioningService.InitializeCore(_factory));
		Assert.Equal(DatabaseVersion.CurrentVersion, Scalar("SELECT Version FROM DatabaseInfo;"));
	}

	[Fact]
	public void MissingFeatureMetadataFailsClosedIntoFullProvisioning()
	{
		DatabaseProvisioningService.Initialize(_factory);
		Execute("DROP TABLE DepotFeatureVersions;");

		Assert.False(DatabaseSchemaStateInspector.IsCurrent(_factory));
		Assert.Equal(DatabaseProvisioningPath.FullProvisioning, DatabaseProvisioningService.InitializeCore(_factory));
		Assert.True(DatabaseSchemaStateInspector.IsCurrent(_factory));
	}

	[Fact]
	public void MissingUnversionedItemStructureFallsBackAndRepairs()
	{
		DatabaseProvisioningService.Initialize(_factory);
		Execute("DROP TABLE StockMovementTracking;");

		Assert.False(DatabaseSchemaStateInspector.IsCurrent(_factory));
		Assert.Equal(DatabaseProvisioningPath.FullProvisioning, DatabaseProvisioningService.InitializeCore(_factory));
		Assert.True(DatabaseSchemaStateInspector.IsCurrent(_factory));
	}

	[Fact]
	public void MissingReferenceDefaultFallsBackAndRepairs()
	{
		DatabaseProvisioningService.Initialize(_factory);
		Execute("DELETE FROM UnitsOfMeasure WHERE Name='EA';");

		Assert.False(DatabaseSchemaStateInspector.IsCurrent(_factory));
		Assert.Equal(DatabaseProvisioningPath.FullProvisioning, DatabaseProvisioningService.InitializeCore(_factory));
		Assert.True(DatabaseSchemaStateInspector.IsCurrent(_factory));
	}

	[Fact]
	public void StaleSystemRolePermissionsFallBackAndRepair()
	{
		DatabaseProvisioningService.Initialize(_factory);
		Execute("DELETE FROM RolePermissions WHERE RoleId=(SELECT Id FROM Roles WHERE Code='ADMINISTRATOR') AND PermissionId=(SELECT MIN(Id) FROM Permissions);");

		Assert.False(DatabaseSchemaStateInspector.IsCurrent(_factory));
		Assert.Equal(DatabaseProvisioningPath.FullProvisioning, DatabaseProvisioningService.InitializeCore(_factory));
		Assert.True(DatabaseSchemaStateInspector.IsCurrent(_factory));
	}

	[Fact]
	public void FutureFeatureVersionIsNeverAcceptedByFastPath()
	{
		DatabaseProvisioningService.Initialize(_factory);
		Execute($"UPDATE DepotFeatureVersions SET Version={SalesSchemaMigration.CurrentVersion + 1} WHERE Name='Sales';");

		Assert.False(DatabaseSchemaStateInspector.IsCurrent(_factory));
		var exception = Assert.Throws<InvalidOperationException>(() => DatabaseProvisioningService.InitializeCore(_factory));
		Assert.Contains("newer than the supported version", exception.Message, StringComparison.Ordinal);
	}

	private void Execute(string sql)
	{
		using var connection = new SqliteConnection($"Data Source={_path}");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	private int Scalar(string sql)
	{
		using var connection = new SqliteConnection($"Data Source={_path}");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
