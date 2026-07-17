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

	public Task<PageResult<User>> SearchPageAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch ? "WHERE Email LIKE $Search OR DisplayName LIKE $Search" : string.Empty;
		var parameters = hasSearch ? new[] { Parameter("$Search", $"%{search}%") } : [];
		return Database.QueryPageAsync(
			$"SELECT {SelectColumns} FROM Users {filter} ORDER BY IsActive DESC, Email",
			$"SELECT COUNT(*) FROM Users {filter};",
			ReadUser,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
	{
		var predicate = DatabaseAccess.CaseInsensitiveEquals("Email", "$Email");
		return Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Users WHERE {predicate};",
			ReadUser,
			cancellationToken,
			Parameter("$Email", email));
	}

	public Task<UserAuthentication?> GetAuthenticationByEmailAsync(
		string email,
		CancellationToken cancellationToken)
	{
		var predicate = DatabaseAccess.CaseInsensitiveEquals("Email", "$Email");
		return Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns}, PasswordHash FROM Users WHERE {predicate};",
			ReadAuthentication,
			cancellationToken,
			Parameter("$Email", email));
	}

	public Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Users WHERE Id = $Id;",
			ReadUser,
			cancellationToken,
			Parameter("$Id", id));

	public Task<long> CreateAsync(User user, string passwordHash, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"""
			INSERT INTO Users
			(Email, DisplayName, PasswordHash, IsAdministrator, IsActive, CreatedUtc)
			VALUES ($Email, $DisplayName, $PasswordHash, $IsAdministrator, $IsActive, $CreatedUtc);
			""",
			cancellationToken,
			Parameter("$Email", user.Email),
			Parameter("$DisplayName", user.DisplayName),
			Parameter("$PasswordHash", passwordHash),
			Parameter("$IsAdministrator", user.IsAdministrator),
			Parameter("$IsActive", user.IsActive),
			Parameter("$CreatedUtc", user.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)));

	public async Task<bool> UpdateAsync(
		User user,
		string? passwordHash,
		CancellationToken cancellationToken)
	{
		var passwordAssignment = passwordHash is null ? string.Empty : "PasswordHash = $PasswordHash,";
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$Id", user.Id),
			Parameter("$Email", user.Email),
			Parameter("$DisplayName", user.DisplayName),
			Parameter("$IsAdministrator", user.IsAdministrator),
			Parameter("$Version", user.Version)
		};
		if (passwordHash is not null)
		{
			parameters.Add(Parameter("$PasswordHash", passwordHash));
		}

		return await Database.ExecuteAsync(
			$"""
			UPDATE Users
			SET Email = $Email, DisplayName = $DisplayName, {passwordAssignment}
			    IsAdministrator = $IsAdministrator, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			cancellationToken,
			parameters.ToArray()) == 1;
	}

	public async Task<bool> SetActiveAsync(
		long id,
		bool isActive,
		long version,
		CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Users SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$IsActive", isActive),
			Parameter("$Version", version)) == 1;

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
