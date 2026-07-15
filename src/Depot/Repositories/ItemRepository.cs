// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

public sealed class ItemRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public ItemRepository(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public long Create(
		Item item)
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

		command.Parameters.AddWithValue("$PartNumber", item.PartNumber);
		command.Parameters.AddWithValue("$Description", item.Description);
		command.Parameters.AddWithValue("$Manufacturer", (object?)item.Manufacturer ?? DBNull.Value);
		command.Parameters.AddWithValue("$Category", (object?)item.Category ?? DBNull.Value);
		command.Parameters.AddWithValue("$IsActive", item.IsActive ? 1 : 0);

		return (long)command.ExecuteScalar()!;
	}

	public bool Update(
		Item item)
	{
		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		command.CommandText =
		"""
		UPDATE Items
		SET
			Description = $Description,
			Manufacturer = $Manufacturer,
			Category = $Category,
			IsActive = $IsActive,
			Version = Version + 1
		WHERE Id = $Id AND Version = $Version;
		""";

		command.Parameters.AddWithValue("$Id", item.Id);
		command.Parameters.AddWithValue("$Description", item.Description);
		command.Parameters.AddWithValue("$Manufacturer", (object?)item.Manufacturer ?? DBNull.Value);
		command.Parameters.AddWithValue("$Category", (object?)item.Category ?? DBNull.Value);
		command.Parameters.AddWithValue("$IsActive", item.IsActive ? 1 : 0);
		command.Parameters.AddWithValue("$Version", item.Version);

		return command.ExecuteNonQuery() == 1;
	}

	public bool Deactivate(
		long id,
		long version)
	{
		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		command.CommandText =
		"""
		UPDATE Items
		SET IsActive = 0, Version = Version + 1
		WHERE Id = $Id AND Version = $Version;
		""";

		command.Parameters.AddWithValue("$Id", id);
		command.Parameters.AddWithValue("$Version", version);

		return command.ExecuteNonQuery() == 1;
	}

	public IReadOnlyList<Item> GetAll()
	{
		return SearchActive(
			null);
	}

	public IReadOnlyList<Item> SearchActive(
		string? searchText)
	{
		var result = new List<Item>();

		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		if (string.IsNullOrWhiteSpace(searchText))
		{
			command.CommandText =
			"""
			SELECT
				Id,
				PartNumber,
				Description,
				Manufacturer,
				Category,
				IsActive,
				Version
			FROM Items
			WHERE IsActive = 1
			ORDER BY PartNumber;
			""";
		}
		else
		{
			command.CommandText =
			"""
			SELECT
				Id,
				PartNumber,
				Description,
				Manufacturer,
				Category,
				IsActive,
				Version
			FROM Items
			WHERE
				IsActive = 1
				AND
				(
					PartNumber LIKE $Search
					OR Description LIKE $Search
					OR Manufacturer LIKE $Search
					OR Category LIKE $Search
				)
			ORDER BY PartNumber;
			""";

			command.Parameters.AddWithValue(
				"$Search",
				$"%{searchText.Trim()}%");
		}

		using var reader = command.ExecuteReader();

		while (reader.Read())
		{
			result.Add(
				ReadItem(reader));
		}

		return result;
	}

	public Item? GetById(
		long id)
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
			IsActive,
			Version
		FROM Items
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue("$Id", id);

		using var reader = command.ExecuteReader();

		if (!reader.Read())
		{
			return null;
		}

		return ReadItem(reader);
	}

	public Item? GetByPartNumber(
		string partNumber)
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
			IsActive,
			Version
		FROM Items
		WHERE PartNumber = $PartNumber;
		""";

		command.Parameters.AddWithValue("$PartNumber", partNumber);

		using var reader = command.ExecuteReader();

		if (!reader.Read())
		{
			return null;
		}

		return ReadItem(reader);
	}

	private static Item ReadItem(
		SqliteDataReader reader)
	{
		return new Item
		{
			Id = reader.GetInt64(0),
			PartNumber = reader.GetString(1),
			Description = reader.GetString(2),
			Manufacturer = reader.IsDBNull(3) ? null : reader.GetString(3),
			Category = reader.IsDBNull(4) ? null : reader.GetString(4),
			IsActive = reader.GetInt64(5) == 1,
			Version = reader.GetInt64(6)
		};
	}
}
