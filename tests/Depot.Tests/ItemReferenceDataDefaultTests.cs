// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Xunit;

namespace Depot.Tests;

public sealed class ItemReferenceDataDefaultTests : IDisposable
{
	private static readonly string[] ExpectedUnits = ["EA", "SET", "PAIR", "M", "M2", "M3", "KG", "G", "L", "ML", "H", "DAY"];
	private static readonly string[] ExpectedPackagings = ["UNIT", "BAG", "BOX", "CARTON", "CASE", "PACK", "BUNDLE", "TRAY", "REEL", "ROLL", "CRATE", "PALLET"];
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-reference-defaults-{Guid.NewGuid():N}.db");

	[Fact]
	public void NewDatabaseContainsStandardUnitsAndPackagings()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();

		AssertDefaultSet(factory, "UnitsOfMeasure", ExpectedUnits);
		AssertDefaultSet(factory, "Packagings", ExpectedPackagings);
		Assert.Equal(0, CountByName(factory, "UnitsOfMeasure", "PCS"));
		AssertReference(factory, "UnitsOfMeasure", "EA", "Each", true);
		AssertReference(factory, "Packagings", "BOX", "Box", true);
	}

	[Fact]
	public void RepeatedInitializationDoesNotCreateDuplicates()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		var initializer = DatabaseProviderFactory.CreateInitializer(factory);
		initializer.Initialize();
		initializer.Initialize();

		AssertDefaultSet(factory, "UnitsOfMeasure", ExpectedUnits);
		AssertDefaultSet(factory, "Packagings", ExpectedPackagings);
	}

	[Fact]
	public void ExistingReferenceDataIsNeverOverwritten()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		using (var connection = factory.CreateConnection())
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				INSERT INTO UnitsOfMeasure (Name, Description, IsActive) VALUES ('EA', 'Existing custom each', 0);
				INSERT INTO UnitsOfMeasure (Name, Description, IsActive) VALUES ('CUSTOM-UOM', 'Custom unit', 0);
				INSERT INTO Packagings (Name, Description, IsActive) VALUES ('BOX', 'Existing custom box', 0);
				INSERT INTO Packagings (Name, Description, IsActive) VALUES ('CUSTOM-PACK', 'Custom packaging', 0);
				""";
			command.ExecuteNonQuery();
		}

		DatabaseProviderFactory.CreateInitializer(factory).Initialize();

		Assert.Equal(1, CountByName(factory, "UnitsOfMeasure", "EA"));
		AssertReference(factory, "UnitsOfMeasure", "EA", "Existing custom each", false);
		AssertReference(factory, "UnitsOfMeasure", "CUSTOM-UOM", "Custom unit", false);
		Assert.Equal(1, CountByName(factory, "Packagings", "BOX"));
		AssertReference(factory, "Packagings", "BOX", "Existing custom box", false);
		AssertReference(factory, "Packagings", "CUSTOM-PACK", "Custom packaging", false);
	}

	[Fact]
	public void ExistingCaseVariantPreventsEquivalentDefaultDuplicate()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		using (var connection = factory.CreateConnection())
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "INSERT INTO UnitsOfMeasure (Name, Description, IsActive) VALUES ('ea', 'Lower-case existing unit', 1);";
			command.ExecuteNonQuery();
		}

		DatabaseProviderFactory.CreateInitializer(factory).Initialize();

		Assert.Equal(1, CountByNameCaseInsensitive(factory, "UnitsOfMeasure", "EA"));
		AssertReference(factory, "UnitsOfMeasure", "ea", "Lower-case existing unit", true);
	}

	private static void AssertDefaultSet(IDatabaseConnectionFactory factory, string tableName, IReadOnlyList<string> expected)
	{
		foreach (var name in expected)
		{
			Assert.Equal(1, CountByNameCaseInsensitive(factory, tableName, name));
		}
	}

	private static void AssertReference(IDatabaseConnectionFactory factory, string tableName, string name, string description, bool isActive)
	{
		using var connection = factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT Description, IsActive FROM {tableName} WHERE Name = @Name;";
		AddParameter(command, "@Name", name);
		using var reader = command.ExecuteReader();
		Assert.True(reader.Read());
		Assert.Equal(description, reader.GetString(0));
		Assert.Equal(isActive, reader.GetBoolean(1));
	}

	private static int CountByName(IDatabaseConnectionFactory factory, string tableName, string name) =>
		Count(factory, tableName, "Name = @Name", name);

	private static int CountByNameCaseInsensitive(IDatabaseConnectionFactory factory, string tableName, string name) =>
		Count(factory, tableName, "UPPER(Name) = UPPER(@Name)", name);

	private static int Count(IDatabaseConnectionFactory factory, string tableName, string predicate, string name)
	{
		using var connection = factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {predicate};";
		AddParameter(command, "@Name", name);
		return Convert.ToInt32(command.ExecuteScalar());
	}

	private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}

	public void Dispose()
	{
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}

[Collection("Provider database")]
public sealed class ItemReferenceDataDefaultProviderTests
{
	[SqlServerProcurementFact]
	public void SqlServerInitializerSeedsReferenceDefaults() =>
		VerifyProvider(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MySqlProcurementFact]
	public void MySqlOrMariaDbInitializerSeedsReferenceDefaults() =>
		VerifyProvider(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static void VerifyProvider(IDatabaseConnectionFactory factory)
	{
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		foreach (var name in new[] { "EA", "SET", "PAIR", "M", "M2", "M3", "KG", "G", "L", "ML", "H", "DAY" })
		{
			Assert.Equal(1, CountByNameCaseInsensitive(factory, "UnitsOfMeasure", name));
		}
		foreach (var name in new[] { "UNIT", "BAG", "BOX", "CARTON", "CASE", "PACK", "BUNDLE", "TRAY", "REEL", "ROLL", "CRATE", "PALLET" })
		{
			Assert.Equal(1, CountByNameCaseInsensitive(factory, "Packagings", name));
		}
	}

	private static int CountByNameCaseInsensitive(IDatabaseConnectionFactory factory, string tableName, string name)
	{
		using var connection = factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE UPPER(Name) = UPPER(@Name);";
		var parameter = command.CreateParameter();
		parameter.ParameterName = "@Name";
		parameter.Value = name;
		command.Parameters.Add(parameter);
		return Convert.ToInt32(command.ExecuteScalar());
	}
}
