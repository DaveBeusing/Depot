// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

namespace Depot.Data;

internal static class MySqlSchemaCompatibility
{
	public static void EnsureColumn(
		DbConnection connection,
		string tableName,
		string columnName,
		string definition)
	{
		ValidateIdentifier(tableName, nameof(tableName));
		ValidateIdentifier(columnName, nameof(columnName));
		if (string.IsNullOrWhiteSpace(definition)) throw new ArgumentException("A column definition is required.", nameof(definition));

		using var exists = connection.CreateCommand();
		exists.CommandText =
			"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=$TableName AND column_name=$ColumnName;";
		AddParameter(exists, "$TableName", tableName);
		AddParameter(exists, "$ColumnName", columnName);
		if (Convert.ToInt32(exists.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0) return;

		using var alter = connection.CreateCommand();
		alter.CommandText = $"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {definition};";
		alter.ExecuteNonQuery();
	}

	private static void AddParameter(DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}

	private static void ValidateIdentifier(string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
			throw new ArgumentException("Only simple SQL identifiers are permitted.", parameterName);
	}
}