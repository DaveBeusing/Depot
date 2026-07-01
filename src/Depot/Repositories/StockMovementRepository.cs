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
			ItemId,
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
			$ItemId,
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
			"$ItemId",
			movement.ItemId);

		command.Parameters.AddWithValue(
			"$InventoryId",
			(object?)movement.InventoryId ?? DBNull.Value);

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
				sm.ItemId,
				sm.InventoryId,
				sm.MovementType,
				sm.TimestampUtc,
				sm.Quantity,
				sm.UnitPrice,
				sm.Reference,
				sm.Notes
			FROM StockMovements sm
			INNER JOIN Items i
				ON i.Id = sm.ItemId
			ORDER BY sm.TimestampUtc DESC;
			""";
		}
		else
		{
			command.CommandText =
			"""
			SELECT
				sm.Id,
				sm.ItemId,
				sm.InventoryId,
				sm.MovementType,
				sm.TimestampUtc,
				sm.Quantity,
				sm.UnitPrice,
				sm.Reference,
				sm.Notes
			FROM StockMovements sm
			INNER JOIN Items i
				ON i.Id = sm.ItemId
			WHERE
				i.PartNumber LIKE $Search
				OR i.Description LIKE $Search
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

	public IReadOnlyList<StockMovement> GetByItemId(
		long itemId)
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
			ItemId,
			InventoryId,
			MovementType,
			TimestampUtc,
			Quantity,
			UnitPrice,
			Reference,
			Notes
		FROM StockMovements
		WHERE ItemId = $ItemId
		ORDER BY TimestampUtc;
		""";

		command.Parameters.AddWithValue(
			"$ItemId",
			itemId);

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

			ItemId =
				reader.GetInt64(1),

			InventoryId =
				reader.IsDBNull(2)
					? null
					: reader.GetInt64(2),

			MovementType =
				(StockMovementType)reader.GetInt32(3),

			TimestampUtc =
				DateTime.Parse(
					reader.GetString(4),
					null,
					System.Globalization.DateTimeStyles.RoundtripKind),

			Quantity =
				reader.GetInt32(5),

			UnitPrice =
				reader.IsDBNull(6)
					? null
					: reader.GetDecimal(6),

			Reference =
				reader.IsDBNull(7)
					? null
					: reader.GetString(7),

			Notes =
				reader.IsDBNull(8)
					? null
					: reader.GetString(8)
		};
	}
	

}