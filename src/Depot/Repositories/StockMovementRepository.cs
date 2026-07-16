// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;
using System.Data.Common;
using System.Text.Json;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Depot.Repositories;

public sealed class StockMovementRepository
{
	private readonly IDatabaseConnectionFactory _connectionFactory;

	public StockMovementRepository(
		IDatabaseConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public long CreateAtomic(
		StockMovement movement,
		AuditEntry auditEntry)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return CreateAtomicCore(movement, auditEntry);
			}
			catch (SqlException exception) when (attempt < 3 && exception.Number is 1205 or 3960)
			{
				Thread.Sleep(50 * attempt);
			}
			catch (SqliteException exception) when (attempt < 3 && exception.SqliteErrorCode is 5 or 6)
			{
				Thread.Sleep(50 * attempt);
			}
			catch (MySqlException exception) when (attempt < 3 && exception.Number is 1205 or 1213)
			{
				Thread.Sleep(50 * attempt);
			}
		}
	}

	private long CreateAtomicCore(
		StockMovement movement,
		AuditEntry auditEntry)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();
		using var transaction = _connectionFactory.BeginWriteTransaction(connection);

		using (var lockCommand = connection.CreateCommand())
		{
			lockCommand.Transaction = transaction;
			lockCommand.CommandText = _connectionFactory.GetInventoryLockSql();
			lockCommand.Parameters.AddWithValue("$InventoryId", movement.InventoryId);
			if (lockCommand.ExecuteScalar() is null)
			{
				throw new InvalidOperationException($"Inventory with id '{movement.InventoryId}' was not found.");
			}
		}

		using (var stockCommand = connection.CreateCommand())
		{
			stockCommand.Transaction = transaction;
			stockCommand.CommandText =
				"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;";
			stockCommand.Parameters.AddWithValue("$InventoryId", movement.InventoryId);
			var currentStock = Convert.ToInt64(stockCommand.ExecuteScalar());
			if (currentStock + movement.Quantity < 0)
			{
				throw new InsufficientStockException();
			}
		}

		using var command =
			connection.CreateCommand();
		command.Transaction = transaction;

		command.CommandText =
		"""
		INSERT INTO StockMovements
		(
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		)
		VALUES
		(
			$InventoryId,
			$MovementType,
			$TimestampUtc,
			$Quantity,
			$UnitPrice,
			$Reference,
			$Notes
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$InventoryId",
			movement.InventoryId);

		command.Parameters.AddWithValue(
			"$MovementType",
			(int)movement.MovementType);

		command.Parameters.AddWithValue(
			"$TimestampUtc",
			movement.TimestampUtc.ToString("O"));

		command.Parameters.AddWithValue(
			"$Quantity",
			movement.Quantity);

		command.Parameters.AddWithValue(
			"$UnitPrice",
			(object?)movement.UnitPrice ?? DBNull.Value);

		command.Parameters.AddWithValue(
			"$Reference",
			(object?)movement.Reference ?? DBNull.Value);

		command.Parameters.AddWithValue(
			"$Notes",
			(object?)movement.Notes ?? DBNull.Value);

		var movementId = Convert.ToInt64(command.ExecuteScalar());
		movement.Id = movementId;
		auditEntry.EntityId = movementId;
		auditEntry.AfterJson = JsonSerializer.Serialize(
			movement,
			new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

		using var auditCommand = connection.CreateCommand();
		auditCommand.Transaction = transaction;
		auditCommand.CommandText =
		"""
		INSERT INTO AuditEntries
		(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
		VALUES
		($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
		""";
		auditCommand.Parameters.AddWithValue("$TimestampUtc", auditEntry.TimestampUtc.ToString("O"));
		auditCommand.Parameters.AddWithValue("$UserId", (object?)auditEntry.UserId ?? DBNull.Value);
		auditCommand.Parameters.AddWithValue("$UserEmail", auditEntry.UserEmail);
		auditCommand.Parameters.AddWithValue("$EntityType", auditEntry.EntityType);
		auditCommand.Parameters.AddWithValue("$EntityId", auditEntry.EntityId);
		auditCommand.Parameters.AddWithValue("$Action", auditEntry.Action);
		auditCommand.Parameters.AddWithValue("$BeforeJson", (object?)auditEntry.BeforeJson ?? DBNull.Value);
		auditCommand.Parameters.AddWithValue("$AfterJson", (object?)auditEntry.AfterJson ?? DBNull.Value);
		auditCommand.ExecuteNonQuery();

		transaction.Commit();
		return movementId;
	}

	public IReadOnlyList<StockMovement> GetAll()
	{
		return Search(
			null);
	}

	public IReadOnlyList<StockMovement> Search(
		string? searchText)
	{
		var result =
			new List<StockMovement>();

		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		if (string.IsNullOrWhiteSpace(searchText))
		{
			command.CommandText =
			"""
			SELECT
				sm.Id,
				sm.InventoryId,
				sm.MovementType,
				sm.TimestampUtc,
				sm.Quantity,
				sm.UnitPrice,
				sm.Reference,
				sm.Notes
			FROM StockMovements sm
			INNER JOIN Inventories inv
				ON inv.Id = sm.InventoryId
			INNER JOIN Items i
				ON i.Id = inv.ItemId
			ORDER BY sm.TimestampUtc DESC;
			""";
		}
		else
		{
			command.CommandText =
			"""
			SELECT
				sm.Id,
				sm.InventoryId,
				sm.MovementType,
				sm.TimestampUtc,
				sm.Quantity,
				sm.UnitPrice,
				sm.Reference,
				sm.Notes
			FROM StockMovements sm
			INNER JOIN Inventories inv
				ON inv.Id = sm.InventoryId
			INNER JOIN Items i
				ON i.Id = inv.ItemId
			LEFT JOIN Purposes p
				ON p.Id = inv.PurposeId
			LEFT JOIN Locations l
				ON l.Id = inv.LocationId
			WHERE
				i.PartNumber LIKE $Search
				OR i.Description LIKE $Search
				OR p.Name LIKE $Search
				OR l.Name LIKE $Search
				OR sm.Reference LIKE $Search
				OR sm.Notes LIKE $Search
			ORDER BY sm.TimestampUtc DESC;
			""";

			command.Parameters.AddWithValue(
				"$Search",
				$"%{searchText.Trim()}%");
		}

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			result.Add(
				ReadMovement(reader));
		}

		return result;
	}

	public IReadOnlyList<StockMovement> GetByInventoryId(
		long inventoryId)
	{
		var result =
			new List<StockMovement>();

		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		SELECT
			Id,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		FROM StockMovements
		WHERE InventoryId = $InventoryId
		ORDER BY TimestampUtc;
		""";

		command.Parameters.AddWithValue(
			"$InventoryId",
			inventoryId);

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			result.Add(
				ReadMovement(reader));
		}

		return result;
	}

	private static StockMovement ReadMovement(
		DbDataReader reader)
	{
		return new StockMovement
		{
			Id =
				reader.GetInt64(0),

			InventoryId =
				reader.GetInt64(1),

			MovementType =
				(StockMovementType)reader.GetInt32(2),

			TimestampUtc =
				DateTime.Parse(
					reader.GetString(3),
					null,
					System.Globalization.DateTimeStyles.RoundtripKind),

			Quantity =
				reader.GetInt32(4),

			UnitPrice =
				reader.IsDBNull(5)
					? null
					: reader.GetDecimal(5),

			Reference =
				reader.IsDBNull(6)
					? null
					: reader.GetString(6),

			Notes =
				reader.IsDBNull(7)
					? null
					: reader.GetString(7)
		};
	}
	

}
