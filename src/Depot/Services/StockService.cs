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

	public void AddPurchase(
		long itemId,
		int quantity,
		decimal unitPrice,
		string? reference,
		string? notes)
	{
		if (itemId <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(itemId));
		}

		if (quantity <= 0)
		{
			throw new ArgumentException(
				"Quantity must be greater than zero.",
				nameof(quantity));
		}

		if (unitPrice <= 0)
		{
			throw new ArgumentException(
				"Unit price must be greater than zero.",
				nameof(unitPrice));
		}

		var item =
			_itemRepository.GetById(itemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{itemId}' was not found.");
		}

		var movement =
			new StockMovement
			{
				ItemId = itemId,

				MovementType =
					StockMovementType.Purchase,

				TimestampUtc =
					DateTime.UtcNow,

				Quantity =
					quantity,

				UnitPrice =
					unitPrice,

				Reference =
					string.IsNullOrWhiteSpace(reference)
						? null
						: reference.Trim(),

				Notes =
					string.IsNullOrWhiteSpace(notes)
						? null
						: notes.Trim()
			};

		_stockMovementRepository.Create(
			movement);
	}

	public void AddWithdrawal(
		long itemId,
		int quantity,
		string? reference,
		string? notes)
	{
		if (itemId <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(itemId));
		}

		if (quantity <= 0)
		{
			throw new ArgumentException(
				"Quantity must be greater than zero.",
				nameof(quantity));
		}

		var item =
			_itemRepository.GetById(itemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{itemId}' was not found.");
		}

		var currentStock =
			GetCurrentStock(itemId);

		if (currentStock < quantity)
		{
			throw new InvalidOperationException(
				$"Insufficient stock. Current stock is {currentStock}.");
		}

		var movement =
			new StockMovement
			{
				ItemId = itemId,

				MovementType =
					StockMovementType.Withdrawal,

				TimestampUtc =
					DateTime.UtcNow,

				Quantity =
					-quantity,

				UnitPrice =
					null,

				Reference =
					string.IsNullOrWhiteSpace(reference)
						? null
						: reference.Trim(),

				Notes =
					string.IsNullOrWhiteSpace(notes)
						? null
						: notes.Trim()
			};

		_stockMovementRepository.Create(
			movement);
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

	public InventorySummary GetInventorySummary(
		long itemId)
	{
		var currentStock =
			GetCurrentStock(itemId);

		var averageCost =
			GetAverageCost(itemId);

		return new InventorySummary
		{
			ItemId = itemId,

			CurrentStock =
				currentStock,

			AverageCost =
				averageCost,

			InventoryValue =
				currentStock * averageCost
		};
	}

	public void AddCorrection(
		long itemId,
		int quantityDelta,
		string? reference,
		string? notes)
	{
		if (itemId <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(itemId));
		}

		if (quantityDelta == 0)
		{
			throw new ArgumentException(
				"Correction quantity cannot be zero.",
				nameof(quantityDelta));
		}

		var item =
			_itemRepository.GetById(itemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{itemId}' was not found.");
		}

		var movement =
			new StockMovement
			{
				ItemId = itemId,

				MovementType =
					StockMovementType.Correction,

				TimestampUtc =
					DateTime.UtcNow,

				Quantity =
					quantityDelta,

				UnitPrice =
					null,

				Reference =
					string.IsNullOrWhiteSpace(reference)
						? null
						: reference.Trim(),

				Notes =
					string.IsNullOrWhiteSpace(notes)
						? null
						: notes.Trim()
			};

		_stockMovementRepository.Create(
			movement);
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
					ItemId = item.Id,
					PartNumber = item.PartNumber,
					Description = item.Description,
					Manufacturer = item.Manufacturer,
					Category = item.Category,
					CurrentStock = summary.CurrentStock,
					AverageCost = summary.AverageCost,
					InventoryValue = summary.InventoryValue
				});
		}

		return result;
	}

	public InventoryDetails GetInventoryDetails(long itemId)
	{
		var item =
			_itemRepository.GetById(itemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{itemId}' was not found.");
		}

		var summary =
			GetInventorySummary(itemId);

		var movements =
			_stockMovementRepository
				.GetByItemId(itemId)
				.OrderByDescending(
					x => x.TimestampUtc)
				.Take(20)
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

	public void AddOpeningBalance(
		long itemId,
		int quantity,
		decimal unitPrice,
		string? notes)
	{
		if (itemId <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(itemId));
		}

		if (quantity <= 0)
		{
			throw new ArgumentException(
				"Quantity must be greater than zero.",
				nameof(quantity));
		}

		var item =
			_itemRepository.GetById(
				itemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{itemId}' was not found.");
		}

		var movement =
			new StockMovement
			{
				ItemId = itemId,

				MovementType =
					StockMovementType.OpeningBalance,

				TimestampUtc =
					DateTime.UtcNow,

				Quantity =
					quantity,

				UnitPrice =
					unitPrice,

				Reference =
					"IMPORT",

				Notes =
					notes
			};

		_stockMovementRepository.Create(
			movement);
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
					ItemId = item.Id,
					PartNumber = item.PartNumber,
					Description = item.Description,
					Manufacturer = item.Manufacturer,
					Category = item.Category,
					CurrentStock = summary.CurrentStock,
					AverageCost = summary.AverageCost,
					InventoryValue = summary.InventoryValue
				});
		}

		return result;
	}

}