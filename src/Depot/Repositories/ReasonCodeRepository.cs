// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ReasonCodeRepository : DatabaseRepository
{
	private const string Columns = "Id, Name, Description, IsActive, Version";

	public ReasonCodeRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<IReadOnlyList<ReasonCode>> SearchAsync(
		string? searchText,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var filter = string.IsNullOrWhiteSpace(search)
			? string.Empty
			: "WHERE Name LIKE $Search OR Description LIKE $Search";
		var parameters = string.IsNullOrWhiteSpace(search)
			? []
			: new[] { Parameter("$Search", $"%{search}%") };
		return Database.QueryAsync(
			$"SELECT {Columns} FROM ReasonCodes {filter} ORDER BY IsActive DESC, Name;",
			Read,
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<ReasonCode>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {Columns} FROM ReasonCodes WHERE IsActive = 1 ORDER BY Name;",
			Read,
			cancellationToken);

	public IReadOnlyList<ReasonCode> GetAll() =>
		Database.Query($"SELECT {Columns} FROM ReasonCodes ORDER BY Name;", Read);

	public Task<ReasonCode?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM ReasonCodes WHERE Id = $Id;",
			Read,
			cancellationToken,
			Parameter("$Id", id));

	public ReasonCode? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {Columns} FROM ReasonCodes WHERE Id = $Id;",
			Read,
			Parameter("$Id", id));

	public Task<ReasonCode?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM ReasonCodes WHERE Name = $Name;",
			Read,
			cancellationToken,
			Parameter("$Name", name));

	public Task<long> CreateAsync(ReasonCode reasonCode, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO ReasonCodes (Name, Description, IsActive) VALUES ($Name, $Description, $IsActive);",
			cancellationToken,
			Parameter("$Name", reasonCode.Name),
			Parameter("$Description", reasonCode.Description),
			Parameter("$IsActive", reasonCode.IsActive));

	public async Task<bool> UpdateAsync(ReasonCode reasonCode, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE ReasonCodes SET Name = $Name, Description = $Description, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", reasonCode.Id),
			Parameter("$Name", reasonCode.Name),
			Parameter("$Description", reasonCode.Description),
			Parameter("$Version", reasonCode.Version)) == 1;

	public async Task<bool> SetActiveAsync(
		long id,
		long version,
		bool isActive,
		CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE ReasonCodes SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$IsActive", isActive)) == 1;

	private static ReasonCode Read(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Name = reader.GetString(1),
		Description = reader.IsDBNull(2) ? null : reader.GetString(2),
		IsActive = reader.GetBoolean(3),
		Version = reader.GetInt64(4)
	};
}
