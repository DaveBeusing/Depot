// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class UserRepository : DatabaseRepository
{
	private const string SelectColumns =
		"Id, Email, DisplayName, IsAdministrator, IsActive, CreatedUtc, Version";

	public UserRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public IReadOnlyList<User> GetAll() =>
		Database.Query(
			$"SELECT {SelectColumns} FROM Users ORDER BY IsActive DESC, Email;",
			ReadUser);

	public User? GetByEmail(string email)
	{
		var predicate = DatabaseAccess.CaseInsensitiveEquals("Email", "$Email");
		return Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Users WHERE {predicate};",
			ReadUser,
			Parameter("$Email", email));
	}

	public UserAuthentication? GetAuthenticationByEmail(string email)
	{
		var predicate = DatabaseAccess.CaseInsensitiveEquals("Email", "$Email");
		return Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns}, PasswordHash FROM Users WHERE {predicate};",
			ReadAuthentication,
			Parameter("$Email", email));
	}

	public User? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Users WHERE Id = $Id;",
			ReadUser,
			Parameter("$Id", id));

	public long Create(User user, string passwordHash) =>
		Database.Insert(
			"""
			INSERT INTO Users
			(Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc)
			VALUES
			($Email, $DisplayName, $PasswordHash, $IsAdministrator, $IsActive, $CreatedUtc);
			""",
			Parameter("$Email", user.Email),
			Parameter("$DisplayName", user.DisplayName),
			Parameter("$PasswordHash", passwordHash),
			Parameter("$IsAdministrator", user.IsAdministrator),
			Parameter("$IsActive", user.IsActive),
			Parameter("$CreatedUtc", user.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)));

	public bool Update(User user, string? passwordHash)
	{
		if (passwordHash is null)
		{
			return Database.Execute(
				"""
				UPDATE Users
				SET Email = $Email, DisplayName = $DisplayName, IsAdministrator = $IsAdministrator,
				    Version = Version + 1
				WHERE Id = $Id AND Version = $Version;
				""",
				Parameter("$Id", user.Id),
				Parameter("$Email", user.Email),
				Parameter("$DisplayName", user.DisplayName),
				Parameter("$IsAdministrator", user.IsAdministrator),
				Parameter("$Version", user.Version)) == 1;
		}

		return Database.Execute(
			"""
			UPDATE Users
			SET Email = $Email, DisplayName = $DisplayName, PasswordHash = $PasswordHash,
			    IsAdministrator = $IsAdministrator, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", user.Id),
			Parameter("$Email", user.Email),
			Parameter("$DisplayName", user.DisplayName),
			Parameter("$PasswordHash", passwordHash),
			Parameter("$IsAdministrator", user.IsAdministrator),
			Parameter("$Version", user.Version)) == 1;
	}

	public bool SetActive(long id, bool isActive, long version) =>
		Database.Execute(
			"""
			UPDATE Users
			SET IsActive = $IsActive, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", id),
			Parameter("$IsActive", isActive),
			Parameter("$Version", version)) == 1;

	private static UserAuthentication ReadAuthentication(DbDataReader reader) =>
		new()
		{
			User = ReadUser(reader),
			PasswordHash = reader.GetString(7)
		};

	private static User ReadUser(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			Email = reader.GetString(1),
			DisplayName = reader.GetString(2),
			IsAdministrator = reader.GetBoolean(3),
			IsActive = reader.GetBoolean(4),
			CreatedUtc = DateTime.Parse(
				reader.GetString(5),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
			Version = reader.GetInt64(6)
		};
}
