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
		var users = new List<User>();
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		SELECT Id, Email, DisplayName, IsAdministrator, IsActive, CreatedUtc, Version
		FROM Users
		ORDER BY IsActive DESC, Email;
		""";
		using var reader = command.ExecuteReader();

		while (reader.Read())
		{
			users.Add(ReadUser(reader));
		}

		return users;
	}

	public User? GetByEmail(string email)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		SELECT Id, Email, DisplayName, IsAdministrator, IsActive, CreatedUtc, Version
		FROM Users
		WHERE Email = $Email COLLATE NOCASE;
		""";
		command.Parameters.AddWithValue("$Email", email);
		using var reader = command.ExecuteReader();
		return reader.Read() ? ReadUser(reader) : null;
	}

	public UserAuthentication? GetAuthenticationByEmail(string email)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		SELECT Id, Email, DisplayName, IsAdministrator, IsActive, CreatedUtc, Version, PasswordHash
		FROM Users
		WHERE Email = $Email COLLATE NOCASE;
		""";
		command.Parameters.AddWithValue("$Email", email);
		using var reader = command.ExecuteReader();

		return reader.Read()
			? new UserAuthentication
			{
				User = ReadUser(reader),
				PasswordHash = reader.GetString(7)
			}
			: null;
	}

	public User? GetById(long id)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		SELECT Id, Email, DisplayName, IsAdministrator, IsActive, CreatedUtc, Version
		FROM Users
		WHERE Id = $Id;
		""";
		command.Parameters.AddWithValue("$Id", id);
		using var reader = command.ExecuteReader();
		return reader.Read() ? ReadUser(reader) : null;
	}

	public long Create(User user, string passwordHash)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText =
		"""
		INSERT INTO Users
		(
			Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc
		)
		VALUES
		(
			$Email, $DisplayName, $PasswordHash, $IsAdministrator, $IsActive, $CreatedUtc
		);

		SELECT last_insert_rowid();
		""";
		AddUserParameters(command, user);
		command.Parameters.AddWithValue("$PasswordHash", passwordHash);
		return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	public bool Update(User user, string? passwordHash)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = passwordHash is null
			? """
			  UPDATE Users
			  SET Email = $Email, DisplayName = $DisplayName, IsAdministrator = $IsAdministrator,
			      Version = Version + 1
			  WHERE Id = $Id AND Version = $Version;
			  """
			: """
			  UPDATE Users
			  SET Email = $Email, DisplayName = $DisplayName, PasswordHash = $PasswordHash,
			      IsAdministrator = $IsAdministrator, Version = Version + 1
			  WHERE Id = $Id AND Version = $Version;
			  """;
		command.Parameters.AddWithValue("$Id", user.Id);
		command.Parameters.AddWithValue("$Email", user.Email);
		command.Parameters.AddWithValue("$DisplayName", user.DisplayName);
		command.Parameters.AddWithValue("$IsAdministrator", user.IsAdministrator);
		command.Parameters.AddWithValue("$Version", user.Version);
		if (passwordHash is not null)
		{
			command.Parameters.AddWithValue("$PasswordHash", passwordHash);
		}
		return command.ExecuteNonQuery() == 1;
	}

	public bool SetActive(long id, bool isActive, long version)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "UPDATE Users SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;";
		command.Parameters.AddWithValue("$Id", id);
		command.Parameters.AddWithValue("$IsActive", isActive);
		command.Parameters.AddWithValue("$Version", version);
		return command.ExecuteNonQuery() == 1;
	}

	private static void AddUserParameters(SqliteCommand command, User user)
	{
		command.Parameters.AddWithValue("$Email", user.Email);
		command.Parameters.AddWithValue("$DisplayName", user.DisplayName);
		command.Parameters.AddWithValue("$IsAdministrator", user.IsAdministrator);
		command.Parameters.AddWithValue("$IsActive", user.IsActive);
		command.Parameters.AddWithValue(
			"$CreatedUtc",
			user.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
	}

	private static User ReadUser(SqliteDataReader reader)
	{
		return new User
		{
			Id = reader.GetInt64(0),
			Email = reader.GetString(1),
			DisplayName = reader.GetString(2),
			IsAdministrator = reader.GetInt64(3) == 1,
			IsActive = reader.GetInt64(4) == 1,
			CreatedUtc = DateTime.Parse(
				reader.GetString(5),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
			Version = reader.GetInt64(6)
		};
	}
}
