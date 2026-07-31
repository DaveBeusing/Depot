// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ReasonCodeRepository : DatabaseRepository
{
	private const string Columns = "Id, Code, Name, Description, IsSystem, IsActive, Version";

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
			: "WHERE Code LIKE $Search OR Name LIKE $Search OR Description LIKE $Search";
		var parameters = string.IsNullOrWhiteSpace(search)
			? []
			: new[] { Parameter("$Search", $"%{search}%") };
		return Database.QueryAsync(
			$"SELECT {Columns} FROM ReasonCodes {filter} ORDER BY IsActive DESC, Name, Code;",
			Read,
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<ReasonCode>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {Columns} FROM ReasonCodes WHERE IsActive = 1 ORDER BY Name, Code;",
			Read,
			cancellationToken);

	public IReadOnlyList<ReasonCode> GetAll() =>
		Database.Query($"SELECT {Columns} FROM ReasonCodes ORDER BY Name, Code;", Read);

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

	public Task<ReasonCode?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM ReasonCodes WHERE Code = $Code;",
			Read,
			cancellationToken,
			Parameter("$Code", code));

	public Task<ReasonCode?> GetByCodeAsync(
		DatabaseTransactionContext transaction,
		string code,
		CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM ReasonCodes WHERE Code = $Code;",
			Read,
			cancellationToken,
			Parameter("$Code", code));

	public Task<long> CreateAsync(ReasonCode reasonCode, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO ReasonCodes (Code, Name, Description, IsSystem, IsActive) VALUES ($Code, $Name, $Description, $IsSystem, $IsActive);",
			cancellationToken,
			Parameter("$Code", reasonCode.Code),
			Parameter("$Name", reasonCode.Name),
			Parameter("$Description", reasonCode.Description),
			Parameter("$IsSystem", reasonCode.IsSystem),
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
		Code = reader.GetString(1),
		Name = reader.GetString(2),
		Description = reader.IsDBNull(3) ? null : reader.GetString(3),
		IsSystem = reader.GetBoolean(4),
		IsActive = reader.GetBoolean(5),
		Version = reader.GetInt64(6)
	};
}
