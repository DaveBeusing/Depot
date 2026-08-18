// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ShipmentService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ShipmentRepository _shipments;
	private readonly SalesOrderRepository _orders;
	private readonly InventoryReservationRepository _reservations;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _movements;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;

	public ShipmentService(IDatabaseTransactionRunner transactions, ShipmentRepository shipments, SalesOrderRepository orders, InventoryReservationRepository reservations, InventoryRepository inventories, StockMovementRepository movements, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization, NotificationService notifications)
	{
		_transactions = transactions;
		_shipments = shipments;
		_orders = orders;
		_reservations = reservations;
		_inventories = inventories;
		_movements = movements;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_notifications = notifications;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.ShipmentsCreate);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.ShipmentsPost);

	public Task<PageResult<Shipment>> SearchAsync(string? searchText, ShipmentStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ShipmentsView);
		return _shipments.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);
	}

	public Task<Shipment?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ShipmentsView);
		return _shipments.GetByIdAsync(id, cancellationToken);
	}

	public async Task<Shipment> CreateAsync(long salesOrderId, IReadOnlyCollection<ShipmentLineRequest> requests, string? carrier = null, string? trackingNumber = null, string? notes = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ShipmentsCreate);
		if (requests.Count == 0 || requests.Any(request => request.Quantity <= 0)) throw new InvalidOperationException("A shipment requires at least one positive quantity.");
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var order = await _orders.GetByIdAsync(transaction, salesOrderId, token) ?? throw new InvalidOperationException("Sales order was not found.");
			if (order.Status is not (SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped)) throw new InvalidOperationException("Only a released sales order can be shipped.");
			var activeReservations = await _reservations.ListActiveByOrderAsync(transaction, salesOrderId, token);
			var reservationById = activeReservations.ToDictionary(value => value.Id);
			if (requests.Select(value => value.InventoryReservationId).Distinct().Count() != requests.Count) throw new InvalidOperationException("A reservation can only occur once per shipment.");
			var lines = new List<ShipmentLine>();
			foreach (var request in requests)
			{
				if (!reservationById.TryGetValue(request.InventoryReservationId, out var reservation)) throw new InvalidOperationException("The selected reservation is no longer active.");
				if (request.Quantity > reservation.Quantity) throw new InvalidOperationException("Shipment quantity exceeds the reserved quantity.");
				var orderLine = order.Lines.Single(value => value.Id == reservation.SalesOrderLineId);
				lines.Add(new ShipmentLine { SalesOrderLineId = orderLine.Id, InventoryReservationId = reservation.Id, InventoryId = reservation.InventoryId, ItemId = orderLine.ItemId, PartNumber = orderLine.PartNumber, Description = orderLine.Description, Quantity = request.Quantity });
			}
			var customerAddress = order.CustomerId > 0
				? await transaction.Session.QuerySingleOrDefaultAsync("SELECT ShippingAddress FROM Customers WHERE Id=$Id;", r => new AddressRow(r.IsDBNull(0) ? null : r.GetString(0)), token, new DatabaseParameter("$Id", order.CustomerId))
				: null;
			var shipment = new Shipment { SalesOrderId = order.Id, SalesOrderNumber = order.OrderNumber, CustomerId = order.CustomerId, CustomerName = order.CustomerName, ShipmentDate = DateTime.Today, Status = ShipmentStatus.Draft, Carrier = Normalize(carrier), TrackingNumber = Normalize(trackingNumber), ShippingAddress = customerAddress?.Value, Notes = Normalize(notes), CreatedByUserId = user.Id, Lines = lines };
			await _shipments.CreateAsync(transaction, shipment, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(shipment.Id, shipment), token);
			return await _shipments.GetByIdAsync(transaction, shipment.Id, token) ?? shipment;
		}, cancellationToken);
	}

	public async Task<Shipment> PostAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ShipmentsPost);
		var user = RequireUser();
		var result = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _shipments.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Shipment was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("shipment");
			if (before.Status != ShipmentStatus.Draft) throw new InvalidOperationException("Only a draft shipment can be posted.");
			if (before.Lines.Count == 0) throw new InvalidOperationException("A shipment requires at least one line.");
			var order = await _orders.GetByIdAsync(transaction, before.SalesOrderId, token) ?? throw new InvalidOperationException("Sales order was not found.");
			if (order.Status is not (SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped)) throw new InvalidOperationException("The sales order is not available for shipping.");
			var inventoryIds = before.Lines.Select(line => line.InventoryId).Distinct().OrderBy(value => value).ToArray();
			await _inventories.GetByIdsForUpdateAsync(transaction, inventoryIds, token);
			var current = (await _movements.GetCurrentQuantitiesAsync(transaction, inventoryIds, token)).ToDictionary(value => value.InventoryId, value => value.Quantity);
			foreach (var group in before.Lines.GroupBy(line => line.InventoryId)) if (current.GetValueOrDefault(group.Key) < group.Sum(line => line.Quantity)) throw new InsufficientStockException();
			var postedAt = DateTime.UtcNow;
			foreach (var line in before.Lines.OrderBy(value => value.Id))
			{
				var orderLine = order.Lines.Single(value => value.Id == line.SalesOrderLineId);
				if (orderLine.ShippedQuantity + line.Quantity > orderLine.Quantity) throw new InvalidOperationException("Shipment quantity exceeds the sales-order quantity.");
				var movement = new StockMovement { InventoryId = line.InventoryId, MovementType = StockMovementType.SalesShipment, TimestampUtc = postedAt, Quantity = -line.Quantity, Reference = $"Shipment {before.ShipmentNumber}", Notes = before.TrackingNumber is null ? before.Notes : $"Tracking {before.TrackingNumber}{(string.IsNullOrWhiteSpace(before.Notes) ? string.Empty : $" · {before.Notes}")}" };
				movement.Id = await _movements.CreateAsync(transaction, movement, token);
				await _reservations.ConsumeAsync(transaction, line.InventoryReservationId, line.Quantity, user.Id, token);
				await _orders.UpdateLineQuantitiesAsync(transaction, orderLine.Id, Math.Max(0, orderLine.ReservedQuantity - line.Quantity), orderLine.ShippedQuantity + line.Quantity, orderLine.InvoicedQuantity, token);
			}
			if (!await _shipments.PostAsync(transaction, id, version, user.Id, postedAt, token)) throw new ConcurrencyConflictException("shipment");
			var reloadedOrder = await _orders.GetByIdAsync(transaction, order.Id, token) ?? throw new InvalidOperationException("Sales order could not be reloaded.");
			var targetStatus = reloadedOrder.Lines.All(line => line.ShippedQuantity >= line.Quantity) ? SalesOrderStatus.Shipped : SalesOrderStatus.PartiallyShipped;
			var afterOrder = SalesOrderService.Copy(reloadedOrder); afterOrder.Status = targetStatus;
			if (reloadedOrder.Status != targetStatus)
			{
				if (!await _orders.SetStatusAsync(transaction, afterOrder, reloadedOrder.Version, order.Status, token)) throw new ConcurrencyConflictException("sales order");
				afterOrder.Version = reloadedOrder.Version + 1;
				await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(order.Id, order, afterOrder), token);
			}
			var after = await _shipments.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Shipment could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
		await _notifications.NotifyUsersAsync(new(NotificationType.Workflow, NotificationSeverity.Success, $"Shipment {result.ShipmentNumber} posted", $"Shipment {result.ShipmentNumber} for sales order {result.SalesOrderNumber} was posted.", NotificationSourceTypes.Shipment, result.Id, result.ShipmentNumber, user.Id), [user.Id], cancellationToken);
		return result;
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for shipping.");
	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private sealed record AddressRow(string? Value);
}
