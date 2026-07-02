// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class StockService
{
	private readonly ItemRepository _itemRepository;
	private readonly InventoryRepository _inventoryRepository;
	private readonly PurposeRepository _purposeRepository;
	private readonly LocationRepository _locationRepository;
	private readonly StockMovementRepository _stockMovementRepository;

	public StockService(
		ItemRepository itemRepository,
		InventoryRepository inventoryRepository,
		PurposeRepository purposeRepository,
		LocationRepository locationRepository,
		StockMovementRepository stockMovementRepository)
	{
		_itemRepository = itemRepository;
		_inventoryRepository = inventoryRepository;
		_purposeRepository = purposeRepository;
		_locationRepository = locationRepository;
		_stockMovementRepository = stockMovementRepository;
	}

	public int GetCurrentStock(
		long inventoryId)
	{
		var inventory =
			GetInventory(
				inventoryId);

		return GetMovementsForInventory(
				inventory)
			.Sum(
				x => x.Quantity);
	}

	public decimal GetAverageCost(
		long inventoryId)
	{
		var inventory =
			GetInventory(
				inventoryId);

		var purchases =
			GetMovementsForInventory(
					inventory)
				.Where(
					x =>
						x.Quantity > 0 &&
						x.UnitPrice.HasValue)
				.ToList();

		if (purchases.Count == 0)
		{
			return 0m;
		}

		var totalQuantity =
			purchases.Sum(
				x => x.Quantity);

		if (totalQuantity == 0)
		{
			return 0m;
		}

		var totalValue =
			purchases.Sum(
				x =>
					x.Quantity *
					x.UnitPrice!.Value);

		return
			totalValue /
			totalQuantity;
	}

	public decimal GetInventoryValue(
		long inventoryId)
	{
		var currentStock =
			GetCurrentStock(
				inventoryId);

		var averageCost =
			GetAverageCost(
				inventoryId);

		return
			currentStock *
			averageCost;
	}

	public InventorySummary GetInventorySummary(
		long inventoryId)
	{
		var inventory =
			GetInventory(
				inventoryId);

		return GetInventorySummary(
			inventory);
	}

	public IReadOnlyList<InventoryOverviewItem> GetInventoryOverview()
	{
		var result =
			new List<InventoryOverviewItem>();

		var items =
			_itemRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var purposes =
			_purposeRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var locations =
			_locationRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		foreach (var inventory in _inventoryRepository.GetAll())
		{
			if (!items.TryGetValue(
				inventory.ItemId,
				out var item))
			{
				continue;
			}

			var summary =
				GetInventorySummary(
					inventory);

			result.Add(
				new InventoryOverviewItem
				{
					InventoryId =
						inventory.Id,

					ItemId =
						item.Id,

					PartNumber =
						item.PartNumber,

					Description =
						item.Description,

					Manufacturer =
						item.Manufacturer,

					Category =
						item.Category,

					PurposeName =
						GetPurposeName(
							purposes,
							inventory.PurposeId),

					LocationName =
						GetLocationName(
							locations,
							inventory.LocationId),

					CurrentStock =
						summary.CurrentStock,

					AverageCost =
						summary.AverageCost,

					InventoryValue =
						summary.InventoryValue
				});
		}

		return result
			.OrderBy(
				x => x.PartNumber)
			.ThenBy(
				x => x.PurposeName)
			.ThenBy(
				x => x.LocationName)
			.ToList();
	}

	public IReadOnlyList<InventoryOverviewItem> SearchInventoryOverview(
		string? searchText)
	{
		var inventory =
			GetInventoryOverview();

		if (string.IsNullOrWhiteSpace(searchText))
		{
			return inventory;
		}

		var search =
			searchText.Trim();

		return inventory
			.Where(
				x =>
					Contains(
						x.PartNumber,
						search) ||
					Contains(
						x.Description,
						search) ||
					Contains(
						x.Manufacturer,
						search) ||
					Contains(
						x.Category,
						search) ||
					Contains(
						x.PurposeName,
						search) ||
					Contains(
						x.LocationName,
						search))
			.ToList();
	}

	public InventoryDetails GetInventoryDetails(
		long inventoryId)
	{
		var inventory =
			GetInventory(
				inventoryId);

		var item =
			_itemRepository.GetById(
				inventory.ItemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{inventory.ItemId}' was not found.");
		}

		var purposes =
			_purposeRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var locations =
			_locationRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var summary =
			GetInventorySummary(
				inventory);

		var movements =
			GetMovementsForInventory(
					inventory)
				.OrderByDescending(
					x => x.TimestampUtc)
				.Take(
					20)
				.ToList();

		return new InventoryDetails
		{
			InventoryId =
				inventory.Id,

			ItemId =
				item.Id,

			PartNumber =
				item.PartNumber,

			Description =
				item.Description,

			Manufacturer =
				item.Manufacturer,

			Category =
				item.Category,

			PurposeName =
				GetPurposeName(
					purposes,
					inventory.PurposeId),

			LocationName =
				GetLocationName(
					locations,
					inventory.LocationId),

			CurrentStock =
				summary.CurrentStock,

			AverageCost =
				summary.AverageCost,

			InventoryValue =
				summary.InventoryValue,

			RecentMovements =
				movements
		};
	}

	public DashboardSummary GetDashboardSummary()
	{
		var inventory =
			GetInventoryOverview();

		var totalItems =
			inventory
				.Select(
					x => x.ItemId)
				.Distinct()
				.Count();

		var totalStockQuantity =
			inventory.Sum(
				x => x.CurrentStock);

		var totalInventoryValue =
			inventory.Sum(
				x => x.InventoryValue);

		var totalMovements =
			_stockMovementRepository
				.GetAll()
				.Count;

		return new DashboardSummary
		{
			TotalItems =
				totalItems,

			TotalStockQuantity =
				totalStockQuantity,

			TotalInventoryValue =
				totalInventoryValue,

			TotalMovements =
				totalMovements
		};
	}

	public IReadOnlyList<DashboardRecentMovement> GetRecentMovements(
		int count = 10)
	{
		var result =
			new List<DashboardRecentMovement>();

		var items =
			_itemRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var inventories =
			_inventoryRepository.GetAll();

		var inventoriesById =
			inventories
				.ToDictionary(
					x => x.Id);

		var inventoriesByItem =
			inventories
				.GroupBy(
					x => x.ItemId)
				.ToDictionary(
					x => x.Key,
					x => x.ToList());

		var purposes =
			_purposeRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var locations =
			_locationRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		foreach (var movement in _stockMovementRepository.GetAll())
		{
			var context =
				ResolveMovementContext(
					movement,
					items,
					inventoriesById,
					inventoriesByItem,
					purposes,
					locations);

			if (context is null)
			{
				continue;
			}

			result.Add(
				new DashboardRecentMovement
				{
					TimestampUtc =
						movement.TimestampUtc,

					InventoryId =
						context.InventoryId,

					PartNumber =
						context.PartNumber,

					Description =
						context.Description,

					PurposeName =
						context.PurposeName,

					LocationName =
						context.LocationName,

					MovementType =
						movement.MovementType,

					Quantity =
						movement.Quantity
				});

			if (result.Count >= count)
			{
				break;
			}
		}

		return result;
	}

	private InventorySummary GetInventorySummary(
		Inventory inventory)
	{
		var movements =
			GetMovementsForInventory(
				inventory);

		var currentStock =
			movements.Sum(
				x => x.Quantity);

		var purchases =
			movements
				.Where(
					x =>
						x.Quantity > 0 &&
						x.UnitPrice.HasValue)
				.ToList();

		var averageCost =
			CalculateAverageCost(
				purchases);

		return new InventorySummary
		{
			InventoryId =
				inventory.Id,

			ItemId =
				inventory.ItemId,

			CurrentStock =
				currentStock,

			AverageCost =
				averageCost,

			InventoryValue =
				currentStock *
				averageCost
		};
	}

	private Inventory GetInventory(
		long inventoryId)
	{
		if (inventoryId <= 0)
		{
			throw new ArgumentException(
				"Inventory id is required.",
				nameof(inventoryId));
		}

		var inventory =
			_inventoryRepository.GetById(
				inventoryId);

		if (inventory is null)
		{
			throw new InvalidOperationException(
				$"Inventory with id '{inventoryId}' was not found.");
		}

		return inventory;
	}

	private IReadOnlyList<StockMovement> GetMovementsForInventory(
		Inventory inventory)
	{
		var inventoriesForItem =
			_inventoryRepository.GetByItem(
				inventory.ItemId);

		var canAssignLegacyMovements =
			inventoriesForItem.Count == 1 &&
			inventoriesForItem[0].Id == inventory.Id;

		return _stockMovementRepository
			.GetByItemId(
				inventory.ItemId)
			.Where(
				x =>
					x.InventoryId == inventory.Id ||
					(
						x.InventoryId is null &&
						canAssignLegacyMovements
					))
			.ToList();
	}

	private static decimal CalculateAverageCost(
		IReadOnlyList<StockMovement> purchases)
	{
		if (purchases.Count == 0)
		{
			return 0m;
		}

		var totalQuantity =
			purchases.Sum(
				x => x.Quantity);

		if (totalQuantity == 0)
		{
			return 0m;
		}

		var totalValue =
			purchases.Sum(
				x =>
					x.Quantity *
					x.UnitPrice!.Value);

		return
			totalValue /
			totalQuantity;
	}

	private static string GetPurposeName(
		IReadOnlyDictionary<long, Purpose> purposes,
		long purposeId)
	{
		return purposes.TryGetValue(
			purposeId,
			out var purpose)
			? purpose.Name
			: "Unknown purpose";
	}

	private static string GetLocationName(
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

	private static bool Contains(
		string? value,
		string searchText)
	{
		return
			!string.IsNullOrWhiteSpace(value) &&
			value.Contains(
				searchText,
				StringComparison.OrdinalIgnoreCase);
	}

	private static MovementContext? ResolveMovementContext(
		StockMovement movement,
		IReadOnlyDictionary<long, Item> items,
		IReadOnlyDictionary<long, Inventory> inventoriesById,
		IReadOnlyDictionary<long, List<Inventory>> inventoriesByItem,
		IReadOnlyDictionary<long, Purpose> purposes,
		IReadOnlyDictionary<long, Location> locations)
	{
		if (movement.InventoryId is not null &&
			inventoriesById.TryGetValue(
				movement.InventoryId.Value,
				out var inventory) &&
			items.TryGetValue(
				inventory.ItemId,
				out var inventoryItem))
		{
			return new MovementContext
			{
				InventoryId =
					inventory.Id,

				ItemId =
					inventoryItem.Id,

				PartNumber =
					inventoryItem.PartNumber,

				Description =
					inventoryItem.Description,

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

		if (!items.TryGetValue(
			movement.ItemId,
			out var item))
		{
			return null;
		}

		if (movement.InventoryId is null &&
			inventoriesByItem.TryGetValue(
				item.Id,
				out var itemInventories) &&
			itemInventories.Count == 1)
		{
			var legacyInventory =
				itemInventories[0];

			return new MovementContext
			{
				InventoryId =
					legacyInventory.Id,

				ItemId =
					item.Id,

				PartNumber =
					item.PartNumber,

				Description =
					item.Description,

				PurposeName =
					GetPurposeName(
						purposes,
						legacyInventory.PurposeId),

				LocationName =
					GetLocationName(
						locations,
						legacyInventory.LocationId)
			};
		}

		return new MovementContext
		{
			InventoryId =
				movement.InventoryId,

			ItemId =
				item.Id,

			PartNumber =
				item.PartNumber,

			Description =
				item.Description,

			PurposeName =
				"Unassigned",

			LocationName =
				"Unassigned"
		};
	}

	private sealed class MovementContext
	{
		public long? InventoryId { get; init; }

		public long ItemId { get; init; }

		public string PartNumber { get; init; } = string.Empty;

		public string Description { get; init; } = string.Empty;

		public string PurposeName { get; init; } = string.Empty;

		public string LocationName { get; init; } = string.Empty;
	}
}
