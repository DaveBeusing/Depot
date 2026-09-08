// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal static class SalesOrderAddressSnapshotSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		if (connectionFactory.Provider == DatabaseProvider.MySql)
		{
			MySqlSchemaCompatibility.EnsureColumn(connection, "SalesOrders", "BillingAddress", "TEXT NULL");
			MySqlSchemaCompatibility.EnsureColumn(connection, "SalesOrders", "ShippingAddress", "TEXT NULL");
			return;
		}

		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "ALTER TABLE SalesOrders ADD COLUMN BillingAddress TEXT NULL; ALTER TABLE SalesOrders ADD COLUMN ShippingAddress TEXT NULL;",
			DatabaseProvider.SqlServer => "IF COL_LENGTH('SalesOrders','BillingAddress') IS NULL ALTER TABLE SalesOrders ADD BillingAddress nvarchar(2000) NULL; IF COL_LENGTH('SalesOrders','ShippingAddress') IS NULL ALTER TABLE SalesOrders ADD ShippingAddress nvarchar(2000) NULL;",
			_ => throw new NotSupportedException($"Sales order address snapshots are not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach (var statement in command.CommandText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
	}
}