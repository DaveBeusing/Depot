// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

namespace Depot.Repositories;

public sealed class UserRepository
{
	private readonly SqliteConnectionFactory _connectionFactory;

	public UserRepository(SqliteConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public IReadOnlyList<User> GetAll()
	{
		var users =	new List<User>();

		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command = connection.CreateCommand();

		command.CommandText =
		"""
		SELECT
			Id,
			UserName,
			DisplayName,
			IsAdministrator,
			IsActive,
			CreatedUtc
		FROM Users
		ORDER BY
			IsActive DESC,
			UserName;
		""";

		using var reader = command.ExecuteReader();

		while (reader.Read())
		{
			users.Add(ReadUser(reader));
		}

		return users;
	}

	public User? GetByUserName(string userName)
	{
		using var connection = _connectionFactory.CreateConnection();

		connection.Open();

		using var command =	connection.CreateCommand();

		command.CommandText =
		"""
		SELECT
			Id,
			UserName,
			DisplayName,
			IsAdministrator,
			IsActive,
			CreatedUtc
		FROM Users
		WHERE UserName = $UserName;
		""";

		command.Parameters.AddWithValue("$UserName", userName);

		using var reader = command.ExecuteReader();

		return reader.Read() ? ReadUser(reader)	: null;
	}

	public long Create(User user)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command =	connection.CreateCommand();
		command.CommandText =
		"""
		INSERT INTO Users
		(
			UserName,
			DisplayName,
			IsAdministrator,
			IsActive,
			CreatedUtc
		)
		VALUES
		(
			$UserName,
			$DisplayName,
			$IsAdministrator,
			$IsActive,
			$CreatedUtc
		);

		SELECT last_insert_rowid();
		""";

		command.Parameters.AddWithValue(
			"$UserName",
			user.UserName);

		command.Parameters.AddWithValue(
			"$DisplayName",
			user.DisplayName);

		command.Parameters.AddWithValue(
			"$IsAdministrator",
			user.IsAdministrator);

		command.Parameters.AddWithValue(
			"$IsActive",
			user.IsActive);

		command.Parameters.AddWithValue(
			"$CreatedUtc",
			user.CreatedUtc.ToString(
				"O",
				CultureInfo.InvariantCulture));

		return (long)command.ExecuteScalar()!;
	}

	private static User ReadUser(SqliteDataReader reader)
	{
		return new User
		{
			Id = reader.GetInt64(0),
			UserName = reader.GetString(1),
			DisplayName = reader.GetString(2),
			IsAdministrator = reader.GetInt64(3) == 1,
			IsActive = reader.GetInt64(4) == 1,
			CreatedUtc =
				DateTime.Parse(
					reader.GetString(5),
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal)
		};
	}

	public User? GetById(long id)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		SELECT
			Id,
			UserName,
			DisplayName,
			IsAdministrator,
			IsActive,
			CreatedUtc
		FROM Users
		WHERE Id = $Id;
		""";
		command.Parameters.AddWithValue("$Id", id);
		using var reader = command.ExecuteReader();
		return reader.Read() ? ReadUser(reader) : null;
	}

	public void Update(User user)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command =	connection.CreateCommand();
		command.CommandText =
		"""
		UPDATE Users
		SET
			DisplayName = $DisplayName,
			IsAdministrator = $IsAdministrator
		WHERE Id = $Id;
		""";
		command.Parameters.AddWithValue("$Id", user.Id);
		command.Parameters.AddWithValue("$DisplayName", user.DisplayName);
		command.Parameters.AddWithValue("$IsAdministrator", user.IsAdministrator);
		command.ExecuteNonQuery();
	}

	public void SetActive(long id, bool isActive)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command =	connection.CreateCommand();
		command.CommandText =
		"""
		UPDATE Users
		SET IsActive = $IsActive
		WHERE Id = $Id;
		""";
		command.Parameters.AddWithValue("$Id", id);
		command.Parameters.AddWithValue("$IsActive", isActive);
		command.ExecuteNonQuery();
	}

}
