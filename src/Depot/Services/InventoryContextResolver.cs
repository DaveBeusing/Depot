// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

internal static class InventoryContextResolver
{
	public static InventoryMovementContext? ResolveMovement(
		StockMovement movement,
		IReadOnlyDictionary<long, Item> items,
		IReadOnlyDictionary<long, Inventory> inventoriesById,
		IReadOnlyDictionary<long, Purpose> purposes,
		IReadOnlyDictionary<long, StorageLocation> storageLocations,
		IReadOnlyDictionary<long, Warehouse> warehouses)
	{
		if (!inventoriesById.TryGetValue(
				movement.InventoryId,
				out var inventory) ||
			!items.TryGetValue(
				inventory.ItemId,
				out var item))
		{
			return null;
		}

		storageLocations.TryGetValue(inventory.StorageLocationId, out var storageLocation);
		return new InventoryMovementContext
		{
			InventoryId =
				inventory.Id,

			ItemId =
				item.Id,

			PartNumber =
				item.PartNumber,

			Description =
				item.Description,

			PurposeName =
				GetPurposeName(
					purposes,
					inventory.PurposeId),

			WarehouseName = storageLocation is null
				? "Unknown warehouse"
				: GetWarehouseName(warehouses, storageLocation.WarehouseId),

			LocationName = storageLocation?.Name ?? "Unknown storage location"
		};
	}

	public static string GetPurposeName(
		IReadOnlyDictionary<long, Purpose> purposes,
		long purposeId)
	{
		return purposes.TryGetValue(
			purposeId,
			out var purpose)
			? purpose.Name
			: "Unknown purpose";
	}

	public static string GetLocationName(
		IReadOnlyDictionary<long, StorageLocation> locations,
		long locationId)
	{
		return locations.TryGetValue(
			locationId,
			out var location)
			? location.Name
			: "Unknown storage location";
	}

	public static string GetWarehouseName(
		IReadOnlyDictionary<long, Warehouse> warehouses,
		long warehouseId) =>
		warehouses.TryGetValue(warehouseId, out var warehouse)
			? warehouse.Name
			: "Unknown warehouse";
}

internal sealed class InventoryMovementContext
{
	public long InventoryId { get; init; }

	public long ItemId { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string PurposeName { get; init; } = string.Empty;

	public string WarehouseName { get; init; } = string.Empty;

	public string LocationName { get; init; } = string.Empty;
}
