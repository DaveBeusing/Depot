// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class WarehouseLayoutVisualizerTests : IDisposable
{
	private readonly string _databasePath =
		Path.Combine(Path.GetTempPath(), $"depot-warehouse-layout-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task ReadProjectionAggregatesExistingStockWithoutPersistingLayout()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var database = new DatabaseAccess(factory);
		var warehouses = new WarehouseRepository(database);
		var locations = new StorageLocationRepository(database);
		var warehouse = await warehouses.GetByNameAsync("Main Warehouse", CancellationToken.None)
			?? throw new InvalidOperationException("The default warehouse was not initialized.");
		var location = await locations.GetByNameAsync(warehouse.Id, "Default", CancellationToken.None)
			?? throw new InvalidOperationException("The default storage location was not initialized.");

		var itemId = await database.InsertAsync(
			"INSERT INTO Items (PartNumber, Description, IsActive) VALUES ($PartNumber, $Description, 1);",
			CancellationToken.None,
			new DatabaseParameter("$PartNumber", "LAYOUT-001"),
			new DatabaseParameter("$Description", "Layout visualizer stock"));
		var purposeId = Convert.ToInt64(
			await database.ExecuteScalarAsync("SELECT MIN(Id) FROM Purposes;", CancellationToken.None),
			System.Globalization.CultureInfo.InvariantCulture);
		var inventoryId = await database.InsertAsync(
			"INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive) VALUES ($ItemId, $PurposeId, $LocationId, 1);",
			CancellationToken.None,
			new DatabaseParameter("$ItemId", itemId),
			new DatabaseParameter("$PurposeId", purposeId),
			new DatabaseParameter("$LocationId", location.Id));
		await database.InsertAsync(
			"INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference) VALUES ($InventoryId, $Type, $Timestamp, 12, 1, $Reference);",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", inventoryId),
			new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance),
			new DatabaseParameter("$Timestamp", DateTime.UtcNow.ToString("O")),
			new DatabaseParameter("$Reference", "Layout projection test"));

		var facts = await new WarehouseLayoutReadRepository(database)
			.ListStockFactsAsync(warehouse.Id, CancellationToken.None);
		var fact = Assert.Single(facts, candidate => candidate.StorageLocationId == location.Id);

		Assert.Equal(1, fact.InventoryContextCount);
		Assert.Equal(1, fact.StockedInventoryContextCount);
		Assert.Equal(12, fact.QuantityOnHand);
		Assert.Equal(0L, Convert.ToInt64(
			await database.ExecuteScalarAsync(
				"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('WarehouseLayout','StorageLocationLayout');",
				CancellationToken.None),
			System.Globalization.CultureInfo.InvariantCulture));
		Assert.Equal(30, DatabaseVersion.CurrentVersion);
	}

	[Fact]
	public async Task VisualizerUsesDeterministicLogicalSlotsAndPermissionGatedAttention()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var database = new DatabaseAccess(factory);
		var warehouseRepository = new WarehouseRepository(database);
		var locationRepository = new StorageLocationRepository(database);
		var authorization = new AuthorizationService();
		var administrator = new UserRepository(database).GetByEmail("admin@depot.local")
			?? throw new InvalidOperationException("The test administrator was not initialized.");
		authorization.SignIn(administrator, [ApplicationPermission.MasterDataView]);

		var warehouse = await warehouseRepository.GetByNameAsync("Main Warehouse", CancellationToken.None)
			?? throw new InvalidOperationException("The default warehouse was not initialized.");
		await database.InsertAsync(
			"INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive) VALUES ($WarehouseId, 'Zulu', NULL, 1);",
			CancellationToken.None,
			new DatabaseParameter("$WarehouseId", warehouse.Id));
		await database.InsertAsync(
			"INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive) VALUES ($WarehouseId, 'Alpha', NULL, 1);",
			CancellationToken.None,
			new DatabaseParameter("$WarehouseId", warehouse.Id));

		var locations = await locationRepository.SearchAsync(warehouse.Id, null, CancellationToken.None);
		var service = new WarehouseLayoutVisualizerService(
			new WarehouseLayoutReadRepository(database),
			authorization);
		var snapshot = await service.GetSnapshotAsync(warehouse, locations, CancellationToken.None);

		Assert.Equal(new[] { "Alpha", "Default", "Zulu" }, snapshot.Locations.Select(item => item.Location.Name).ToArray());
		Assert.Equal(new[] { "R01 / C01", "R01 / C02", "R01 / C03" }, snapshot.Locations.Select(item => item.LogicalGridPosition).ToArray());
		Assert.False(snapshot.CanViewTransferAttention);
		Assert.False(snapshot.CanViewInventoryCountAttention);
		Assert.All(snapshot.Locations, item => Assert.Equal("Restricted", item.TransferAttentionDisplay));
		Assert.All(snapshot.Locations, item => Assert.Equal("Restricted", item.InventoryCountAttentionDisplay));
	}

	[Fact]
	public void VisualizerContractIsReadOnlyVirtualizedAndSelectionSynchronized()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Warehouses", "WarehouseStructureView.xaml"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "Warehouses", "WarehouseStructureViewModel.cs"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "WarehouseLayoutVisualizerService.cs"));
		var repository = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "WarehouseLayoutReadRepository.cs"));

		Assert.Contains("Header=\"Layout visualizer\"", view, StringComparison.Ordinal);
		Assert.Contains("do not represent physical coordinates", view, StringComparison.Ordinal);
		Assert.Contains("It is not a physical capacity measurement.", view, StringComparison.Ordinal);
		Assert.Contains("IsReadOnly=\"True\"", view, StringComparison.Ordinal);
		Assert.Contains("EnableRowVirtualization=\"True\"", view, StringComparison.Ordinal);
		Assert.Contains("EnableColumnVirtualization=\"True\"", view, StringComparison.Ordinal);
		Assert.Contains("SelectedItem=\"{Binding SelectedLayoutLocation}\"", view, StringComparison.Ordinal);
		Assert.Contains("SelectedStorageLocation = StorageLocations.FirstOrDefault", viewModel, StringComparison.Ordinal);
		Assert.Contains("await LoadLayoutAsync(warehouse.Id, cancellationToken);", viewModel, StringComparison.Ordinal);
		Assert.Contains("StorageLocationService", viewModel, StringComparison.Ordinal);
		Assert.Contains("RequirePermission(ApplicationPermission.MasterDataView)", service, StringComparison.Ordinal);
		Assert.DoesNotContain("SaveAsync", service, StringComparison.Ordinal);
		Assert.DoesNotContain("INSERT INTO", repository, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("UPDATE ", repository, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("DELETE FROM", repository, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("Drag", view, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("Resize", view, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("Zoom", view, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("PanCommand", view, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ProjectionQueriesAreBoundedAndSetBased()
	{
		var root = FindRepositoryRoot();
		var repository = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "WarehouseLayoutReadRepository.cs"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "WarehouseLayoutVisualizerService.cs"));

		Assert.Contains("GROUP BY sl.Id", repository, StringComparison.Ordinal);
		Assert.Contains("COUNT(DISTINCT attention.TransferId)", repository, StringComparison.Ordinal);
		Assert.Contains("COUNT(DISTINCT inventoryCount.Id)", repository, StringComparison.Ordinal);
		Assert.Contains("Task.WhenAll", service, StringComparison.Ordinal);
		Assert.DoesNotContain("foreach (var location", repository, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
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
