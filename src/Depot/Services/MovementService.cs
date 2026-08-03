// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class MovementService
{
	private readonly ItemRepository _items;
	private readonly InventoryRepository _inventories;
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly StockMovementRepository _movements;
	private readonly AuditService _audit;
	private readonly StockMovementReversalService _reversals;

	public MovementService(ItemRepository items, InventoryRepository inventories, ReasonCodeRepository reasonCodes, StockMovementRepository movements, AuditService audit, StockMovementReversalService reversals)
	{
		_items = items;
		_inventories = inventories;
		_reasonCodes = reasonCodes;
		_movements = movements;
		_audit = audit;
		_reversals = reversals;
	}

	public async Task<MovementOverviewItem> ReverseWithdrawalAsync(long movementId, long reasonCodeId, string reversalReason, CancellationToken cancellationToken)
	{
		_audit.RequirePermission(ApplicationPermission.StockMovementsReverse);
		var reversal = await _reversals.ReverseWithdrawalAsync(movementId, reasonCodeId, reversalReason, cancellationToken);
		return await _movements.GetOverviewByIdAsync(reversal.Id, cancellationToken)
			?? throw new InvalidOperationException("The reversal movement could not be loaded.");
	}

	public Task<IReadOnlyList<InventoryLookupItem>> SearchAvailableInventoriesAsync(string? searchText, int count, CancellationToken cancellationToken) =>
		_inventories.SearchLookupAsync(searchText, count, cancellationToken);

	public Task<PageResult<MovementOverviewItem>> SearchAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_audit.RequirePermission(ApplicationPermission.StockMovementsView);
		return _movements.SearchOverviewPageAsync(searchText, pageNumber, pageSize, cancellationToken);
	}

	public Task<MovementOverviewItem> AddPurchaseAsync(long inventoryId, int quantity, decimal unitPrice, long? reasonCodeId, string? reference, string? notes, CancellationToken cancellationToken)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		if (unitPrice <= 0) throw new ArgumentException("Unit price must be greater than zero.", nameof(unitPrice));
		return CreateAsync(inventoryId, StockMovementType.Purchase, quantity, unitPrice, reasonCodeId, reference, notes, cancellationToken);
	}

	public Task<MovementOverviewItem> AddWithdrawalAsync(long inventoryId, int quantity, long? reasonCodeId, string? reference, string? notes, CancellationToken cancellationToken)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		return CreateAsync(inventoryId, StockMovementType.Withdrawal, -quantity, null, reasonCodeId, reference, notes, cancellationToken);
	}

	public Task<MovementOverviewItem> AddCorrectionAsync(long inventoryId, int quantityDelta, long? reasonCodeId, string? reference, string? notes, CancellationToken cancellationToken)
	{
		if (quantityDelta == 0) throw new ArgumentException("Correction quantity cannot be zero.", nameof(quantityDelta));
		return CreateAsync(inventoryId, StockMovementType.Correction, quantityDelta, null, reasonCodeId, reference, notes, cancellationToken);
	}

	public Task<MovementOverviewItem> AddOpeningBalanceAsync(long inventoryId, int quantity, decimal unitPrice, string? notes, CancellationToken cancellationToken)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		return CreateAsync(inventoryId, StockMovementType.OpeningBalance, quantity, unitPrice, null, "IMPORT", notes, cancellationToken);
	}

	public void AddPurchase(long inventoryId, int quantity, decimal unitPrice, long? reasonCodeId, string? reference, string? notes)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		if (unitPrice <= 0) throw new ArgumentException("Unit price must be greater than zero.", nameof(unitPrice));
		Create(inventoryId, StockMovementType.Purchase, quantity, unitPrice, reasonCodeId, reference, notes);
	}

	public void AddWithdrawal(long inventoryId, int quantity, long? reasonCodeId, string? reference, string? notes)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		Create(inventoryId, StockMovementType.Withdrawal, -quantity, null, reasonCodeId, reference, notes);
	}

	public void AddCorrection(long inventoryId, int quantityDelta, long? reasonCodeId, string? reference, string? notes)
	{
		if (quantityDelta == 0) throw new ArgumentException("Correction quantity cannot be zero.", nameof(quantityDelta));
		Create(inventoryId, StockMovementType.Correction, quantityDelta, null, reasonCodeId, reference, notes);
	}

	public void AddOpeningBalance(long inventoryId, int quantity, decimal unitPrice, string? notes)
	{
		if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
		Create(inventoryId, StockMovementType.OpeningBalance, quantity, unitPrice, null, "IMPORT", notes);
	}

	private void Create(long inventoryId, StockMovementType movementType, int quantity, decimal? unitPrice, long? reasonCodeId, string? reference, string? notes)
	{
		_audit.RequirePermission(ApplicationPermission.StockMovementsPost);
		if (inventoryId <= 0) throw new ArgumentException("Inventory id is required.", nameof(inventoryId));
		var inventory = _inventories.GetById(inventoryId) ?? throw new InvalidOperationException($"Inventory with id '{inventoryId}' was not found.");
		if (_items.GetById(inventory.ItemId) is null) throw new InvalidOperationException($"Item with id '{inventory.ItemId}' was not found.");
		ValidateReasonCode(reasonCodeId);
		var movement = CreateMovement(inventory.Id, movementType, quantity, unitPrice, reasonCodeId, reference, notes);
		movement.Id = _movements.CreateAtomic(movement, _audit.CreateCreatedEntry(0, movement));
	}

	private async Task<MovementOverviewItem> CreateAsync(long inventoryId, StockMovementType movementType, int quantity, decimal? unitPrice, long? reasonCodeId, string? reference, string? notes, CancellationToken cancellationToken)
	{
		_audit.RequirePermission(ApplicationPermission.StockMovementsPost);
		if (inventoryId <= 0) throw new ArgumentException("Inventory id is required.", nameof(inventoryId));
		var inventory = await _inventories.GetByIdAsync(inventoryId, cancellationToken) ?? throw new InvalidOperationException($"Inventory with id '{inventoryId}' was not found.");
		if (await _items.GetByIdAsync(inventory.ItemId, cancellationToken) is null) throw new InvalidOperationException($"Item with id '{inventory.ItemId}' was not found.");
		await ValidateReasonCodeAsync(reasonCodeId, cancellationToken);
		var movement = CreateMovement(inventory.Id, movementType, quantity, unitPrice, reasonCodeId, reference, notes);
		movement.Id = await _movements.CreateAtomicAsync(movement, _audit.CreateCreatedEntry(0, movement), cancellationToken);
		return await _movements.GetOverviewByIdAsync(movement.Id, cancellationToken) ?? throw new InvalidOperationException($"Movement with id '{movement.Id}' was not found.");
	}

	private static StockMovement CreateMovement(long inventoryId, StockMovementType movementType, int quantity, decimal? unitPrice, long? reasonCodeId, string? reference, string? notes) => new()
	{
		InventoryId = inventoryId,
		ReasonCodeId = reasonCodeId,
		MovementType = movementType,
		TimestampUtc = DateTime.UtcNow,
		Quantity = quantity,
		UnitPrice = unitPrice,
		Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
		Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
	};

	private void ValidateReasonCode(long? reasonCodeId)
	{
		if (reasonCodeId is null) return;
		var reasonCode = _reasonCodes.GetById(reasonCodeId.Value) ?? throw new InvalidOperationException($"Reason code with id '{reasonCodeId}' was not found.");
		if (!reasonCode.IsActive) throw new InvalidOperationException("The selected reason code is inactive.");
	}

	private async Task ValidateReasonCodeAsync(long? reasonCodeId, CancellationToken cancellationToken)
	{
		if (reasonCodeId is null) return;
		var reasonCode = await _reasonCodes.GetByIdAsync(reasonCodeId.Value, cancellationToken) ?? throw new InvalidOperationException($"Reason code with id '{reasonCodeId}' was not found.");
		if (!reasonCode.IsActive) throw new InvalidOperationException("The selected reason code is inactive.");
	}
}
