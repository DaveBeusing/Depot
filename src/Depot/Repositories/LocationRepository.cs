// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

/// <summary>
/// Provides persistence for locations.
/// </summary>
public sealed class LocationRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public LocationRepository(
		SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public IReadOnlyList<Location> GetAll()
	{
		var locations =
			new List<Location>();

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
		FROM Locations
		WHERE IsActive = 1
		ORDER BY Name;
		""";

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			locations.Add(
				ReadLocation(
					reader));
		}

		return locations;
	}

	public Location? GetById(
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
			Name,
			Description,
			IsActive
		FROM Locations
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		using var reader =
			command.ExecuteReader();

		return reader.Read()
			? ReadLocation(reader)
			: null;
	}

	public Location? GetByName(
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
		FROM Locations
		WHERE Name = $Name;
		""";

		command.Parameters.AddWithValue(
			"$Name",
			name);

		using var reader =
			command.ExecuteReader();

		return reader.Read()
			? ReadLocation(reader)
			: null;
	}

	public long Create(
		Location location)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		INSERT INTO Locations
		(
			Name,
			Description,
			IsActive
		)
		VALUES
		(
			$Name,
			$Description,
			$IsActive
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$Name",
			location.Name);

		command.Parameters.AddWithValue(
			"$Description",
			(object?)location.Description ?? DBNull.Value);

		command.Parameters.AddWithValue(
			"$IsActive",
			location.IsActive);

		return (long)command.ExecuteScalar()!;
	}

	public void Update(
		Location location)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		UPDATE Locations
		SET
			Name = $Name,
			Description = $Description
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			location.Id);

		command.Parameters.AddWithValue(
			"$Name",
			location.Name);

		command.Parameters.AddWithValue(
			"$Description",
			(object?)location.Description ?? DBNull.Value);

		command.ExecuteNonQuery();
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
		UPDATE Locations
		SET IsActive = 0
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		command.ExecuteNonQuery();
	}

	private static Location ReadLocation(
		SqliteDataReader reader)
	{
		return new Location
		{
			Id =
				reader.GetInt64(0),

			Name =
				reader.GetString(1),

			Description =
				reader.IsDBNull(2)
					? null
					: reader.GetString(2),

			IsActive =
				reader.GetInt64(3) == 1
		};
	}
}