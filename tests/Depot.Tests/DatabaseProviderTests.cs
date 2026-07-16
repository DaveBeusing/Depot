// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Xunit;

namespace Depot.Tests;

public sealed class DatabaseProviderTests
{
	[Fact]
	public void SqlServerCommandsNormalizePortableRepositorySql()
	{
		var factory = new SqlServerConnectionFactory(
			new DatabaseConnectionSettings
			{
				Provider = DatabaseProvider.SqlServer,
				SqlServerHost = "localhost",
				SqlServerDatabase = "DepotTest",
				SqlServerUserName = "test",
				SqlServerPassword = "secret"
			});

		using var connection = factory.CreateConnection();
		using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT last_insert_rowid(); SELECT * FROM Users WHERE Email = $Email COLLATE NOCASE;";

		Assert.Contains("SCOPE_IDENTITY", command.CommandText, StringComparison.Ordinal);
		Assert.Contains("@Email", command.CommandText, StringComparison.Ordinal);
		Assert.DoesNotContain("NOCASE", command.CommandText, StringComparison.Ordinal);
	}

	[Fact]
	public void ProviderFactoryPreservesSqliteCompatibility()
	{
		var factory = DatabaseProviderFactory.CreateConnectionFactory(
			new DatabaseConnectionSettings
			{
				Provider = DatabaseProvider.Local,
				LocalDatabasePath = "depot-test.db"
			});

		Assert.IsType<SqliteConnectionFactory>(factory);
	}

	[Fact]
	public void MySqlCommandsNormalizePortableRepositorySql()
	{
		var factory = new MySqlConnectionFactory(
			new DatabaseConnectionSettings
			{
				Provider = DatabaseProvider.MySql,
				MySqlHost = "localhost",
				MySqlDatabase = "DepotTest",
				MySqlUserName = "test",
				MySqlPassword = "secret"
			});

		using var connection = factory.CreateConnection();
		using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT last_insert_rowid(); SELECT * FROM Users WHERE Email = $Email COLLATE NOCASE;";

		Assert.Contains("LAST_INSERT_ID", command.CommandText, StringComparison.Ordinal);
		Assert.Contains("@Email", command.CommandText, StringComparison.Ordinal);
		Assert.DoesNotContain("NOCASE", command.CommandText, StringComparison.Ordinal);
	}

	[Fact]
	public void ProviderFactoryCreatesMySqlProviderAndInitializer()
	{
		var factory = DatabaseProviderFactory.CreateConnectionFactory(
			new DatabaseConnectionSettings
			{
				Provider = DatabaseProvider.MySql,
				MySqlHost = "localhost",
				MySqlDatabase = "DepotTest",
				MySqlUserName = "test",
				MySqlPassword = "secret"
			});

		Assert.IsType<MySqlConnectionFactory>(factory);
		Assert.IsType<MySqlDatabase>(DatabaseProviderFactory.CreateInitializer(factory));
	}

	[Fact]
	public void ConnectionFailuresExposeAnExplicitSafeMessage()
	{
		var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		IDatabaseConnectionFactory factory =
			new SqliteConnectionFactory(Path.Combine(missingDirectory, "depot.db"));

		using var connection = factory.CreateConnection();
		var exception = Assert.Throws<DatabaseConnectionException>(() => connection.Open());

		Assert.Equal(
			"The local SQLite database file or its directory is unavailable.",
			exception.Message);
		Assert.DoesNotContain("Data Source", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void AllDatabaseRepositoriesUseTheSharedDataAccessLayer()
	{
		Type[] repositoryTypes =
		[
			typeof(AuditRepository),
			typeof(InventoryRepository),
			typeof(ItemRepository),
			typeof(LocationRepository),
			typeof(PurposeRepository),
			typeof(StockMovementRepository),
			typeof(UserRepository)
		];

		Assert.All(
			repositoryTypes,
			type =>
			{
				var constructor = Assert.Single(type.GetConstructors());
				var parameter = Assert.Single(constructor.GetParameters());
				Assert.Equal(typeof(DatabaseAccess), parameter.ParameterType);
			});
	}
}
