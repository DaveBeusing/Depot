// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class PurposeRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, Name, Description, IsActive, Version";

	public PurposeRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<IReadOnlyList<Purpose>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QuerySliceAsync(
			$"SELECT {SelectColumns} FROM Purposes WHERE IsActive = 1 ORDER BY Name, Id",
			ReadPurpose,
			0,
			200,
			cancellationToken);

	public Task<IReadOnlyList<Purpose>> SearchAsync(string? searchText, bool? isActive, CancellationToken cancellationToken)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(Name LIKE $Search OR Description LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (isActive is not null)
		{
			filters.Add("IsActive = $IsActive");
			parameters.Add(Parameter("$IsActive", isActive.Value));
		}
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QuerySliceAsync($"SELECT {SelectColumns} FROM Purposes {where} ORDER BY Name, Id", ReadPurpose, 0, 200, cancellationToken, parameters.ToArray());
	}

	public Task<Purpose?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Purposes WHERE Id = $Id;",
			ReadPurpose,
			cancellationToken,
			Parameter("$Id", id));

	public Task<Purpose?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Purposes WHERE Name = $Name;",
			ReadPurpose,
			cancellationToken,
			Parameter("$Name", name));

	public Task<long> CreateAsync(Purpose purpose, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO Purposes (Name, Description, IsActive) VALUES ($Name, $Description, $IsActive);",
			cancellationToken,
			Parameter("$Name", purpose.Name),
			Parameter("$Description", purpose.Description),
			Parameter("$IsActive", purpose.IsActive));

	public async Task<bool> UpdateAsync(Purpose purpose, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Purposes SET Name = $Name, Description = $Description, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", purpose.Id),
			Parameter("$Name", purpose.Name),
			Parameter("$Description", purpose.Description),
			Parameter("$Version", purpose.Version)) == 1;

	public async Task<bool> DeactivateAsync(long id, long version, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Purposes SET IsActive = 0, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Purposes SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$IsActive", isActive),
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	public Purpose? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Purposes WHERE Id = $Id;",
			ReadPurpose,
			Parameter("$Id", id));

	public Purpose? GetByName(string name) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Purposes WHERE Name = $Name;",
			ReadPurpose,
			Parameter("$Name", name));

	public long Create(Purpose purpose) =>
		Database.Insert(
			"""
			INSERT INTO Purposes (Name, Description, IsActive)
			VALUES ($Name, $Description, $IsActive);
			""",
			Parameter("$Name", purpose.Name),
			Parameter("$Description", purpose.Description),
			Parameter("$IsActive", purpose.IsActive));

	public bool Update(Purpose purpose) =>
		Database.Execute(
			"""
			UPDATE Purposes
			SET Name = $Name, Description = $Description, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", purpose.Id),
			Parameter("$Name", purpose.Name),
			Parameter("$Description", purpose.Description),
			Parameter("$Version", purpose.Version)) == 1;

	public bool Deactivate(long id, long version) =>
		Database.Execute(
			"""
			UPDATE Purposes
			SET IsActive = 0, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	private static Purpose ReadPurpose(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			Name = reader.GetString(1),
			Description = reader.IsDBNull(2) ? null : reader.GetString(2),
			IsActive = reader.GetBoolean(3),
			Version = reader.GetInt64(4)
		};
}
