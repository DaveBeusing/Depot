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
		Database.QueryAsync(
			$"SELECT {SelectColumns} FROM Purposes WHERE IsActive = 1 ORDER BY Name;",
			ReadPurpose,
			cancellationToken);

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

	public IReadOnlyList<Purpose> GetAll() =>
		Database.Query(
			$"SELECT {SelectColumns} FROM Purposes WHERE IsActive = 1 ORDER BY Name;",
			ReadPurpose);

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
