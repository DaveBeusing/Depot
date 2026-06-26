// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class StockService
{
	private readonly ItemRepository _itemRepository;
	private readonly StockMovementRepository _stockMovementRepository;

	public StockService(
		ItemRepository itemRepository,
		StockMovementRepository stockMovementRepository)
	{
		_itemRepository = itemRepository;
		_stockMovementRepository = stockMovementRepository;
	}

	public int GetCurrentStock(
		long itemId)
	{
		var movements =
			_stockMovementRepository.GetByItemId(
				itemId);

		return movements.Sum(
			x => x.Quantity);
	}

	public decimal GetAverageCost(
		long itemId)
	{
		var movements =
			_stockMovementRepository.GetByItemId(
				itemId);

		var purchases =
			movements
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
		long itemId)
	{
		var currentStock =
			GetCurrentStock(
				itemId);

		var averageCost =
			GetAverageCost(
				itemId);

		return
			currentStock *
			averageCost;
	}

	public InventorySummary GetInventorySummary(
		long itemId)
	{
		var currentStock =
			GetCurrentStock(
				itemId);

		var averageCost =
			GetAverageCost(
				itemId);

		return new InventorySummary
		{
			ItemId =
				itemId,

			CurrentStock =
				currentStock,

			AverageCost =
				averageCost,

			InventoryValue =
				currentStock *
				averageCost
		};
	}

	public IReadOnlyList<InventoryOverviewItem> GetInventoryOverview()
	{
		var result =
			new List<InventoryOverviewItem>();

		var items =
			_itemRepository.GetAll();

		foreach (var item in items)
		{
			var summary =
				GetInventorySummary(
					item.Id);

			result.Add(
				new InventoryOverviewItem
				{
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

					CurrentStock =
						summary.CurrentStock,

					AverageCost =
						summary.AverageCost,

					InventoryValue =
						summary.InventoryValue
				});
		}

		return result;
	}

	public IReadOnlyList<InventoryOverviewItem> SearchInventoryOverview(
		string? searchText)
	{
		var result =
			new List<InventoryOverviewItem>();

		var items =
			_itemRepository.SearchActive(
				searchText);

		foreach (var item in items)
		{
			var summary =
				GetInventorySummary(
					item.Id);

			result.Add(
				new InventoryOverviewItem
				{
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

					CurrentStock =
						summary.CurrentStock,

					AverageCost =
						summary.AverageCost,

					InventoryValue =
						summary.InventoryValue
				});
		}

		return result;
	}

	public InventoryDetails GetInventoryDetails(
		long itemId)
	{
		var item =
			_itemRepository.GetById(
				itemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{itemId}' was not found.");
		}

		var summary =
			GetInventorySummary(
				itemId);

		var movements =
			_stockMovementRepository
				.GetByItemId(
					itemId)
				.OrderByDescending(
					x => x.TimestampUtc)
				.Take(
					20)
				.ToList();

		return new InventoryDetails
		{
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
			inventory.Count;

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
		var items =
			_itemRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		return
			_stockMovementRepository
				.GetAll()
				.Take(
					count)
				.Where(
					x =>
						items.ContainsKey(
							x.ItemId))
				.Select(
					x =>
					{
						var item =
							items[x.ItemId];

						return new DashboardRecentMovement
						{
							TimestampUtc =
								x.TimestampUtc,

							PartNumber =
								item.PartNumber,

							Description =
								item.Description,

							MovementType =
								x.MovementType,

							Quantity =
								x.Quantity
						};
					})
				.ToList();
	}
}