// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class StockService
{
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _movements;

	public StockService(InventoryRepository inventories, StockMovementRepository movements)
	{
		_inventories = inventories;
		_movements = movements;
	}

	public Task<PageResult<InventoryOverviewItem>> SearchInventoryOverviewAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken) =>
		_inventories.SearchOverviewPageAsync(searchText, pageNumber, pageSize, cancellationToken);

	public Task<IReadOnlyList<InventoryOverviewItem>> ListInventoryOverviewSliceAsync(
		string? searchText,
		int offset,
		int count,
		CancellationToken cancellationToken) =>
		_inventories.ListOverviewSliceAsync(searchText, offset, count, cancellationToken);

	public Task<InventoryReportSummary?> GetInventoryReportSummaryAsync(
		string? searchText,
		CancellationToken cancellationToken) =>
		_inventories.GetReportSummaryAsync(searchText, cancellationToken);

	public Task<IReadOnlyList<GroupedInventoryReportItem>> GetGroupedInventoryReportItemsAsync(
		string? searchText,
		GroupedInventoryReportType reportType,
		CancellationToken cancellationToken) =>
		_inventories.GetGroupedReportItemsAsync(searchText, reportType, cancellationToken);

	public async Task<InventoryDetails> GetInventoryDetailsAsync(
		long inventoryId,
		CancellationToken cancellationToken)
	{
		if (inventoryId <= 0) throw new ArgumentException("Inventory id is required.", nameof(inventoryId));
		var overviewTask = _inventories.GetOverviewByIdAsync(inventoryId, cancellationToken);
		var movementsTask = _movements.ListRecentForInventoryAsync(inventoryId, 20, cancellationToken);
		await Task.WhenAll(overviewTask, movementsTask);
		var overview = await overviewTask
			?? throw new InvalidOperationException($"Inventory with id '{inventoryId}' was not found.");
		return new InventoryDetails
		{
			InventoryId = overview.InventoryId,
			ItemId = overview.ItemId,
			PartNumber = overview.PartNumber,
			Description = overview.Description,
			Manufacturer = overview.Manufacturer,
			Category = overview.Category,
			PurposeName = overview.PurposeName,
			WarehouseName = overview.WarehouseName,
			LocationName = overview.LocationName,
			CurrentStock = overview.CurrentStock,
			AverageCost = overview.AverageCost,
			InventoryValue = overview.InventoryValue,
			RecentMovements = await movementsTask
		};
	}

	public async Task<DashboardData> GetDashboardDataAsync(CancellationToken cancellationToken)
	{
		var summaryTask = _inventories.GetDashboardSummaryAsync(cancellationToken);
		var recentTask = _movements.ListDashboardRecentAsync(10, cancellationToken);
		await Task.WhenAll(summaryTask, recentTask);
		return new DashboardData(await summaryTask ?? new DashboardSummary(), await recentTask);
	}
}
