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

	public CustomerReturnService(IDatabaseTransactionRunner transactions, CustomerReturnRepository returns, ShipmentRepository shipments, StockMovementRepository movements, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_returns = returns;
		_shipments = shipments;
		_movements = movements;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
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
			var shipment = await _shipments.GetByIdAsync(transaction, shipmentId, cancellationToken) ?? throw new InvalidOperationException("Shipment was not found.");
			if (shipment.Status != ShipmentStatus.Posted) throw new InvalidOperationException("Only a posted shipment can be returned.");
			var lines = new List<CustomerReturnLine>();
			foreach (var line in shipment.Lines)
			{
				var returned = Convert.ToInt32(await transaction.Session.ExecuteScalarAsync("SELECT COALESCE(SUM(crl.Quantity),0) FROM CustomerReturnLines crl INNER JOIN CustomerReturns cr ON cr.Id=crl.CustomerReturnId WHERE crl.ShipmentLineId=$LineId AND cr.Status=$Posted;", cancellationToken, new DatabaseParameter("$LineId", line.Id), new DatabaseParameter("$Posted", (int)CustomerReturnStatus.Posted)) ?? 0);
				var remaining = line.Quantity - returned;
				if (remaining > 0) lines.Add(new CustomerReturnLine { ShipmentLineId = line.Id, InventoryId = line.InventoryId, ItemId = line.ItemId, PartNumber = line.PartNumber, Description = line.Description, Quantity = remaining });
			}
			if (lines.Count == 0) throw new InvalidOperationException("All quantities from this shipment have already been returned.");
			var value = new CustomerReturn { ShipmentId = shipment.Id, SalesOrderId = shipment.SalesOrderId, CustomerId = shipment.CustomerId, ReturnDate = DateTime.Today, Reason = reason.Trim(), CreatedByUserId = user.Id, Lines = lines };
			await _returns.CreateAsync(transaction, value, cancellationToken);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(value.Id, value), cancellationToken);
			return await _returns.GetByIdAsync(transaction, value.Id, cancellationToken) ?? value;
		}, token);
	}

	public async Task<CustomerReturn> PostAsync(long id, long version, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomerReturnsPost);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
		{
			var before = await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Customer return was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("customer return");
			if (before.Status != CustomerReturnStatus.Draft) throw new InvalidOperationException("Only a draft customer return can be posted.");
			var postedAt = DateTime.UtcNow;
			foreach (var line in before.Lines)
			{
				var movement = new StockMovement { InventoryId = line.InventoryId, MovementType = StockMovementType.CustomerReturn, TimestampUtc = postedAt, Quantity = line.Quantity, Reference = $"Customer return {before.ReturnNumber}", Notes = before.Reason };
				movement.Id = await _movements.CreateAsync(transaction, movement, cancellationToken);
			}
			if (!await _returns.PostAsync(transaction, id, version, user.Id, postedAt, cancellationToken)) throw new ConcurrencyConflictException("customer return");
			var after = await _returns.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Customer return could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), cancellationToken);
			return after;
		}, token);
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for customer returns.");
}
