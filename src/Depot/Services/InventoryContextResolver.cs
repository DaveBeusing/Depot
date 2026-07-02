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
		IReadOnlyDictionary<long, Location> locations)
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

			LocationName =
				GetLocationName(
					locations,
					inventory.LocationId)
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
		IReadOnlyDictionary<long, Location> locations,
		long? locationId)
	{
		if (locationId is null)
		{
			return "Unknown location";
		}

		return locations.TryGetValue(
			locationId.Value,
			out var location)
			? location.Name
			: "Unknown location";
	}
}

internal sealed class InventoryMovementContext
{
	public long InventoryId { get; init; }

	public long ItemId { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string PurposeName { get; init; } = string.Empty;

	public string LocationName { get; init; } = string.Empty;
}
