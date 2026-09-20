// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class WarehouseLayoutVisualizerService
{
	private const int BoardColumnCount = 4;

	private readonly WarehouseLayoutReadRepository _repository;
	private readonly IAuthorizationService _authorization;

	public WarehouseLayoutVisualizerService(
		WarehouseLayoutReadRepository repository,
		IAuthorizationService authorization)
	{
		_repository = repository;
		_authorization = authorization;
	}

	public async Task<WarehouseLayoutSnapshot> GetSnapshotAsync(
		Warehouse warehouse,
		IReadOnlyList<StorageLocation> locations,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(warehouse);
		ArgumentNullException.ThrowIfNull(locations);
		_authorization.RequirePermission(ApplicationPermission.MasterDataView);

		var canViewTransfers = _authorization.HasPermission(ApplicationPermission.StockTransfersView);
		var canViewCounts = _authorization.HasPermission(ApplicationPermission.InventoryCountsView);

		var stockTask = _repository.ListStockFactsAsync(warehouse.Id, cancellationToken);
		var transferTask = canViewTransfers
			? _repository.ListDraftTransferAttentionAsync(warehouse.Id, cancellationToken)
			: Task.FromResult<IReadOnlyList<WarehouseLayoutAttentionFact>>([]);
		var countTask = canViewCounts
			? _repository.ListInventoryCountAttentionAsync(warehouse.Id, cancellationToken)
			: Task.FromResult<IReadOnlyList<WarehouseLayoutAttentionFact>>([]);
		var warehouseAttentionTask = _repository.GetWarehouseAttentionAsync(
			warehouse.Id,
			canViewTransfers,
			canViewCounts,
			cancellationToken);

		await Task.WhenAll(stockTask, transferTask, countTask, warehouseAttentionTask);

		var stockByLocation = (await stockTask).ToDictionary(fact => fact.StorageLocationId);
		var transfersByLocation = (await transferTask).ToDictionary(fact => fact.StorageLocationId);
		var countsByLocation = (await countTask).ToDictionary(fact => fact.StorageLocationId);

		var orderedLocations = locations
			.Where(location => location.WarehouseId == warehouse.Id)
			.OrderBy(location => location.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(location => location.Id)
			.ToArray();

		var projections = new List<WarehouseLayoutLocationProjection>(orderedLocations.Length);
		for (var index = 0; index < orderedLocations.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var location = orderedLocations[index];
			stockByLocation.TryGetValue(location.Id, out var stock);
			transfersByLocation.TryGetValue(location.Id, out var transferAttention);
			countsByLocation.TryGetValue(location.Id, out var countAttention);

			projections.Add(
				new WarehouseLayoutLocationProjection(
					location,
					index + 1,
					(index / BoardColumnCount) + 1,
					(index % BoardColumnCount) + 1,
					stock?.InventoryContextCount ?? 0,
					stock?.StockedInventoryContextCount ?? 0,
					stock?.QuantityOnHand ?? 0,
					transferAttention?.AttentionCount ?? 0,
					countAttention?.AttentionCount ?? 0,
					canViewTransfers,
					canViewCounts));
		}

		var warehouseAttention = await warehouseAttentionTask;
		return new WarehouseLayoutSnapshot(
			warehouse,
			projections,
			warehouseAttention.DraftTransferCount,
			warehouseAttention.ActiveInventoryCountCount,
			canViewTransfers,
			canViewCounts);
	}
}
