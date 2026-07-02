// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

public sealed class StockMovementRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public StockMovementRepository(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public long Create(
		StockMovement movement)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

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

		return (long)command.ExecuteScalar()!;
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
		SqliteDataReader reader)
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
