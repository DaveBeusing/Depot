// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;
using Depot.Data;
using Depot.Models;
using Xunit;

namespace Depot.Tests;

public sealed class ProviderParameterNormalizationTests
{
	[Theory]
	[InlineData("SalesLeads", "CreatedAtUtc")]
	[InlineData("SalesLeads", "UpdatedAtUtc")]
	[InlineData("SalesLeads", "ConvertedAtUtc")]
	[InlineData("SalesOpportunities", "CreatedAtUtc")]
	[InlineData("SalesOpportunities", "UpdatedAtUtc")]
	[InlineData("SalesOpportunities", "ClosedAtUtc")]
	[InlineData("SalesOpportunities", "NextActivityDate")]
	[InlineData("SalesOpportunities", "NowUtc")]
	[InlineData("SalesActivities", "DueAtUtc")]
	[InlineData("SalesActivities", "CompletedAtUtc")]
	[InlineData("SalesActivities", "CancelledAtUtc")]
	[InlineData("FinanceJournals", "Created")]
	public void MySqlBindsCrmAndFinanceTimestampsAsUtcDateTime(string table, string name)
	{
		using var connection = new MySqlConnectionFactory(new DatabaseConnectionSettings()).CreateConnection();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT * FROM {table} WHERE CreatedAtUtc <= ${name};";
		var parameter = command.CreateParameter();
		parameter.ParameterName = $"${name}";
		parameter.Value = "2026-10-10T22:42:26.7987351+02:00";
		command.Parameters.Add(parameter);

		Normalize(command);
		Normalize(command); // Retried commands must retain the same instant.

		var value = Assert.IsType<DateTime>(parameter.Value);
		Assert.Equal(new DateTime(2026, 10, 10, 20, 42, 26).AddTicks(7987351), value);
		Assert.Equal(DateTimeKind.Unspecified, value.Kind);
	}

	[Theory]
	[InlineData("SalesLeads", "NotesSummary", "2026-10-10T20:42:26.7987351Z")]
	[InlineData("SalesOpportunities", "ExpectedCloseDate", "2026-10-10")]
	[InlineData("SalesActivities", "DueAtUtc", null)]
	[InlineData("SalesActivities", "DueAtUtc", "invalid")]
	[InlineData("SalesOrders", "SubmittedAtUtc", "2026-10-10T20:42:26.7987351Z")]
	public void MySqlPreservesTextDatesNullsAndOtherDomains(string table, string name, string? text)
	{
		using var connection = new MySqlConnectionFactory(new DatabaseConnectionSettings()).CreateConnection();
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT * FROM {table} WHERE Id = $Id;";
		var parameter = command.CreateParameter();
		parameter.ParameterName = $"${name}";
		parameter.Value = (object?)text ?? DBNull.Value;
		command.Parameters.Add(parameter);
		Normalize(command);
		Assert.Equal((object?)text ?? DBNull.Value, parameter.Value);
	}

	private static void Normalize(System.Data.Common.DbCommand command)
	{
		var normalize = command.GetType().GetMethod("NormalizeParameters", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(normalize);
		normalize.Invoke(command, null);
	}

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

		var normalize = command.GetType().GetMethod("NormalizeParameters", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(normalize);
		normalize.Invoke(command, null);

		Assert.Equal("SELECT @Name;", command.CommandText);
		Assert.Equal("@Name", command.Parameters[0].ParameterName);
	}
}
