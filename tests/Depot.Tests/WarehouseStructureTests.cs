// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class WarehouseStructureTests : IDisposable
{
	private readonly string _databasePath =
		Path.Combine(Path.GetTempPath(), $"depot-warehouse-structure-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task WarehouseAndStorageLocationActivationRulesPreserveHierarchy()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var database = new DatabaseAccess(factory);
		var warehouseRepository = new WarehouseRepository(database);
		var storageLocationRepository = new StorageLocationRepository(database);
		var authorization = new AuthorizationService();
		var administrator = new UserRepository(database).GetByEmail("admin@depot.local")
			?? throw new InvalidOperationException("The test administrator was not initialized.");
		authorization.SignIn(administrator);
		var audit = new AuditService(new AuditRepository(database), authorization);
		var warehouseService = new WarehouseService(warehouseRepository, storageLocationRepository, audit);
		var storageLocationService = new StorageLocationService(storageLocationRepository, warehouseRepository, audit);
		var warehouse = await warehouseRepository.GetByNameAsync("Main Warehouse", CancellationToken.None)
			?? throw new InvalidOperationException("The default warehouse was not initialized.");
		var location = await storageLocationRepository.GetByNameAsync(warehouse.Id, "Default", CancellationToken.None)
			?? throw new InvalidOperationException("The default storage location was not initialized.");

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => warehouseService.SetActiveAsync(warehouse.Id, warehouse.Version, false));

		location = await storageLocationService.SetActiveAsync(location.Id, location.Version, false);
		warehouse = await warehouseService.SetActiveAsync(warehouse.Id, warehouse.Version, false);

		Assert.False(location.IsActive);
		Assert.False(warehouse.IsActive);
		Assert.Contains(
			await warehouseService.SearchAsync("Main"),
			item => item.Id == warehouse.Id && !item.IsActive);
		await Assert.ThrowsAsync<InvalidOperationException>(
			() => storageLocationService.SetActiveAsync(location.Id, location.Version, true));

		warehouse = await warehouseService.SetActiveAsync(warehouse.Id, warehouse.Version, true);
		location = await storageLocationService.SetActiveAsync(location.Id, location.Version, true);

		Assert.True(warehouse.IsActive);
		Assert.True(location.IsActive);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath))
		{
			File.Delete(_databasePath);
		}
	}
}
