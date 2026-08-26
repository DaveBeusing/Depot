// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SupplierReturnService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SupplierReturnRepository _returns;
	private readonly PurchaseOrderRepository _orders;
	private readonly GoodsReceiptRepository _receipts;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _movements;
	private readonly ReasonCodeRepository _reasons;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly StockMovementReversalService _reversals;
	private readonly IAuthorizationService _authorization;
	private readonly ItemTraceabilityService? _traceability;

	public SupplierReturnService(IDatabaseTransactionRunner transactions, SupplierReturnRepository returns, PurchaseOrderRepository orders, GoodsReceiptRepository receipts, InventoryRepository inventories, StockMovementRepository movements, ReasonCodeRepository reasons, AuditRepository auditEntries, AuditService audit, StockMovementReversalService reversals, IAuthorizationService authorization, ItemTraceabilityService? traceability = null)
	{
		_transactions = transactions;
		_returns = returns;
		_orders = orders;
		_receipts = receipts;
		_inventories = inventories;
		_movements = movements;
		_reasons = reasons;
		_auditEntries = auditEntries;
		_audit = audit;
		_reversals = reversals;
		_authorization = authorization;
		_traceability = traceability;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.SupplierReturnsCreate);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.SupplierReturnsPost);

	public Task<PageResult<SupplierReturnOverviewItem>> SearchAsync(string? search, long? supplierId, SupplierReturnStatus? status, int page, int size, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierReturnsView);
		return _returns.SearchAsync(search, supplierId, status, page, size, cancellationToken);
	}

	public Task<SupplierReturn?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierReturnsView);
		return _returns.GetByIdAsync(id, cancellationToken);
	}

	public Task<SupplierReturnOverviewItem?> GetOverviewByIdAsync(long id, CancellationToken cancellationToken = default) => _returns.GetOverviewByIdAsync(id, cancellationToken);
	public Task<IReadOnlyList<SupplierReturnReceiptOption>> SearchReceiptOptionsAsync(string? search, long? supplierId, CancellationToken cancellationToken = default) => _returns.SearchReceiptOptionsAsync(search, supplierId, cancellationToken);
	public Task<IReadOnlyList<SupplierReturnableLine>> GetReturnableLinesAsync(long receiptId, CancellationToken cancellationToken = default) => _returns.ListReturnableLinesAsync(receiptId, cancellationToken);

	public async Task<IReadOnlyList<MovementOverviewItem>> GetMovementsAsync(long id, CancellationToken cancellationToken = default)
	{
		var value = await _returns.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The supplier return was not found.");
		return await _movements.ListByReferenceAsync(DocumentReference(value.ReturnNumber), cancellationToken);
	}

	public Task<SupplierReturn> SaveDraftAsync(SupplierReturn value, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(value.Id == 0 ? ApplicationPermission.SupplierReturnsCreate : ApplicationPermission.SupplierReturnsEdit);
		NormalizeAndValidate(value);
		var userId = RequireUser("save");
		return _transactions.ExecuteAsync((transaction, token) => SaveDraftAsync(transaction, value, userId, token), cancellationToken);
	}

	public Task<SupplierReturn> PostSupplierReturnAsync(long id, long version, CancellationToken cancellationToken = default) =>
		PostSupplierReturnAsync(id, version, EmptyTracking(), Guid.NewGuid(), cancellationToken);

	public Task<SupplierReturn> PostSupplierReturnAsync(long id, long version, Guid operationId, CancellationToken cancellationToken = default) =>
		PostSupplierReturnAsync(id, version, EmptyTracking(), operationId, cancellationToken);

	public Task<SupplierReturn> PostSupplierReturnAsync(long id, long version, IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> trackingByLineId, CancellationToken cancellationToken = default) =>
		PostSupplierReturnAsync(id, version, trackingByLineId, Guid.NewGuid(), cancellationToken);

	public Task<SupplierReturn> PostSupplierReturnAsync(long id, long version, IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> trackingByLineId, Guid operationId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierReturnsPost);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
		ArgumentNullException.ThrowIfNull(trackingByLineId);
		var userId = RequireUser("post");
		return _transactions.ExecuteAsync((transaction, token) => PostSupplierReturnAsync(transaction, id, version, userId, trackingByLineId, new(operationId, WorkflowOperationNames.PostSupplierReturn, id), token), cancellationToken);
	}

	public Task<SupplierReturn> CancelAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierReturnsEdit);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
		RequireUser("cancel");
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _returns.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("The supplier return was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("supplier return");
			if (before.Status != SupplierReturnStatus.Draft) throw new InvalidOperationException("Only a draft supplier return can be cancelled.");
			if (!await _returns.CancelAsync(transaction, id, version, token)) throw new ConcurrencyConflictException("supplier return");
			var after = Copy(before); after.Status = SupplierReturnStatus.Cancelled; after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<StockMovement>> ReverseAsync(long id, long version, long reasonCodeId, string reversalReason, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SupplierReturnsReverse);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
		var userId = _reversals.RequireUser();
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _returns.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("The supplier return was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("supplier return");
			if (before.Status != SupplierReturnStatus.Posted || before.IsReversed) throw new InvalidOperationException("Only a posted supplier return that has not been reversed can be reversed.");
			var originals = await _movements.ListOriginalsByReferenceAsync(transaction, DocumentReference(before.ReturnNumber), token);
			if (originals.Count != before.Lines.Count || originals.Any(movement => movement.MovementType != StockMovementType.SupplierReturn)) throw new InvalidOperationException("The supplier-return movements are incomplete or inconsistent.");
			var normalizedReason = Normalize(reversalReason) ?? throw new ArgumentException("A reversal reason is required.", nameof(reversalReason));
			var movements = await _reversals.CreateReversalsAsync(transaction, originals, reasonCodeId, normalizedReason, userId, token);
			var reversedAtUtc = DateTime.UtcNow;
			if (!await _returns.MarkReversedAsync(transaction, id, version, userId, reversedAtUtc, normalizedReason, token)) throw new ConcurrencyConflictException("supplier return");
			var after = Copy(before); after.ReversedByUserId = userId; after.ReversedAtUtc = reversedAtUtc; after.ReversalReason = normalizedReason; after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return movements;
		}, cancellationToken);
	}

	private async Task<SupplierReturn> SaveDraftAsync(DatabaseTransactionContext transaction, SupplierReturn value, long userId, CancellationToken cancellationToken)
	{
		SupplierReturn? before = null;
		if (value.Id == 0)
		{
			value.CreatedByUserId = userId;
			value.ReturnNumber = $"PENDING-{Guid.NewGuid():N}";
			await ValidateDocumentAsync(transaction, value, cancellationToken);
			value.Id = await _returns.CreateAsync(transaction, value, cancellationToken);
			value.ReturnNumber = $"SR-{value.Id:000000}";
			if (await _returns.UpdateNumberAsync(transaction, value.Id, value.ReturnNumber, cancellationToken) != 1) throw new ConcurrencyConflictException("supplier return number");
		}
		else
		{
			before = await _returns.GetByIdAsync(transaction, value.Id, cancellationToken) ?? throw new InvalidOperationException("The supplier return was not found.");
			if (before.Version != value.Version) throw new ConcurrencyConflictException("supplier return");
			if (before.Status != SupplierReturnStatus.Draft) throw new InvalidOperationException("Only draft supplier returns can be edited.");
			value.ReturnNumber = before.ReturnNumber;
			value.CreatedByUserId = before.CreatedByUserId;
			await ValidateDocumentAsync(transaction, value, cancellationToken);
			if (!await _returns.UpdateDraftAsync(transaction, value, cancellationToken)) throw new ConcurrencyConflictException("supplier return");
			value.Version++;
		}
		var existingLineIds = before?.Lines.Select(line => line.Id).ToHashSet() ?? [];
		var suppliedLineIds = value.Lines.Where(line => line.Id > 0).Select(line => line.Id).ToArray();
		if (suppliedLineIds.Distinct().Count() != suppliedLineIds.Length || suppliedLineIds.Any(id => !existingLineIds.Contains(id))) throw new InvalidOperationException("A supplier-return line does not belong to this return.");
		await _returns.DeleteLinesAsync(transaction, value.Id, existingLineIds.Except(suppliedLineIds).OrderBy(id => id).ToArray(), cancellationToken);
		foreach (var line in value.Lines)
		{
			line.SupplierReturnId = value.Id;
			if (line.Id == 0) line.Id = await _returns.CreateLineAsync(transaction, line, cancellationToken);
			else { if (!await _returns.UpdateLineAsync(transaction, line, cancellationToken)) throw new ConcurrencyConflictException("supplier return line"); line.Version++; }
		}
		await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(value.Id, value) : _audit.CreateUpdatedEntry(value.Id, before, value), cancellationToken);
		return value;
	}

	private async Task<SupplierReturn> PostSupplierReturnAsync(DatabaseTransactionContext transaction, long id, long version, long userId, IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> trackingByLineId, WorkflowOperation operation, CancellationToken cancellationToken)
	{
		if (await WorkflowOperationRepository.IsCompletedAsync(transaction.Session, operation, cancellationToken))
			return await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("The completed supplier return operation could not be reloaded.");
		var before = await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("The supplier return was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("supplier return");
		if (before.Status != SupplierReturnStatus.Draft) throw new InvalidOperationException("Only a draft supplier return can be posted.");
		NormalizeAndValidate(before);
		await ValidateDocumentAsync(transaction, before, cancellationToken);
		ValidateTrackingKeys(before.Lines.Select(line => line.Id), trackingByLineId);
		var requiredQuantities = before.Lines.GroupBy(line => line.InventoryId).ToDictionary(group => group.Key, group => group.Sum(line => (long)line.Quantity));
		var currentQuantities = (await _movements.GetCurrentQuantitiesAsync(transaction, requiredQuantities.Keys, cancellationToken)).ToDictionary(value => value.InventoryId, value => value.Quantity);
		if (requiredQuantities.Any(value => currentQuantities.GetValueOrDefault(value.Key) < value.Value)) throw new InsufficientStockException();
		var postedAtUtc = DateTime.UtcNow;
		foreach (var line in before.Lines.OrderBy(line => line.Id))
		{
			if (_traceability is not null)
			{
				var policy = await _traceability.GetPolicyAsync(transaction, line.InventoryId, cancellationToken);
				ItemTraceabilityService.EnsurePhysicalStockItem(policy, "supplier return");
			}
			var movement = new StockMovement { InventoryId = line.InventoryId, ReasonCodeId = line.ReasonCodeId, MovementType = StockMovementType.SupplierReturn, TimestampUtc = postedAtUtc, Quantity = -line.Quantity, UnitPrice = line.UnitCost, Reference = DocumentReference(before.ReturnNumber), Notes = $"Return to {before.SupplierName}" };
			movement.Id = await _movements.CreateAsync(transaction, movement, cancellationToken);
			if (_traceability is not null) await _traceability.AttachMovementAsync(transaction, movement, trackingByLineId.GetValueOrDefault(line.Id) ?? [], cancellationToken);
		}
		if (!await _returns.SetPostedAsync(transaction, id, version, userId, postedAtUtc, cancellationToken)) throw new ConcurrencyConflictException("supplier return");
		var after = Copy(before); after.Status = SupplierReturnStatus.Posted; after.PostedByUserId = userId; after.PostedAtUtc = postedAtUtc; after.Version++;
		await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), cancellationToken);
		await WorkflowOperationRepository.CompleteAsync(transaction.Session, operation, cancellationToken);
		return after;
	}

	private async Task ValidateDocumentAsync(DatabaseTransactionContext transaction, SupplierReturn value, CancellationToken cancellationToken)
	{
		var order = await _orders.GetForReceiptUpdateAsync(transaction, value.PurchaseOrderId, cancellationToken) ?? throw new InvalidOperationException("The purchase order was not found.");
		var receipt = await _receipts.GetByIdAsync(transaction, value.GoodsReceiptId, cancellationToken) ?? throw new InvalidOperationException("The goods receipt was not found.");
		if (receipt.IsReversed) throw new InvalidOperationException("A reversed goods receipt cannot be returned to the supplier.");
		if (receipt.PurchaseOrderId != order.Id) throw new InvalidOperationException("The goods receipt does not belong to the selected purchase order.");
		if (order.SupplierId != value.SupplierId) throw new InvalidOperationException("The supplier return does not match the purchase-order supplier.");
		value.SupplierName = order.SupplierName; value.PurchaseOrderNumber = order.OrderNumber; value.GoodsReceiptNumber = receipt.ReceiptNumber;
		var inventoryIds = value.Lines.Select(line => line.InventoryId).Distinct().OrderBy(id => id).ToArray();
		var inventories = await _inventories.GetByIdsForUpdateAsync(transaction, inventoryIds, cancellationToken);
		if (inventories.Count != inventoryIds.Length || inventories.Any(inventory => !inventory.IsActive)) throw new InvalidOperationException("Every supplier-return inventory must exist and be active.");
		var reasonCodeIds = value.Lines.Select(line => line.ReasonCodeId).Distinct().ToArray();
		var reasons = await _reasons.GetByIdsAsync(transaction, reasonCodeIds, cancellationToken);
		if (reasons.Count != reasonCodeIds.Length || reasons.Any(reason => !reason.IsActive)) throw new InvalidOperationException("Every supplier-return reason code must exist and be active.");
		var availableLines = (await _returns.ListReturnableLinesAsync(transaction, receipt.Id, cancellationToken)).ToDictionary(line => line.GoodsReceiptLineId);
		foreach (var line in value.Lines)
		{
			if (!availableLines.TryGetValue(line.GoodsReceiptLineId, out var source)) throw new InvalidOperationException("A return line does not belong to the selected goods receipt.");
			if (line.InventoryId != source.InventoryId || line.ItemId != source.ItemId) throw new InvalidOperationException("The return line inventory or item does not match the received line.");
			if (line.Quantity > source.ReturnableQuantity) throw new InvalidOperationException("The return quantity exceeds the net received quantity that has not already been returned.");
			line.UnitCost = source.UnitCost; line.PartNumber = source.PartNumber; line.ItemDescription = source.ItemDescription; line.InventoryDisplay = source.InventoryDisplay; line.ReceivedQuantity = source.ReceivedQuantity; line.AlreadyReturnedQuantity = source.AlreadyReturnedQuantity; line.AvailableStock = source.AvailableStock;
		}
	}

	private static void NormalizeAndValidate(SupplierReturn value)
	{
		if (value.Status != SupplierReturnStatus.Draft) throw new InvalidOperationException("Only draft supplier returns can be saved or posted.");
		if (value.SupplierId <= 0 || value.PurchaseOrderId <= 0 || value.GoodsReceiptId <= 0) throw new ArgumentException("Supplier, purchase order and goods receipt are required.");
		value.SupplierReference = Normalize(value.SupplierReference); value.Notes = Normalize(value.Notes);
		if (value.SupplierReference?.Length > 250 || value.Notes?.Length > 4000) throw new ArgumentException("Supplier-return text exceeds its maximum length.");
		if (value.Lines.Count == 0) throw new InvalidOperationException("A supplier return requires at least one line.");
		if (value.Lines.Any(line => line.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(value), "Every return quantity must be greater than zero.");
		if (value.Lines.Any(line => line.InventoryId <= 0 || line.ItemId <= 0 || line.ReasonCodeId <= 0 || line.GoodsReceiptLineId <= 0)) throw new InvalidOperationException("Every return line requires receipt, inventory, item and reason code references.");
		if (value.Lines.Select(line => line.GoodsReceiptLineId).Distinct().Count() != value.Lines.Count) throw new InvalidOperationException("A goods-receipt line can only occur once per supplier return.");
	}

	private static IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> EmptyTracking() => new Dictionary<long, IReadOnlyList<TrackingAllocationInput>>();
	private static void ValidateTrackingKeys(IEnumerable<long> validLineIds, IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> trackingByLineId)
	{
		var valid = validLineIds.ToHashSet();
		if (trackingByLineId.Keys.Any(id => !valid.Contains(id))) throw new InvalidOperationException("Tracking data references a line outside this supplier return.");
	}
	private long RequireUser(string operation) => _audit.CurrentUserId ?? throw new InvalidOperationException($"A signed-in user is required to {operation} a supplier return.");
	private static string DocumentReference(string number) => $"Supplier Return {number}";
	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private static SupplierReturn Copy(SupplierReturn value) => new() { Id = value.Id, ReturnNumber = value.ReturnNumber, SupplierId = value.SupplierId, SupplierName = value.SupplierName, ReturnDate = value.ReturnDate, Status = value.Status, PurchaseOrderId = value.PurchaseOrderId, PurchaseOrderNumber = value.PurchaseOrderNumber, GoodsReceiptId = value.GoodsReceiptId, GoodsReceiptNumber = value.GoodsReceiptNumber, SupplierReference = value.SupplierReference, Notes = value.Notes, CreatedByUserId = value.CreatedByUserId, PostedByUserId = value.PostedByUserId, PostedAtUtc = value.PostedAtUtc, ReversedByUserId = value.ReversedByUserId, ReversedAtUtc = value.ReversedAtUtc, ReversalReason = value.ReversalReason, Version = value.Version, Lines = value.Lines };
}
