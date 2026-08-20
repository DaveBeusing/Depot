// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

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
}
