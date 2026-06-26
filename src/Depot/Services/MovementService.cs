// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class MovementService
{
	private readonly ItemRepository _itemRepository;
	private readonly StockMovementRepository _stockMovementRepository;

	public MovementService(
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
				ItemId =
					itemId,

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
				ItemId =
					itemId,

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
				ItemId =
					itemId,

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
}