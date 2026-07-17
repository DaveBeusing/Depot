// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class StorageLocationRepository : DatabaseRepository
{
	private const string Columns = "Id, WarehouseId, Name, Description, IsActive, Version";

	public StorageLocationRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<StorageLocation>> SearchAsync(
		long? warehouseId,
		string? searchText,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = warehouseId is null ? "WHERE 1 = 1" : "WHERE WarehouseId = $WarehouseId";
		if (hasSearch) filter += " AND (Name LIKE $Search OR Description LIKE $Search)";
		var parameters = new List<DatabaseParameter>();
		if (warehouseId is not null) parameters.Add(Parameter("$WarehouseId", warehouseId.Value));
		if (hasSearch) parameters.Add(Parameter("$Search", $"%{search}%"));
		return Database.QueryAsync(
			$"SELECT {Columns} FROM StorageLocations {filter} ORDER BY IsActive DESC, Name;",
			Read,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<IReadOnlyList<StorageLocation>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {Columns} FROM StorageLocations WHERE IsActive = 1 ORDER BY WarehouseId, Name;",
			Read,
			cancellationToken);

	public IReadOnlyList<StorageLocation> GetAll() =>
		Database.Query($"SELECT {Columns} FROM StorageLocations ORDER BY WarehouseId, Name;", Read);

	public Task<StorageLocation?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM StorageLocations WHERE Id = $Id;",
			Read,
			cancellationToken,
			Parameter("$Id", id));

	public async Task<bool> HasActiveForWarehouseAsync(long warehouseId, CancellationToken cancellationToken) =>
		Convert.ToInt64(await Database.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM StorageLocations WHERE WarehouseId = $WarehouseId AND IsActive = 1;",
			cancellationToken,
			Parameter("$WarehouseId", warehouseId))) > 0;

	public Task<StorageLocation?> GetByNameAsync(long warehouseId, string name, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM StorageLocations WHERE WarehouseId = $WarehouseId AND Name = $Name;",
			Read,
			cancellationToken,
			Parameter("$WarehouseId", warehouseId),
			Parameter("$Name", name));

	public Task<long> CreateAsync(StorageLocation location, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive) VALUES ($WarehouseId, $Name, $Description, $IsActive);",
			cancellationToken,
			Parameter("$WarehouseId", location.WarehouseId),
			Parameter("$Name", location.Name),
			Parameter("$Description", location.Description),
			Parameter("$IsActive", location.IsActive));

	public async Task<bool> UpdateAsync(StorageLocation location, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE StorageLocations SET WarehouseId = $WarehouseId, Name = $Name, Description = $Description, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", location.Id),
			Parameter("$WarehouseId", location.WarehouseId),
			Parameter("$Name", location.Name),
			Parameter("$Description", location.Description),
			Parameter("$Version", location.Version)) == 1;

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE StorageLocations SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version),
			Parameter("$IsActive", isActive)) == 1;

	private static StorageLocation Read(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		WarehouseId = reader.GetInt64(1),
		Name = reader.GetString(2),
		Description = reader.IsDBNull(3) ? null : reader.GetString(3),
		IsActive = reader.GetBoolean(4),
		Version = reader.GetInt64(5)
	};
}
