// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

/// <summary>
/// Provides persistence for inventories.
/// </summary>
public sealed class InventoryRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public InventoryRepository(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public IReadOnlyList<Inventory> GetAll()
	{
		var inventories =
			new List<Inventory>();

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
			PurposeId,
			LocationId,
			IsActive
		FROM Inventories
		WHERE IsActive = 1
		ORDER BY ItemId;
		""";

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			inventories.Add(
				ReadInventory(reader));
		}

		return inventories;
	}

	public IReadOnlyList<Inventory> GetByItem(
		long itemId)
	{
		var inventories =
			new List<Inventory>();

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
			PurposeId,
			LocationId,
			IsActive
		FROM Inventories
		WHERE
			ItemId = $ItemId
			AND IsActive = 1
		ORDER BY PurposeId,
				 LocationId;
		""";

		command.Parameters.AddWithValue(
			"$ItemId",
			itemId);

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			inventories.Add(
				ReadInventory(reader));
		}

		return inventories;
	}

	public Inventory? GetById(
		long id)
	{
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
			PurposeId,
			LocationId,
			IsActive
		FROM Inventories
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		using var reader =
			command.ExecuteReader();

		return reader.Read()
			? ReadInventory(reader)
			: null;
	}

	public Inventory? GetByItemPurposeLocation(
		long itemId,
		long purposeId,
		long locationId)
	{
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
			PurposeId,
			LocationId,
			IsActive
		FROM Inventories
		WHERE
			ItemId = $ItemId
			AND PurposeId = $PurposeId
			AND LocationId = $LocationId;
		""";

		command.Parameters.AddWithValue(
			"$ItemId",
			itemId);

		command.Parameters.AddWithValue(
			"$PurposeId",
			purposeId);

		command.Parameters.AddWithValue(
			"$LocationId",
			locationId);

		using var reader =
			command.ExecuteReader();

		return reader.Read()
			? ReadInventory(reader)
			: null;
	}

	public long Create(
		Inventory inventory)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT INTO Inventories
		(
			ItemId,
			PurposeId,
			LocationId,
			IsActive
		)
		VALUES
		(
			$ItemId,
			$PurposeId,
			$LocationId,
			$IsActive
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$ItemId",
			inventory.ItemId);

		command.Parameters.AddWithValue(
			"$PurposeId",
			inventory.PurposeId);

		command.Parameters.AddWithValue(
			"$LocationId",
			inventory.LocationId);

		command.Parameters.AddWithValue(
			"$IsActive",
			inventory.IsActive);

		return (long)command.ExecuteScalar()!;
	}

	public void Deactivate(
		long id)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		UPDATE Inventories
		SET IsActive = 0
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		command.ExecuteNonQuery();
	}

	private static Inventory ReadInventory(
		SqliteDataReader reader)
	{
		return new Inventory
		{
			Id = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PurposeId = reader.GetInt64(2),
			LocationId = reader.GetInt64(3),
			IsActive = reader.GetInt64(4) == 1
		};
	}
}