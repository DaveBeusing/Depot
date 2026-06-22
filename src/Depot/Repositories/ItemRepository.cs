// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ItemRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public ItemRepository(SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public long Create(Item item)
	{
		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		command.CommandText =
		"""
		INSERT INTO Items
		(
			PartNumber,
			Description,
			Manufacturer,
			Category,
			IsActive
		)
		VALUES
		(
			$PartNumber,
			$Description,
			$Manufacturer,
			$Category,
			$IsActive
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$PartNumber",
			item.PartNumber);

		command.Parameters.AddWithValue(
			"$Description",
			item.Description);

		command.Parameters.AddWithValue(
			"$Manufacturer",
			(object?)item.Manufacturer ?? DBNull.Value);

		command.Parameters.AddWithValue(
			"$Category",
			(object?)item.Category ?? DBNull.Value);

		command.Parameters.AddWithValue(
			"$IsActive",
			item.IsActive ? 1 : 0);

		return (long)command.ExecuteScalar()!;
	}

	public IReadOnlyList<Item> GetAll()
	{
		var result = new List<Item>();

		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		command.CommandText =
		"""
		SELECT
			Id,
			PartNumber,
			Description,
			Manufacturer,
			Category,
			IsActive
		FROM Items
		ORDER BY PartNumber;
		""";

		using var reader = command.ExecuteReader();

		while (reader.Read())
		{
			result.Add(
				new Item
				{
					Id = reader.GetInt64(0),
					PartNumber = reader.GetString(1),
					Description = reader.GetString(2),
					Manufacturer = reader.IsDBNull(3)
						? null
						: reader.GetString(3),
					Category = reader.IsDBNull(4)
						? null
						: reader.GetString(4),
					IsActive = reader.GetInt64(5) == 1
				});
		}

		return result;
	}

	public Item? GetByPartNumber(string partNumber)
	{
		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		command.CommandText =
		"""
		SELECT
			Id,
			PartNumber,
			Description,
			Manufacturer,
			Category,
			IsActive
		FROM Items
		WHERE PartNumber = $PartNumber;
		""";

		command.Parameters.AddWithValue(
			"$PartNumber",
			partNumber);

		using var reader = command.ExecuteReader();

		if (!reader.Read())
		{
			return null;
		}

		return new Item
		{
			Id = reader.GetInt64(0),
			PartNumber = reader.GetString(1),
			Description = reader.GetString(2),
			Manufacturer = reader.IsDBNull(3)
				? null
				: reader.GetString(3),
			Category = reader.IsDBNull(4)
				? null
				: reader.GetString(4),
			IsActive = reader.GetInt64(5) == 1
		};
	}


}