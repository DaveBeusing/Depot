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
			_stockMovementRepository.GetByItemId(itemId);

		return movements.Sum(
			x => x.Quantity);
	}

	public decimal GetAverageCost(
		long itemId)
	{
		var movements =
			_stockMovementRepository.GetByItemId(itemId);

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

		return totalValue / totalQuantity;
	}

	public decimal GetInventoryValue(
		long itemId)
	{
		var currentStock =
			GetCurrentStock(itemId);

		var averageCost =
			GetAverageCost(itemId);

		return currentStock * averageCost;
	}
}