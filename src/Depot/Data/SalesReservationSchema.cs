// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Models;

namespace Depot.Data;

internal static class SalesReservationSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		if (connectionFactory.Provider != DatabaseProvider.Local) return;

		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
			"""
			PRAGMA foreign_keys = OFF;
			CREATE TABLE IF NOT EXISTS InventoryReservations_v4
			(
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				SalesOrderLineId INTEGER NOT NULL REFERENCES SalesOrderLines(Id),
				InventoryId INTEGER NOT NULL REFERENCES Inventories(Id),
				Quantity INTEGER NOT NULL,
				Status INTEGER NOT NULL DEFAULT 1,
				CreatedAtUtc TEXT NOT NULL,
				CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id),
				ReleasedAtUtc TEXT NULL,
				ReleasedByUserId INTEGER NULL REFERENCES Users(Id),
				Version INTEGER NOT NULL DEFAULT 1
			);
			INSERT INTO InventoryReservations_v4
			(Id,SalesOrderLineId,InventoryId,Quantity,Status,CreatedAtUtc,CreatedByUserId,ReleasedAtUtc,ReleasedByUserId,Version)
			SELECT Id,SalesOrderLineId,InventoryId,Quantity,Status,CreatedAtUtc,CreatedByUserId,ReleasedAtUtc,ReleasedByUserId,Version
			FROM InventoryReservations;
			DROP TABLE InventoryReservations;
			ALTER TABLE InventoryReservations_v4 RENAME TO InventoryReservations;
			CREATE INDEX IF NOT EXISTS IX_InventoryReservations_Inventory_Status ON InventoryReservations(InventoryId, Status);
			CREATE UNIQUE INDEX IF NOT EXISTS UX_InventoryReservations_Active
			ON InventoryReservations(SalesOrderLineId, InventoryId)
			WHERE Status = 1;
			PRAGMA foreign_keys = ON;
			""";
		command.ExecuteNonQuery();
	}

	public static void EnsureActiveUniqueness(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		switch (connectionFactory.Provider)
		{
			case DatabaseProvider.Local:
				Execute(connection,
					"CREATE UNIQUE INDEX IF NOT EXISTS UX_InventoryReservations_Active ON InventoryReservations(SalesOrderLineId, InventoryId) WHERE Status = 1;");
				break;
			case DatabaseProvider.SqlServer:
				Execute(connection,
					"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'InventoryReservations') AND name=N'UX_InventoryReservations_Active') CREATE UNIQUE INDEX UX_InventoryReservations_Active ON InventoryReservations(SalesOrderLineId, InventoryId) WHERE Status = 1;");
				break;
			case DatabaseProvider.MySql:
				if (!ColumnExists(connection, "InventoryReservations", "ActiveInventoryId"))
				{
					Execute(connection,
						"ALTER TABLE InventoryReservations ADD COLUMN ActiveInventoryId BIGINT GENERATED ALWAYS AS (CASE WHEN Status = 1 THEN InventoryId ELSE NULL END) STORED;");
				}
				if (!IndexExists(connection, "InventoryReservations", "UX_InventoryReservations_Active"))
				{
					Execute(connection,
						"CREATE UNIQUE INDEX UX_InventoryReservations_Active ON InventoryReservations(SalesOrderLineId, ActiveInventoryId);");
				}
				break;
			default:
				throw new NotSupportedException($"Reservation uniqueness is not supported for provider '{connectionFactory.Provider}'.");
		}
	}

	private static bool ColumnExists(DbConnection connection, string table, string column)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@Table AND column_name=@Column;";
		Add(command, "@Table", table);
		Add(command, "@Column", column);
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}

	private static bool IndexExists(DbConnection connection, string table, string index)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name=@Table AND index_name=@Index;";
		Add(command, "@Table", table);
		Add(command, "@Index", index);
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}

	private static void Add(DbCommand command, string name, object value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.Value = value;
		command.Parameters.Add(parameter);
	}

	private static void Execute(DbConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
