// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class WarehouseRepository : DatabaseRepository
{
	private const string Columns = "Id, Name, Description, IsActive, Version";

	public WarehouseRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<Warehouse>> SearchAsync(string? searchText, CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var filter = string.IsNullOrWhiteSpace(search) ? string.Empty : "WHERE Name LIKE $Search OR Description LIKE $Search";
		var parameters = string.IsNullOrWhiteSpace(search) ? [] : new[] { Parameter("$Search", $"%{search}%") };
		return Database.QueryAsync(
			$"SELECT {Columns} FROM Warehouses {filter} ORDER BY IsActive DESC, Name;",
			Read,
			cancellationToken,
			parameters);
	}

	public IReadOnlyList<Warehouse> GetAll() =>
		Database.Query($"SELECT {Columns} FROM Warehouses ORDER BY Name;", Read);

	public Task<Warehouse?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM Warehouses WHERE Id = $Id;",
			Read,
			cancellationToken,
			Parameter("$Id", id));

	public Task<Warehouse?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM Warehouses WHERE Name = $Name;",
			Read,
			cancellationToken,
			Parameter("$Name", name));

	public Task<long> CreateAsync(Warehouse warehouse, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO Warehouses (Name, Description, IsActive) VALUES ($Name, $Description, $IsActive);",
			cancellationToken,
			Parameter("$Name", warehouse.Name),
			Parameter("$Description", warehouse.Description),
			Parameter("$IsActive", warehouse.IsActive));

	public async Task<bool> UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Warehouses SET Name = $Name, Description = $Description, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", warehouse.Id),
			Parameter("$Name", warehouse.Name),
			Parameter("$Description", warehouse.Description),
			Parameter("$Version", warehouse.Version)) == 1;

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Warehouses SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$IsActive", isActive)) == 1;

	private static Warehouse Read(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Name = reader.GetString(1),
		Description = reader.IsDBNull(2) ? null : reader.GetString(2),
		IsActive = reader.GetBoolean(3),
		Version = reader.GetInt64(4)
	};
}
