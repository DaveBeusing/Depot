// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

namespace Depot.Data;

public static class FinanceAccountsPayableSchemaMigration
{
	public const int CurrentVersion = 4;
	private const string FeatureName = "Finance";

	public static void Migrate(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		FinanceAccountsReceivableSchemaMigration.Migrate(connectionFactory);
		var version = ReadVersion(connectionFactory);
		if (version > CurrentVersion) return;
		if (version == 3)
		{
			FinanceAccountsPayableSchemaInitializer.Ensure(connectionFactory);
			WriteVersion(connectionFactory, CurrentVersion);
			version = CurrentVersion;
		}
		if (version != CurrentVersion) throw new InvalidOperationException($"Finance schema migration stopped at unsupported version '{version}'.");
	}

	private static int ReadVersion(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name=$Name;";
		Add(command, "$Name", FeatureName);
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private static void WriteVersion(IDatabaseConnectionFactory connectionFactory, int version)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connection.BeginTransaction();
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "UPDATE DepotFeatureVersions SET Version=$Version WHERE Name=$Name;";
		Add(command, "$Version", version);
		Add(command, "$Name", FeatureName);
		if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Finance feature version could not be updated.");
		transaction.Commit();
	}

	private static void Add(DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}
}
