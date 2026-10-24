// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class CustomerReturnService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly CustomerReturnRepository _returns;
	private readonly ShipmentRepository _shipments;
	private readonly StockMovementRepository _movements;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;
	private readonly ItemTraceabilityService? _traceability;

	public CustomerReturnService(IDatabaseTransactionRunner transactions, CustomerReturnRepository returns, ShipmentRepository shipments, StockMovementRepository movements, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization, NotificationService notifications, ItemTraceabilityService? traceability = null)
	{
		_transactions = transactions;
		_returns = returns;
		_shipments = shipments;
		_movements = movements;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_notifications = notifications;
		_traceability = traceability;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.CustomerReturnsCreate);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.CustomerReturnsPost);
	public Task<PageResult<CustomerReturn>> SearchAsync(string? searchText, CustomerReturnStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomerReturnsView);
		return _returns.SearchAsync(searchText, status, pageNumber, pageSize, token);
	}
	public Task<CustomerReturn?> GetByIdAsync(long id, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomerReturnsView);
		return _returns.GetByIdAsync(id, token);
	}

	public async Task<CustomerReturn> CreateFromShipmentAsync(long shipmentId, string reason, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomerReturnsCreate);
		if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A return reason is required.", nameof(reason));
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
		{
			var shipment = await _shipments.GetByIdForUpdateAsync(transaction, shipmentId, cancellationToken) ?? throw new InvalidOperationException("Shipment was not found.");
			if (shipment.Status != ShipmentStatus.Posted || shipment.ReversedAtUtc is not null) throw new InvalidOperationException("Only an unreversed posted shipment can be returned.");
			var postedQuantities = await _returns.GetPostedQuantitiesAsync(transaction, shipmentId, cancellationToken);
			var lines = new List<CustomerReturnLine>();
			foreach (var line in shipment.Lines)
			{
				var returned = postedQuantities.GetValueOrDefault(line.Id);
				var remaining = line.Quantity - returned;
				if (remaining > 0) lines.Add(new CustomerReturnLine { ShipmentLineId = line.Id, InventoryId = line.InventoryId, ItemId = line.ItemId, PartNumber = line.PartNumber, Description = line.Description, Quantity = checked((int)remaining) });
			}
			if (lines.Count == 0) throw new InvalidOperationException("All quantities from this shipment have already been returned.");
			var value = new CustomerReturn { ShipmentId = shipment.Id, SalesOrderId = shipment.SalesOrderId, CustomerId = shipment.CustomerId, ReturnDate = DateTime.Today, Reason = reason.Trim(), CreatedByUserId = user.Id, Lines = lines };
			await _returns.CreateAsync(transaction, value, cancellationToken);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(value.Id, value), cancellationToken);
			return await _returns.GetByIdAsync(transaction, value.Id, cancellationToken) ?? value;
		}, token);
	}

	public Task<CustomerReturn> PostAsync(long id, long version, CancellationToken token = default) =>
		PostAsync(id, version, new Dictionary<long, IReadOnlyList<TrackingAllocationInput>>(), token);

	public async Task<CustomerReturn> PostAsync(long id, long version, IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> trackingByLineId, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomerReturnsPost);
		ArgumentNullException.ThrowIfNull(trackingByLineId);
		var user = RequireUser();
		// Resolve only the source identity before the transaction; acquire its lock before reading
		// the mutable return so competing posts and reversal always use the same lock order.
		var shipmentId = await _returns.GetShipmentIdAsync(id, token) ?? throw new InvalidOperationException("Customer return was not found.");
		var result = await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
		{
			var shipment = await _shipments.GetByIdForUpdateAsync(transaction, shipmentId, cancellationToken) ?? throw new InvalidOperationException("Shipment was not found.");
			var before = await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Customer return was not found.");
			if (before.ShipmentId != shipment.Id || before.SalesOrderId != shipment.SalesOrderId || before.CustomerId != shipment.CustomerId) throw new InvalidOperationException("The customer return does not match its source shipment.");
			if (before.Version != version) throw new ConcurrencyConflictException("customer return");
			if (before.Status != CustomerReturnStatus.Draft) throw new InvalidOperationException("Only a draft customer return can be posted.");
			if (shipment.Status != ShipmentStatus.Posted || shipment.ReversedAtUtc is not null) throw new InvalidOperationException("Only an unreversed posted shipment can be returned.");
			if (before.Lines.Count == 0) throw new InvalidOperationException("A customer return requires at least one line.");
			var validLineIds = before.Lines.Select(line => line.Id).ToHashSet();
			if (trackingByLineId.Keys.Any(lineId => !validLineIds.Contains(lineId))) throw new InvalidOperationException("Tracking data references a line outside this customer return.");
			var shipmentLines = shipment.Lines.ToDictionary(line => line.Id);
			var postedQuantities = await _returns.GetPostedQuantitiesAsync(transaction, shipment.Id, cancellationToken);
			foreach (var group in before.Lines.GroupBy(line => line.ShipmentLineId))
			{
				if (!shipmentLines.TryGetValue(group.Key, out var source) || group.Any(line => line.InventoryId != source.InventoryId || line.Quantity <= 0)) throw new InvalidOperationException("A customer-return line must reference its shipment inventory and a positive quantity.");
				if (group.Sum(line => (long)line.Quantity) > source.Quantity - postedQuantities.GetValueOrDefault(source.Id)) throw new InvalidOperationException("Customer-return quantity exceeds the remaining shipped quantity.");
			}
			var postedAt = DateTime.UtcNow;
			foreach (var line in before.Lines.OrderBy(line => line.Id))
			{
				if (_traceability is not null)
				{
					var policy = await _traceability.GetPolicyAsync(transaction, line.InventoryId, cancellationToken);
					ItemTraceabilityService.EnsurePhysicalStockItem(policy, "customer return");
				}
				var movement = new StockMovement { InventoryId = line.InventoryId, MovementType = StockMovementType.CustomerReturn, TimestampUtc = postedAt, Quantity = line.Quantity, Reference = $"Customer return {before.ReturnNumber}", Notes = before.Reason };
				movement.Id = await _movements.CreateAsync(transaction, movement, cancellationToken);
				if (_traceability is not null) await _traceability.AttachMovementAsync(transaction, movement, trackingByLineId.GetValueOrDefault(line.Id) ?? [], cancellationToken);
			}
			if (!await _returns.PostAsync(transaction, id, version, user.Id, postedAt, cancellationToken)) throw new ConcurrencyConflictException("customer return");
			var after = await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Customer return could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), cancellationToken);
			return after;
		}, token);

		var request = new NotificationRequest(NotificationType.Workflow, NotificationSeverity.Information, $"Customer return {result.ReturnNumber} posted", $"Customer return {result.ReturnNumber} was posted and inventory was returned to stock.", NotificationSourceTypes.CustomerReturn, result.Id, result.ReturnNumber, user.Id);
		await _notifications.NotifyPermissionHoldersAsync(request, ApplicationPermission.SalesOrdersView, [user.Id], token);
		await _notifications.NotifyPermissionHoldersAsync(request, ApplicationPermission.CreditNotesView, [user.Id], token);
		return result;
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for customer returns.");
}
