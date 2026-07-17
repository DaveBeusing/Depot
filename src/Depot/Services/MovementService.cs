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
	private readonly StorageLocationRepository _storageLocationRepository;
	private readonly WarehouseRepository _warehouseRepository;
	private readonly ReasonCodeRepository _reasonCodeRepository;
	private readonly StockMovementRepository _stockMovementRepository;
	private readonly AuditService _auditService;

	public MovementService(
		ItemRepository itemRepository,
		InventoryRepository inventoryRepository,
		PurposeRepository purposeRepository,
		StorageLocationRepository storageLocationRepository,
		WarehouseRepository warehouseRepository,
		ReasonCodeRepository reasonCodeRepository,
		StockMovementRepository stockMovementRepository,
		AuditService auditService)
	{
		_itemRepository = itemRepository;
		_inventoryRepository = inventoryRepository;
		_purposeRepository = purposeRepository;
		_storageLocationRepository = storageLocationRepository;
		_warehouseRepository = warehouseRepository;
		_reasonCodeRepository = reasonCodeRepository;
		_stockMovementRepository = stockMovementRepository;
		_auditService = auditService;
	}

	public Task<IReadOnlyList<InventoryLookupItem>> SearchAvailableInventoriesAsync(
		string? searchText,
		int count,
		CancellationToken cancellationToken) =>
		_inventoryRepository.SearchLookupAsync(searchText, count, cancellationToken);

	public Task<PageResult<MovementOverviewItem>> SearchAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken) =>
		_stockMovementRepository.SearchOverviewPageAsync(
			searchText,
			pageNumber,
			pageSize,
			cancellationToken);

	public Task<MovementOverviewItem> AddPurchaseAsync(
		long inventoryId,
		int quantity,
		decimal unitPrice,
		long? reasonCodeId,
		string? reference,
		string? notes,
		CancellationToken cancellationToken)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		if (unitPrice <= 0) throw new ArgumentException("Unit price must be greater than zero.", nameof(unitPrice));
		return CreateAsync(
			inventoryId,
			StockMovementType.Purchase,
			quantity,
			unitPrice,
			reasonCodeId,
			reference,
			notes,
			cancellationToken);
	}

	public Task<MovementOverviewItem> AddWithdrawalAsync(
		long inventoryId,
		int quantity,
		long? reasonCodeId,
		string? reference,
		string? notes,
		CancellationToken cancellationToken)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		return CreateAsync(
			inventoryId,
			StockMovementType.Withdrawal,
			-quantity,
			null,
			reasonCodeId,
			reference,
			notes,
			cancellationToken);
	}

	public Task<MovementOverviewItem> AddCorrectionAsync(
		long inventoryId,
		int quantityDelta,
		long? reasonCodeId,
		string? reference,
		string? notes,
		CancellationToken cancellationToken)
	{
		if (quantityDelta == 0) throw new ArgumentException("Correction quantity cannot be zero.", nameof(quantityDelta));
		return CreateAsync(
			inventoryId,
			StockMovementType.Correction,
			quantityDelta,
			null,
			reasonCodeId,
			reference,
			notes,
			cancellationToken);
	}

	public Task<MovementOverviewItem> AddOpeningBalanceAsync(
		long inventoryId,
		int quantity,
		decimal unitPrice,
		string? notes,
		CancellationToken cancellationToken)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		return CreateAsync(
			inventoryId,
			StockMovementType.OpeningBalance,
			quantity,
			unitPrice,
			null,
			"IMPORT",
			notes,
			cancellationToken);
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
			_storageLocationRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var warehouses = _warehouseRepository.GetAll().ToDictionary(x => x.Id);

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

					WarehouseName = locations.TryGetValue(inventory.StorageLocationId, out var storageLocation)
						? InventoryContextResolver.GetWarehouseName(warehouses, storageLocation.WarehouseId)
						: "Unknown warehouse",

					LocationName =
						InventoryContextResolver.GetLocationName(
							locations,
							inventory.StorageLocationId)
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
			_storageLocationRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		var warehouses = _warehouseRepository.GetAll().ToDictionary(x => x.Id);
		var reasonCodes = _reasonCodeRepository.GetAll().ToDictionary(x => x.Id);

		foreach (var movement in _stockMovementRepository.Search(searchText))
		{
			var context =
				InventoryContextResolver.ResolveMovement(
					movement,
					items,
					inventoriesById,
					purposes,
					locations,
					warehouses);

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

					WarehouseName = context.WarehouseName,

					LocationName =
						context.LocationName,

					ReasonCodeName = movement.ReasonCodeId is long reasonCodeId &&
						reasonCodes.TryGetValue(reasonCodeId, out var reasonCode)
							? reasonCode.Name
							: null,

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
		long? reasonCodeId,
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
			reasonCodeId,
			reference,
			notes);
	}

	public void AddWithdrawal(
		long inventoryId,
		int quantity,
		long? reasonCodeId,
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
			reasonCodeId,
			reference,
			notes);
	}

	public void AddCorrection(
		long inventoryId,
		int quantityDelta,
		long? reasonCodeId,
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
			reasonCodeId,
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
			null,
			"IMPORT",
			notes);
	}

	private void Create(
		long inventoryId,
		StockMovementType movementType,
		int quantity,
		decimal? unitPrice,
		long? reasonCodeId,
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

		ValidateReasonCode(reasonCodeId);

		var movement =
			new StockMovement
			{
				InventoryId =
					inventory.Id,

				ReasonCodeId = reasonCodeId,

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

		var auditEntry = _auditService.CreateCreatedEntry(0, movement);
		movement.Id = _stockMovementRepository.CreateAtomic(movement, auditEntry);
	}

	private async Task<MovementOverviewItem> CreateAsync(
		long inventoryId,
		StockMovementType movementType,
		int quantity,
		decimal? unitPrice,
		long? reasonCodeId,
		string? reference,
		string? notes,
		CancellationToken cancellationToken)
	{
		if (inventoryId <= 0) throw new ArgumentException("Inventory id is required.", nameof(inventoryId));
		var inventory = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken)
			?? throw new InvalidOperationException($"Inventory with id '{inventoryId}' was not found.");
		if (await _itemRepository.GetByIdAsync(inventory.ItemId, cancellationToken) is null)
			throw new InvalidOperationException($"Item with id '{inventory.ItemId}' was not found.");
		await ValidateReasonCodeAsync(reasonCodeId, cancellationToken);
		var movement = new StockMovement
		{
			InventoryId = inventory.Id,
			ReasonCodeId = reasonCodeId,
			MovementType = movementType,
			TimestampUtc = DateTime.UtcNow,
			Quantity = quantity,
			UnitPrice = unitPrice,
			Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
			Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
		};
		var auditEntry = _auditService.CreateCreatedEntry(0, movement);
		movement.Id = await _stockMovementRepository.CreateAtomicAsync(
			movement,
			auditEntry,
			cancellationToken);
		return await _stockMovementRepository.GetOverviewByIdAsync(movement.Id, cancellationToken)
			?? throw new InvalidOperationException($"Movement with id '{movement.Id}' was not found.");
	}

	private void ValidateReasonCode(long? reasonCodeId)
	{
		if (reasonCodeId is null) return;
		var reasonCode = _reasonCodeRepository.GetById(reasonCodeId.Value)
			?? throw new InvalidOperationException($"Reason code with id '{reasonCodeId}' was not found.");
		if (!reasonCode.IsActive) throw new InvalidOperationException("The selected reason code is inactive.");
	}

	private async Task ValidateReasonCodeAsync(long? reasonCodeId, CancellationToken cancellationToken)
	{
		if (reasonCodeId is null) return;
		var reasonCode = await _reasonCodeRepository.GetByIdAsync(reasonCodeId.Value, cancellationToken)
			?? throw new InvalidOperationException($"Reason code with id '{reasonCodeId}' was not found.");
		if (!reasonCode.IsActive) throw new InvalidOperationException("The selected reason code is inactive.");
	}

}
