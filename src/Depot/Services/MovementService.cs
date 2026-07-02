// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class MovementService
{
	private readonly ItemRepository _itemRepository;
	private readonly InventoryRepository _inventoryRepository;
	private readonly PurposeRepository _purposeRepository;
	private readonly LocationRepository _locationRepository;
	private readonly StockMovementRepository _stockMovementRepository;

	public MovementService(
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

	public IReadOnlyList<InventoryLookupItem> GetAvailableInventories()
	{
		var result =
			new List<InventoryLookupItem>();

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

			result.Add(
				new InventoryLookupItem
				{
					Id =
						inventory.Id,

					ItemId =
						item.Id,

					PartNumber =
						item.PartNumber,

					Description =
						item.Description,

					PurposeName =
						InventoryContextResolver.GetPurposeName(
							purposes,
							inventory.PurposeId),

					LocationName =
						InventoryContextResolver.GetLocationName(
							locations,
							inventory.LocationId)
				});
		}

		return result;
	}

	public IReadOnlyList<MovementOverviewItem> Search(
		string? searchText)
	{
		var result =
			new List<MovementOverviewItem>();

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

		foreach (var movement in _stockMovementRepository.Search(searchText))
		{
			var context =
				InventoryContextResolver.ResolveMovement(
					movement,
					items,
					inventoriesById,
					purposes,
					locations);

			if (context is null)
			{
				continue;
			}

			result.Add(
				new MovementOverviewItem
				{
					MovementId =
						movement.Id,

					TimestampUtc =
						movement.TimestampUtc,

					InventoryId =
						context.InventoryId,

					ItemId =
						context.ItemId,

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
						movement.Quantity,

					UnitPrice =
						movement.UnitPrice,

					Reference =
						movement.Reference,

					Notes =
						movement.Notes
				});
		}

		return result;
	}

	public void AddPurchase(
		long inventoryId,
		int quantity,
		decimal unitPrice,
		string? reference,
		string? notes)
	{
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

		Create(
			inventoryId,
			StockMovementType.Purchase,
			quantity,
			unitPrice,
			reference,
			notes);
	}

	public void AddWithdrawal(
		long inventoryId,
		int quantity,
		string? reference,
		string? notes)
	{
		if (quantity <= 0)
		{
			throw new ArgumentException(
				"Quantity must be greater than zero.",
				nameof(quantity));
		}

		Create(
			inventoryId,
			StockMovementType.Withdrawal,
			-quantity,
			null,
			reference,
			notes);
	}

	public void AddCorrection(
		long inventoryId,
		int quantityDelta,
		string? reference,
		string? notes)
	{
		if (quantityDelta == 0)
		{
			throw new ArgumentException(
				"Correction quantity cannot be zero.",
				nameof(quantityDelta));
		}

		Create(
			inventoryId,
			StockMovementType.Correction,
			quantityDelta,
			null,
			reference,
			notes);
	}

	public void AddOpeningBalance(
		long inventoryId,
		int quantity,
		decimal unitPrice,
		string? notes)
	{
		if (quantity <= 0)
		{
			throw new ArgumentException(
				"Quantity must be greater than zero.",
				nameof(quantity));
		}

		Create(
			inventoryId,
			StockMovementType.OpeningBalance,
			quantity,
			unitPrice,
			"IMPORT",
			notes);
	}

	private void Create(
		long inventoryId,
		StockMovementType movementType,
		int quantity,
		decimal? unitPrice,
		string? reference,
		string? notes)
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

		var item =
			_itemRepository.GetById(
				inventory.ItemId);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{inventory.ItemId}' was not found.");
		}

		var movement =
			new StockMovement
			{
				InventoryId =
					inventory.Id,

				MovementType =
					movementType,

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

}
