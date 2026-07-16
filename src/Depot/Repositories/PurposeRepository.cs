// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using System.Data.Common;

namespace Depot.Repositories;

/// <summary>
/// Provides persistence for purposes.
/// </summary>
public sealed class PurposeRepository
{
	private readonly IDatabaseConnectionFactory _connectionFactory;

	public PurposeRepository(
		IDatabaseConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public IReadOnlyList<Purpose> GetAll()
	{
		var purposes =
			new List<Purpose>();

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
			IsActive,
			Version
		FROM Purposes
		WHERE IsActive = 1
		ORDER BY Name;
		""";

		using var reader =
			command.ExecuteReader();

		while (reader.Read())
		{
			purposes.Add(
				ReadPurpose(reader));
		}

		return purposes;
	}

	public Purpose? GetById(
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
			IsActive,
			Version
		FROM Purposes
		WHERE Id = $Id;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		using var reader =
			command.ExecuteReader();

		return reader.Read()
			? ReadPurpose(reader)
			: null;
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
			IsActive,
			Version
		FROM Purposes
		WHERE Name = $Name;
		""";

		command.Parameters.AddWithValue(
			"$Name",
			name);

		using var reader =
			command.ExecuteReader();

		return reader.Read()
			? ReadPurpose(reader)
			: null;
	}

	public long Create(
		Purpose purpose)
	{
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
			$IsActive
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$Name",
			purpose.Name);

		command.Parameters.AddWithValue(
			"$Description",
			(object?)purpose.Description ?? DBNull.Value);

		command.Parameters.AddWithValue(
			"$IsActive",
			purpose.IsActive);

		return (long)command.ExecuteScalar()!;
	}

	public bool Update(
		Purpose purpose)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		UPDATE Purposes
		SET
			Name = $Name,
			Description = $Description,
			Version = Version + 1
		WHERE Id = $Id AND Version = $Version;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			purpose.Id);

		command.Parameters.AddWithValue(
			"$Name",
			purpose.Name);

		command.Parameters.AddWithValue(
			"$Description",
			(object?)purpose.Description ?? DBNull.Value);

		command.Parameters.AddWithValue("$Version", purpose.Version);

		return command.ExecuteNonQuery() == 1;
	}

	public bool Deactivate(
		long id,
		long version)
	{
		using var connection =
			_connectionFactory.CreateConnection();

		connection.Open();

		using var command =
			connection.CreateCommand();

		command.CommandText =
		"""
		UPDATE Purposes
		SET IsActive = 0, Version = Version + 1
		WHERE Id = $Id AND Version = $Version;
		""";

		command.Parameters.AddWithValue(
			"$Id",
			id);

		command.Parameters.AddWithValue("$Version", version);

		return command.ExecuteNonQuery() == 1;
	}

	private static Purpose ReadPurpose(
		DbDataReader reader)
	{
		return new Purpose
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
				reader.GetInt64(3) == 1,

			Version =
				reader.GetInt64(4)
		};
	}
}
