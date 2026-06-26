// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

public sealed class InventoryRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public InventoryRepository(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public Inventory GetOrCreate(
		long itemId,
		long purposeId)
	{
		var existingInventory =
			GetByItemAndPurpose(
				itemId,
				purposeId);

		if (existingInventory is not null)
		{
			return existingInventory;
		}

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
			IsActive
		)
		VALUES
		(
			$ItemId,
			$PurposeId,
			1
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$ItemId",
			itemId);

		command.Parameters.AddWithValue(
			"$PurposeId",
			purposeId);

		var id =
			(long)command.ExecuteScalar()!;

		return new Inventory
		{
			Id = id,
			ItemId = itemId,
			PurposeId = purposeId,
			IsActive = true
		};
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
			IsActive
		FROM Inventories
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		using var reader =
			command.ExecuteReader();

		if (!reader.Read())
		{
			return null;
		}

		return ReadInventory(
			reader);
	}

	public IReadOnlyList<Inventory> GetAll()
	{
		var result =
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
			IsActive
		FROM Inventories
		WHERE IsActive = 1
		ORDER BY Id;
		""";

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			result.Add(
				ReadInventory(
					reader));
		}

		return result;
	}

	private Inventory? GetByItemAndPurpose(
		long itemId,
		long purposeId)
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
			IsActive
		FROM Inventories
		WHERE
			ItemId = $ItemId
			AND PurposeId = $PurposeId;
		""";

		command.Parameters.AddWithValue(
			"$ItemId",
			itemId);

		command.Parameters.AddWithValue(
			"$PurposeId",
			purposeId);

		using var reader =
			command.ExecuteReader();

		if (!reader.Read())
		{
			return null;
		}

		return ReadInventory(
			reader);
	}

	private static Inventory ReadInventory(
		SqliteDataReader reader)
	{
		return new Inventory
		{
			Id = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PurposeId = reader.GetInt64(2),
			IsActive = reader.GetInt64(3) == 1
		};
	}
}