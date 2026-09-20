// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record WarehouseLayoutStockFact(
	long StorageLocationId,
	int InventoryContextCount,
	int StockedInventoryContextCount,
	long QuantityOnHand);

public sealed record WarehouseLayoutAttentionFact(
	long StorageLocationId,
	int AttentionCount);

public sealed record WarehouseLayoutWarehouseAttention(
	int DraftTransferCount,
	int ActiveInventoryCountCount);

public sealed record WarehouseLayoutLocationProjection(
	StorageLocation Location,
	int BoardIndex,
	int BoardRow,
	int BoardColumn,
	int InventoryContextCount,
	int StockedInventoryContextCount,
	long QuantityOnHand,
	int TransferAttentionCount,
	int InventoryCountAttentionCount,
	bool CanViewTransferAttention,
	bool CanViewInventoryCountAttention)
{
	public string LogicalGridPosition => $"R{BoardRow:00} / C{BoardColumn:00}";

	public string OccupancyDisplay =>
		InventoryContextCount == 0
			? "No inventory contexts"
			: $"{StockedInventoryContextCount:N0} stocked / {InventoryContextCount:N0} inventory contexts";

	public bool HasAttention =>
		TransferAttentionCount > 0 ||
		InventoryCountAttentionCount > 0;
}

public sealed record WarehouseLayoutSnapshot(
	Warehouse Warehouse,
	IReadOnlyList<WarehouseLayoutLocationProjection> Locations,
	int DraftTransferCount,
	int ActiveInventoryCountCount,
	bool CanViewTransferAttention,
	bool CanViewInventoryCountAttention)
{
	public int ActiveLocationCount => Locations.Count(location => location.Location.IsActive);

	public int InactiveLocationCount => Locations.Count - ActiveLocationCount;

	public long QuantityOnHand => Locations.Sum(location => location.QuantityOnHand);
}
