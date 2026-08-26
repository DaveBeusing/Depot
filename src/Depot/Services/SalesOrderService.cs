// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesOrderService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SalesOrderRepository _orders;
	private readonly CustomerRepository _customers;
	private readonly ItemRepository _items;
	private readonly InventoryRepository _inventories;
	private readonly InventoryReservationRepository _reservations;
	private readonly StockMovementRepository _movements;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;
	private readonly ItemTraceabilityService? _traceability;

	public SalesOrderService(IDatabaseTransactionRunner transactions, SalesOrderRepository orders, CustomerRepository customers, ItemRepository items, InventoryRepository inventories, InventoryReservationRepository reservations, StockMovementRepository movements, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization, NotificationService notifications, ItemTraceabilityService? traceability = null)
	{
		_transactions = transactions;
		_orders = orders;
		_customers = customers;
		_items = items;
		_inventories = inventories;
		_reservations = reservations;
		_movements = movements;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_notifications = notifications;
		_traceability = traceability;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.SalesOrdersCreate);
	public bool CanEdit => _authorization.HasPermission(ApplicationPermission.SalesOrdersEdit);
	public bool CanSubmit => _authorization.HasPermission(ApplicationPermission.SalesOrdersSubmit);
	public bool CanApprove => _authorization.HasPermission(ApplicationPermission.SalesOrdersApprove);
	public bool CanRelease => _authorization.HasPermission(ApplicationPermission.SalesOrdersRelease);
	public Task<PageResult<SalesOrder>> SearchAsync(string? searchText, SalesOrderStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) { _authorization.RequirePermission(ApplicationPermission.SalesOrdersView); return _orders.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken); }
	public Task<SalesOrder?> GetByIdAsync(long id, CancellationToken cancellationToken = default) { _authorization.RequirePermission(ApplicationPermission.SalesOrdersView); return _orders.GetByIdAsync(id, cancellationToken); }
	public Task<IReadOnlyList<InventoryReservation>> GetReservationsAsync(long orderId, CancellationToken cancellationToken = default) { _authorization.RequirePermission(ApplicationPermission.SalesOrdersView); return _reservations.ListByOrderAsync(orderId, cancellationToken); }
	public Task<IReadOnlyList<SalesInventoryAvailability>> SearchAvailabilityAsync(long itemId, string? searchText = null, CancellationToken cancellationToken = default) { _authorization.RequirePermission(ApplicationPermission.SalesOrdersEdit); return _reservations.SearchAvailabilityAsync(itemId, searchText, cancellationToken); }

	public async Task<SalesOrder> SaveDraftAsync(SalesOrder order, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(order.Id == 0 ? ApplicationPermission.SalesOrdersCreate : ApplicationPermission.SalesOrdersEdit);
		if (order.Id != 0 && order.Status != SalesOrderStatus.Draft) throw new InvalidOperationException("Only draft sales orders can be edited.");
		await ValidateContentAsync(order, cancellationToken);
		var user = RequireUser();
		if (order.Id == 0) order.CreatedByUserId = user.Id;
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = order.Id == 0 ? null : await _orders.GetByIdAsync(transaction, order.Id, token);
			var saved = await _orders.SaveDraftAsync(transaction, order, token);
			await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(saved.Id, saved) : _audit.CreateUpdatedEntry(saved.Id, before, saved), token);
			return saved;
		}, cancellationToken);
	}

	public Task<SalesOrder> SubmitAsync(long id, long version, CancellationToken cancellationToken = default) => ChangeStatusAsync(ApplicationPermission.SalesOrdersSubmit, id, version, SalesOrderStatus.Draft, SalesOrderStatus.PendingApproval, order => { var user = RequireUser(); order.SubmittedByUserId = user.Id; order.SubmittedAtUtc = DateTime.UtcNow; order.ApprovalDecisionByUserId = null; order.ApprovalDecisionAtUtc = null; order.ApprovalComment = null; }, cancellationToken);

	public async Task<SalesOrder> ApproveAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesOrdersApprove);
		var user = RequireUser();
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Sales order was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("sales order");
		if (before.Status != SalesOrderStatus.PendingApproval) throw new InvalidOperationException("Only a pending sales order can be approved.");
		if (before.CreatedByUserId == user.Id && !user.IsAdministrator) throw new InvalidOperationException("A sales order cannot be approved by its creator.");
		comment = Normalize(comment);
		return await ChangeStatusCoreAsync(before, version, SalesOrderStatus.PendingApproval, SalesOrderStatus.Approved, order => { order.ApprovalDecisionByUserId = user.Id; order.ApprovalDecisionAtUtc = DateTime.UtcNow; order.ApprovalComment = comment; }, cancellationToken);
	}

	public async Task<SalesOrder> RejectAsync(long id, long version, string? comment = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesOrdersApprove);
		var user = RequireUser();
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Sales order was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("sales order");
		if (before.Status != SalesOrderStatus.PendingApproval) throw new InvalidOperationException("Only a pending sales order can be rejected.");
		if (before.CreatedByUserId == user.Id && !user.IsAdministrator) throw new InvalidOperationException("A sales order cannot be rejected by its creator.");
		return await ChangeStatusCoreAsync(before, version, SalesOrderStatus.PendingApproval, SalesOrderStatus.Rejected, order => { order.ApprovalDecisionByUserId = user.Id; order.ApprovalDecisionAtUtc = DateTime.UtcNow; order.ApprovalComment = Normalize(comment); }, cancellationToken);
	}

	public Task<SalesOrder> ReopenRejectedAsync(long id, long version, CancellationToken cancellationToken = default) => ChangeStatusAsync(ApplicationPermission.SalesOrdersEdit, id, version, SalesOrderStatus.Rejected, SalesOrderStatus.Draft, order => { order.SubmittedByUserId = null; order.SubmittedAtUtc = null; order.ApprovalDecisionByUserId = null; order.ApprovalDecisionAtUtc = null; order.ApprovalComment = null; }, cancellationToken);

	public async Task<SalesOrder> SetReservationsAsync(long id, long version, IReadOnlyCollection<SalesReservationRequest> requests, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesOrdersEdit);
		if (requests.Count == 0) throw new InvalidOperationException("At least one inventory reservation is required.");
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _orders.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales order was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("sales order");
			if (before.Status is not (SalesOrderStatus.Approved or SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped)) throw new InvalidOperationException("Inventory can only be reserved for an approved or released sales order.");
			if (requests.Any(request => request.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(requests));
			if (requests.Any(request => before.Lines.All(line => line.Id != request.SalesOrderLineId))) throw new InvalidOperationException("A reservation references a line that does not belong to the sales order.");
			var inventoryIds = requests.Select(request => request.InventoryId).Distinct().OrderBy(value => value).ToArray();
			var inventoryRows = await _inventories.GetByIdsForUpdateAsync(transaction, inventoryIds, token);
			if (inventoryRows.Count != inventoryIds.Length || inventoryRows.Any(inventory => !inventory.IsActive)) throw new InvalidOperationException("Every reserved inventory must exist and be active.");
			var inventoryById = inventoryRows.ToDictionary(value => value.Id);
			foreach (var request in requests)
			{
				var line = before.Lines.Single(value => value.Id == request.SalesOrderLineId);
				if (inventoryById[request.InventoryId].ItemId != line.ItemId) throw new InvalidOperationException("A reservation inventory must contain the sales-order item.");
				if (_traceability is not null)
				{
					var policy = await _traceability.GetPolicyAsync(transaction, request.InventoryId, token);
					ItemTraceabilityService.EnsurePhysicalStockItem(policy, "sales reservation");
				}
			}
			await _reservations.ReleaseOrderAsync(transaction, id, user.Id, token);
			foreach (var line in before.Lines) await _orders.UpdateLineQuantitiesAsync(transaction, line.Id, 0, line.ShippedQuantity, line.InvoicedQuantity, token);
			var current = (await _movements.GetCurrentQuantitiesAsync(transaction, inventoryIds, token)).ToDictionary(value => value.InventoryId, value => value.Quantity);
			foreach (var inventoryGroup in requests.GroupBy(value => value.InventoryId))
			{
				var otherReserved = Convert.ToInt64(await transaction.Session.ExecuteScalarAsync("SELECT COALESCE(SUM(Quantity),0) FROM InventoryReservations WHERE InventoryId=$InventoryId AND Status=$Active AND SalesOrderLineId NOT IN (SELECT Id FROM SalesOrderLines WHERE SalesOrderId=$OrderId);", token, new DatabaseParameter("$InventoryId", inventoryGroup.Key), new DatabaseParameter("$Active", (int)InventoryReservationStatus.Active), new DatabaseParameter("$OrderId", id)) ?? 0);
				var requested = inventoryGroup.Sum(value => value.Quantity);
				if (current.GetValueOrDefault(inventoryGroup.Key) - otherReserved < requested) throw new InsufficientStockException();
			}
			foreach (var request in requests)
			{
				var reservation = new InventoryReservation { SalesOrderLineId = request.SalesOrderLineId, InventoryId = request.InventoryId, Quantity = request.Quantity, Status = InventoryReservationStatus.Active, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id };
				reservation.Id = await _reservations.CreateAsync(transaction, reservation, token);
			}
			foreach (var line in before.Lines)
			{
				var reserved = requests.Where(value => value.SalesOrderLineId == line.Id).Sum(value => value.Quantity);
				if (reserved > Math.Max(0, line.Quantity - line.ShippedQuantity)) throw new InvalidOperationException("Reserved quantity cannot exceed the unshipped sales-order quantity.");
				await _orders.UpdateLineQuantitiesAsync(transaction, line.Id, reserved, line.ShippedQuantity, line.InvoicedQuantity, token);
			}
			var after = await _orders.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales order could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<SalesOrder> ReleaseAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesOrdersRelease);
		var user = RequireUser();
		var order = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Sales order was not found.");
		if (order.Version != version) throw new ConcurrencyConflictException("sales order");
		if (order.Status != SalesOrderStatus.Approved) throw new InvalidOperationException("Only an approved sales order can be released.");
		var reservations = await _reservations.ListByOrderAsync(id, cancellationToken);
		var activeReserved = reservations.Where(value => value.Status == InventoryReservationStatus.Active).Sum(value => value.Quantity);
		if (activeReserved <= 0) throw new InvalidOperationException("At least one quantity must be reserved before release.");
		foreach (var line in order.Lines)
		{
			var reserved = reservations.Where(value => value.SalesOrderLineId == line.Id && value.Status == InventoryReservationStatus.Active).Sum(value => value.Quantity);
			if (reserved > Math.Max(0, line.Quantity - line.ShippedQuantity)) throw new InvalidOperationException($"Line {line.LineNumber} is over-reserved.");
		}
		var released = await ChangeStatusCoreAsync(order, version, SalesOrderStatus.Approved, SalesOrderStatus.Released, result => { result.ReleasedByUserId = user.Id; result.ReleasedAtUtc = DateTime.UtcNow; }, cancellationToken);
		var backordered = released.Lines.Sum(line => line.BackorderedQuantity);
		if (backordered > 0)
		{
			await _notifications.NotifyPermissionHoldersAsync(new NotificationRequest(NotificationType.Workflow, NotificationSeverity.Warning, $"Sales order {released.OrderNumber} has backorders", $"{backordered:N0} unit(s) on sales order {released.OrderNumber} are not reserved and remain backordered.", NotificationSourceTypes.SalesOrder, released.Id, released.OrderNumber, user.Id), ApplicationPermission.SalesOrdersView, [user.Id], cancellationToken);
		}
		return released;
	}

	public async Task<SalesOrder> CancelAsync(long id, long version, string reason, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesOrdersCancel);
		var user = RequireUser();
		var normalizedReason = Normalize(reason) ?? throw new ArgumentException("A cancellation reason is required.", nameof(reason));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _orders.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales order was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("sales order");
			if (before.Status is SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped or SalesOrderStatus.Completed or SalesOrderStatus.Cancelled) throw new InvalidOperationException("This sales order can no longer be cancelled.");
			if (before.Lines.Any(line => line.ShippedQuantity > 0)) throw new InvalidOperationException("A sales order with shipments cannot be cancelled.");
			await _reservations.ReleaseOrderAsync(transaction, id, user.Id, token);
			foreach (var line in before.Lines) await _orders.UpdateLineQuantitiesAsync(transaction, line.Id, 0, line.ShippedQuantity, line.InvoicedQuantity, token);
			var after = Copy(before); after.Status = SalesOrderStatus.Cancelled; after.CancelledByUserId = user.Id; after.CancelledAtUtc = DateTime.UtcNow; after.CancelReason = normalizedReason;
			if (!await _orders.SetStatusAsync(transaction, after, version, before.Status, token)) throw new ConcurrencyConflictException("sales order");
			after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	private async Task<SalesOrder> ChangeStatusAsync(ApplicationPermission permission, long id, long version, SalesOrderStatus expected, SalesOrderStatus target, Action<SalesOrder>? apply, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(permission);
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Sales order was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("sales order");
		if (before.Status != expected) throw new InvalidOperationException($"The sales order must be in {expected} status.");
		if (target == SalesOrderStatus.PendingApproval) await ValidateContentAsync(before, cancellationToken);
		return await ChangeStatusCoreAsync(before, version, expected, target, apply, cancellationToken);
	}

	private async Task<SalesOrder> ChangeStatusCoreAsync(SalesOrder before, long version, SalesOrderStatus expected, SalesOrderStatus target, Action<SalesOrder>? apply, CancellationToken cancellationToken)
	{
		var after = Copy(before); after.Status = target; apply?.Invoke(after);
		var saved = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _orders.SetStatusAsync(transaction, after, version, expected, token)) throw new ConcurrencyConflictException("sales order");
			after.Version = version + 1;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(before.Id, before, after), token);
			return after;
		}, cancellationToken);
		await NotifyStatusAsync(saved, cancellationToken);
		return saved;
	}

	private async Task NotifyStatusAsync(SalesOrder order, CancellationToken cancellationToken)
	{
		var userId = _authorization.CurrentUser?.Id;
		if (order.Status == SalesOrderStatus.PendingApproval)
			await _notifications.NotifyPermissionHoldersAsync(new(NotificationType.Workflow, NotificationSeverity.Information, $"Sales order {order.OrderNumber} requires approval", $"Sales order {order.OrderNumber} for {order.CustomerName} requires approval.", NotificationSourceTypes.SalesOrderApproval, order.Id, order.OrderNumber, userId), ApplicationPermission.SalesOrdersApprove, order.CreatedByUserId is null ? null : [order.CreatedByUserId.Value], cancellationToken);
		else if (order.Status is SalesOrderStatus.Approved or SalesOrderStatus.Rejected)
		{
			var recipients = new long?[] { order.CreatedByUserId, order.SubmittedByUserId }.Where(value => value is > 0).Select(value => value!.Value).Distinct().ToArray();
			await _notifications.NotifyUsersAsync(new(NotificationType.Workflow, order.Status == SalesOrderStatus.Approved ? NotificationSeverity.Success : NotificationSeverity.Warning, $"Sales order {order.OrderNumber} {order.Status.ToString().ToLowerInvariant()}", $"Sales order {order.OrderNumber} was {order.Status.ToString().ToLowerInvariant()}.", NotificationSourceTypes.SalesOrder, order.Id, order.OrderNumber, userId), recipients, cancellationToken);
		}
	}

	private async Task ValidateContentAsync(SalesOrder order, CancellationToken cancellationToken)
	{
		if (order.CustomerId <= 0) throw new InvalidOperationException("A customer is required.");
		var customer = await _customers.GetByIdAsync(order.CustomerId, cancellationToken) ?? throw new InvalidOperationException("Customer was not found.");
		if (!customer.IsActive) throw new InvalidOperationException("The customer is inactive.");
		order.CustomerName = customer.Name;
		order.BillingAddress = Normalize(order.BillingAddress) ?? customer.Addresses.FirstOrDefault(a => a.IsActive && a.IsDefault && a.Type == CustomerAddressType.Billing)?.Address ?? customer.BillingAddress;
		order.ShippingAddress = Normalize(order.ShippingAddress) ?? customer.Addresses.FirstOrDefault(a => a.IsActive && a.IsDefault && a.Type == CustomerAddressType.Shipping)?.Address ?? customer.ShippingAddress;
		order.Currency = string.IsNullOrWhiteSpace(order.Currency) ? customer.Currency : order.Currency.Trim().ToUpperInvariant();
		order.CustomerReference = Normalize(order.CustomerReference); order.Notes = Normalize(order.Notes);
		if (order.Lines.Count == 0) throw new InvalidOperationException("A sales order requires at least one line.");
		if (order.Lines.Any(line => line.ItemId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0 || line.DiscountPercent is < 0 or > 100 || line.TaxRate is < 0 or > 100)) throw new InvalidOperationException("Sales-order lines contain invalid quantities or pricing.");
		var items = await _items.GetByIdsAsync(order.Lines.Select(line => line.ItemId), cancellationToken);
		if (items.Count != order.Lines.Select(line => line.ItemId).Distinct().Count() || items.Any(item => !item.IsActive)) throw new InvalidOperationException("Every sales-order item must exist and be active.");
		var byId = items.ToDictionary(item => item.Id);
		foreach (var line in order.Lines)
		{
			var item = byId[line.ItemId];
			ItemOperationalPolicy.EnsureSellable(item);
			line.PartNumber = item.PartNumber;
			line.Description = item.Description;
		}
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for sales operations.");
	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	internal static SalesOrder Copy(SalesOrder source) => new() { Id=source.Id,OrderNumber=source.OrderNumber,CustomerId=source.CustomerId,CustomerName=source.CustomerName,BillingAddress=source.BillingAddress,ShippingAddress=source.ShippingAddress,OrderDate=source.OrderDate,RequestedDeliveryDate=source.RequestedDeliveryDate,Currency=source.Currency,CustomerReference=source.CustomerReference,Notes=source.Notes,Status=source.Status,CreatedByUserId=source.CreatedByUserId,SubmittedByUserId=source.SubmittedByUserId,SubmittedAtUtc=source.SubmittedAtUtc,ApprovalDecisionByUserId=source.ApprovalDecisionByUserId,ApprovalDecisionAtUtc=source.ApprovalDecisionAtUtc,ApprovalComment=source.ApprovalComment,ReleasedByUserId=source.ReleasedByUserId,ReleasedAtUtc=source.ReleasedAtUtc,CancelledByUserId=source.CancelledByUserId,CancelledAtUtc=source.CancelledAtUtc,CancelReason=source.CancelReason,Version=source.Version,Lines=source.Lines };
}
