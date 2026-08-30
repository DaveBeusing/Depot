// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

namespace Depot.Data;

internal static class ItemReferenceDataDefaults
{
	private static readonly ReferenceDefault[] UnitsOfMeasure =
	[
		new("EA", "Each"),
		new("SET", "Set"),
		new("PAIR", "Pair"),
		new("M", "Meter"),
		new("M2", "Square Meter"),
		new("M3", "Cubic Meter"),
		new("KG", "Kilogram"),
		new("G", "Gram"),
		new("L", "Liter"),
		new("ML", "Milliliter"),
		new("H", "Hour"),
		new("DAY", "Day")
	];

	private static readonly ReferenceDefault[] Packagings =
	[
		new("UNIT", "Unit"),
		new("PACK", "Pack"),
		new("BAG", "Bag"),
		new("BOX", "Box"),
		new("CARTON", "Carton"),
		new("CASE", "Case"),
		new("BUNDLE", "Bundle"),
		new("TRAY", "Tray"),
		new("REEL", "Reel"),
		new("ROLL", "Roll"),
		new("CRATE", "Crate"),
		new("PALLET", "Pallet")
	];

	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;

		EnsureValues(command, "UnitsOfMeasure", UnitsOfMeasure);
		EnsureValues(command, "Packagings", Packagings);

		transaction.Commit();
	}

	private static void EnsureValues(DbCommand command, string tableName, IReadOnlyList<ReferenceDefault> defaults)
	{
		foreach (var value in defaults)
		{
			command.Parameters.Clear();
			command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE UPPER(Name) = UPPER(@Name);";
			AddParameter(command, "@Name", value.Name);
			var existingCount = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
			if (existingCount > 0) continue;

			command.Parameters.Clear();
			command.CommandText = $"INSERT INTO {tableName} (Name, Description, IsActive) VALUES (@Name, @Description, @IsActive);";
			AddParameter(command, "@Name", value.Name);
			AddParameter(command, "@Description", value.Description);
			AddParameter(command, "@IsActive", true);
			command.ExecuteNonQuery();
		}
	}

	private static void AddParameter(DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}

	private sealed record ReferenceDefault(string Name, string Description);
}
