// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;
using Depot.Data;
using Depot.Models;
using Xunit;

namespace Depot.Tests;

public sealed class ProviderParameterNormalizationTests
{
	[Fact]
	public void MySql_NormalizesSqlAndParameterNamesTogether() =>
		AssertParameterNormalization(new MySqlConnectionFactory(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.MySql,
			MySqlHost = "localhost",
			MySqlDatabase = "DepotTests",
			MySqlUserName = "depot",
			MySqlPassword = "test",
			UseMySqlTls = false
		}));

	[Fact]
	public void SqlServer_NormalizesSqlAndParameterNamesTogether() =>
		AssertParameterNormalization(new SqlServerConnectionFactory(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.SqlServer,
			SqlServerHost = "localhost",
			SqlServerDatabase = "DepotTests",
			SqlServerUserName = "depot",
			SqlServerPassword = "test",
			EncryptSqlServerConnection = false
		}));

	private static void AssertParameterNormalization(IDatabaseConnectionFactory factory)
	{
		using var connection = factory.CreateConnection();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT $Name;";
		var parameter = command.CreateParameter();
		parameter.ParameterName = "$Name";
		parameter.Value = "Finance";
		command.Parameters.Add(parameter);

		var normalize = command.GetType().GetMethod("NormalizeParameterNames", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(normalize);
		normalize.Invoke(command, null);

		Assert.Equal("SELECT @Name;", command.CommandText);
		Assert.Equal("@Name", command.Parameters[0].ParameterName);
	}
}
