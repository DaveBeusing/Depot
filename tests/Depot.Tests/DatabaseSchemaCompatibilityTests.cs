// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json;

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class DatabaseSchemaCompatibilityTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqliteInspectsEveryRegisteredFeatureAndRejectsSameCoreDrift()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"depot-compatibility-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try { await VerifyProviderAsync(new SqliteConnectionFactory(Path.Combine(directory, "schema.db"))); }
		finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerInspectsEveryRegisteredFeatureAndRejectsSameCoreDrift() => VerifyProviderAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbInspectsEveryRegisteredFeatureAndRejectsSameCoreDrift() => VerifyProviderAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlInspectsEveryRegisteredFeatureAndRejectsSameCoreDrift() => VerifyProviderAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public async Task MySqlStructuralDefinitionsIncludeEnforcementDefaultsAndPrefixKeys()
	{
		var factory = new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings());
		DatabaseProvisioningService.Initialize(factory);
		using var connection = factory.CreateConnection();
		await connection.OpenAsync();
		var suffix = Guid.NewGuid().ToString("N");
		var table = "CompatibilityCheck_" + suffix;
		var constraint = "CK_Compatibility_" + suffix;
		await using var command = connection.CreateCommand();
		try
		{
			command.CommandText = $"CREATE TABLE {table}(ProbeValue INT,Gtin VARCHAR(14) NULL DEFAULT NULL,CONSTRAINT {constraint} CHECK(ProbeValue > 0)) ENGINE=InnoDB;";
			await command.ExecuteNonQueryAsync();
			var before = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			command.CommandText = $"ALTER TABLE {table} ALTER CHECK {constraint} NOT ENFORCED;";
			await command.ExecuteNonQueryAsync();
			var after = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.False(before.HasSameState(after));
			Assert.NotEqual(CheckDefinition(before, table, constraint), CheckDefinition(after, table, constraint));
			command.CommandText = $"ALTER TABLE {table} ALTER CHECK {constraint} ENFORCED;";
			await command.ExecuteNonQueryAsync();
			before = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			command.CommandText = $"ALTER TABLE {table} ALTER COLUMN Gtin SET DEFAULT '';";
			await command.ExecuteNonQueryAsync();
			after = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.False(before.HasSameState(after));
			Assert.NotEqual(before.TableDefinitions[table], after.TableDefinitions[table]);
			command.CommandText = $"CREATE UNIQUE INDEX IX_Prefix_Gtin ON {table}(Gtin(5));";
			await command.ExecuteNonQueryAsync();
			var prefix = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.Equal("Gtin PREFIX(5)", Assert.Single(prefix.Indexes[table].Single(index => index.Name == "IX_Prefix_Gtin").Columns));
			command.CommandText = $"ALTER TABLE {table} DROP CHECK {constraint};";
			await command.ExecuteNonQueryAsync();
			before = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			command.CommandText = $"ALTER TABLE {table} ENGINE=MyISAM;";
			await command.ExecuteNonQueryAsync();
			after = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.False(before.HasSameState(after));
			Assert.NotEqual(before.TableDefinitions[table], after.TableDefinitions[table]);
		}
		finally
		{
			command.CommandText = $"DROP TABLE IF EXISTS {table};";
			await command.ExecuteNonQueryAsync();
		}
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public async Task MariaDbCheckNamesAreScopedToTheirOwningTable()
	{
		var factory = new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings());
		DatabaseProvisioningService.Initialize(factory);
		using var connection = factory.CreateConnection();
		await connection.OpenAsync();
		var suffix = Guid.NewGuid().ToString("N");
		var tableA = "CompatibilityA_" + suffix;
		var tableB = "CompatibilityB_" + suffix;
		var constraint = "CK_Compatibility_" + suffix;
		await using var command = connection.CreateCommand();
		try
		{
			command.CommandText = $"CREATE TABLE {tableA}(ProbeValue INT,CONSTRAINT {constraint} CHECK(ProbeValue > 0));";
			await command.ExecuteNonQueryAsync();
			command.CommandText = $"CREATE TABLE {tableB}(ProbeValue INT,CONSTRAINT {constraint} CHECK(ProbeValue < 0));";
			await command.ExecuteNonQueryAsync();
			var actual = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			using var definitionsA = JsonDocument.Parse(CheckDefinition(actual, tableA, constraint));
			using var definitionsB = JsonDocument.Parse(CheckDefinition(actual, tableB, constraint));
			Assert.Equal(1, definitionsA.RootElement.GetArrayLength());
			Assert.Equal(1, definitionsB.RootElement.GetArrayLength());
			Assert.NotEqual(definitionsA.RootElement[0][2].GetString(), definitionsB.RootElement[0][2].GetString());
		}
		finally
		{
			command.CommandText = $"DROP TABLE IF EXISTS {tableA};";
			await command.ExecuteNonQueryAsync();
			command.CommandText = $"DROP TABLE IF EXISTS {tableB};";
			await command.ExecuteNonQueryAsync();
		}
	}

	[Fact]
	public void ExportedContractCoversEveryRegisteredFeatureAndUnversionedExtension()
	{
		var contract = DatabaseSchemaContract.Current;
		contract.Validate(requirement: true);
		Assert.Equal(14, contract.FeatureVersions.Count);
		Assert.Equal(DatabaseSchemaRegistry.CurrentFeatureVersions.OrderBy(pair => pair.Key), contract.FeatureVersions.OrderBy(pair => pair.Key));
		Assert.All(ItemMasterDataSchema.RequiredColumns, column => Assert.Contains(column, contract.Tables["Items"]));
		Assert.All(ItemTraceabilitySchema.RequiredTables, table => Assert.True(contract.Tables.ContainsKey(table)));
		Assert.All(ItemTraceabilitySchema.RequiredIndexes, index => Assert.Contains(contract.Indexes[index.Table], actual => actual.Name == index.Index));
	}

	private static async Task VerifyProviderAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		using var connection = factory.CreateConnection();
		await connection.OpenAsync();
		var current = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
		current.RequireCompatibleWith(DatabaseSchemaContract.Current);
		Assert.Equal(14, current.FeatureVersions.Count);
		Assert.True(current.HasSameState(await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None)));
		var salesVersion = current.FeatureVersions["Sales"];
		try
		{
			await SetSalesAsync(connection, salesVersion - 1);
			var older = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.Equal(current.CoreVersion, older.CoreVersion);
			Assert.True(older.RequiresMigrationTo(DatabaseSchemaContract.Current));
			Assert.Throws<InvalidOperationException>(() => older.RequireCompatibleWith(DatabaseSchemaContract.Current));
			Assert.False(current.HasSameState(older));

			await SetSalesAsync(connection, salesVersion + 1);
			var future = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.Throws<InvalidOperationException>(() => future.RequiresMigrationTo(DatabaseSchemaContract.Current));

			await using (var delete = connection.CreateCommand())
			{
				delete.CommandText = "DELETE FROM DepotFeatureVersions WHERE Name='Sales';";
				await delete.ExecuteNonQueryAsync();
			}
			var missing = await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None);
			Assert.True(missing.RequiresMigrationTo(DatabaseSchemaContract.Current));
			Assert.Throws<InvalidOperationException>(() => missing.RequireCompatibleWith(DatabaseSchemaContract.Current));
		}
		finally
		{
			await using var restore = connection.CreateCommand();
			restore.CommandText = "DELETE FROM DepotFeatureVersions WHERE Name='Sales';";
			await restore.ExecuteNonQueryAsync();
			restore.CommandText = "INSERT INTO DepotFeatureVersions(Name,Version) VALUES('Sales',@Version);";
			var parameter = restore.CreateParameter(); parameter.ParameterName = "@Version"; parameter.Value = salesVersion; restore.Parameters.Add(parameter);
			await restore.ExecuteNonQueryAsync();
		}
		Assert.True(current.HasSameState(await DatabaseCompatibilityReader.ReadAsync(connection, factory.Provider, CancellationToken.None)));
	}

	private static string CheckDefinition(DatabaseSchemaCompatibility compatibility, string table, string constraint) =>
		Assert.Single(compatibility.OtherDefinitions, pair =>
			string.Equals(pair.Key, $"check:{table}.{constraint}", StringComparison.OrdinalIgnoreCase)).Value;

	private static async Task SetSalesAsync(DbConnection connection, int version)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = "UPDATE DepotFeatureVersions SET Version=@Version WHERE Name='Sales';";
		var parameter = command.CreateParameter(); parameter.ParameterName = "@Version"; parameter.Value = version; command.Parameters.Add(parameter);
		await command.ExecuteNonQueryAsync();
	}
}
