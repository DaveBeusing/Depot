// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

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
}
