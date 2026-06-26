// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

public sealed class PurposeRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public PurposeRepository(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public Purpose GetOrCreate(
		string name,
		string? description)
	{
		var existingPurpose =
			GetByName(
				name);

		if (existingPurpose is not null)
		{
			return existingPurpose;
		}

		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT INTO Purposes
		(
			Name,
			Description,
			IsActive
		)
		VALUES
		(
			$Name,
			$Description,
			1
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$Name",
			name);

		command.Parameters.AddWithValue(
			"$Description",
			(object?)description ?? DBNull.Value);

		var id =
			(long)command.ExecuteScalar()!;

		return new Purpose
		{
			Id = id,
			Name = name,
			Description = description,
			IsActive = true
		};
	}

	public Purpose? GetByName(
		string name)
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
			Name,
			Description,
			IsActive
		FROM Purposes
		WHERE Name = $Name;
		""";

		command.Parameters.AddWithValue(
			"$Name",
			name);

		using var reader =
			command.ExecuteReader();

		if (!reader.Read())
		{
			return null;
		}

		return ReadPurpose(
			reader);
	}

	private static Purpose ReadPurpose(
		SqliteDataReader reader)
	{
		return new Purpose
		{
			Id = reader.GetInt64(0),
			Name = reader.GetString(1),
			Description = reader.IsDBNull(2) ? null : reader.GetString(2),
			IsActive = reader.GetInt64(3) == 1
		};
	}
}