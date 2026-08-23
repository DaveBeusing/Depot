// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesInvoiceService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SalesInvoiceRepository _invoices;
	private readonly ShipmentRepository _shipments;
	private readonly SalesOrderRepository _orders;
	private readonly CustomerRepository _customers;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;
	private readonly SalesCreditNoteService _creditNotes;

	public SalesInvoiceService(IDatabaseTransactionRunner transactions, SalesInvoiceRepository invoices, ShipmentRepository shipments, SalesOrderRepository orders, CustomerRepository customers, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization, NotificationService notifications, SalesCreditNoteService creditNotes)
	{
		_transactions = transactions;
		_invoices = invoices;
		_shipments = shipments;
		_orders = orders;
		_customers = customers;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_notifications = notifications;
		_creditNotes = creditNotes;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.SalesInvoicesCreate);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.SalesInvoicesPost);
	public bool CanCreateCreditNote => _creditNotes.CanCreate;
	public bool CanPostCreditNote => _creditNotes.CanPost;
	public Task<PageResult<SalesInvoice>> SearchAsync(string? searchText, SalesInvoiceStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) { _authorization.RequirePermission(ApplicationPermission.SalesInvoicesView); return _invoices.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken); }
	public Task<SalesInvoice?> GetByIdAsync(long id, CancellationToken cancellationToken = default) { _authorization.RequirePermission(ApplicationPermission.SalesInvoicesView); return _invoices.GetByIdAsync(id, cancellationToken); }
	public Task<PageResult<SalesCreditNote>> SearchCreditNotesAsync(string? searchText, SalesCreditNoteStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken token = default) => _creditNotes.SearchAsync(searchText, status, pageNumber, pageSize, token);
	public Task<SalesCreditNote> CreateCreditNoteAsync(long invoiceId, string reason, CancellationToken token = default) => _creditNotes.CreateFromInvoiceAsync(invoiceId, reason, token);
	public Task<SalesCreditNote> CreateCreditNoteAsync(long invoiceId, IReadOnlyCollection<SalesCreditRequest> requests, string reason, CancellationToken token = default) => _creditNotes.CreateFromInvoiceAsync(invoiceId, requests, reason, token);
	public Task<SalesCreditNote> PostCreditNoteAsync(long id, long version, CancellationToken token = default) => _creditNotes.PostAsync(id, version, token);

	public async Task<SalesInvoice> CreateFromShipmentAsync(long shipmentId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesInvoicesCreate);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (await _invoices.GetByShipmentIdAsync(transaction, shipmentId, token) is not null) throw new InvalidOperationException("This shipment already has a sales invoice.");
			var shipment = await _shipments.GetByIdAsync(transaction, shipmentId, token) ?? throw new InvalidOperationException("Shipment was not found.");
			if (shipment.Status != ShipmentStatus.Posted) throw new InvalidOperationException("Only a posted shipment can be invoiced.");
			var order = await _orders.GetByIdAsync(transaction, shipment.SalesOrderId, token) ?? throw new InvalidOperationException("Sales order was not found.");
			var customer = await _customers.GetByIdAsync(order.CustomerId, token) ?? throw new InvalidOperationException("Customer was not found.");
			var orderLines = order.Lines.ToDictionary(value => value.Id);
			var invoice = new SalesInvoice
			{
				CustomerId = customer.Id,
				CustomerName = order.CustomerName,
				SalesOrderId = order.Id,
				SalesOrderNumber = order.OrderNumber,
				ShipmentId = shipment.Id,
				ShipmentNumber = shipment.ShipmentNumber,
				InvoiceDate = DateTime.Today,
				DueDate = DateTime.Today.AddDays(customer.PaymentTermsDays),
				Currency = order.Currency,
				Status = SalesInvoiceStatus.Draft,
				CustomerReference = order.CustomerReference,
				BillingAddress = order.BillingAddress,
				CreatedByUserId = user.Id,
				Lines = shipment.Lines.Select((shipmentLine, index) =>
				{
					var source = orderLines[shipmentLine.SalesOrderLineId];
					return new SalesInvoiceLine { LineNumber = index + 1, SalesOrderLineId = source.Id, ShipmentLineId = shipmentLine.Id, PartNumber = source.PartNumber, Description = source.Description, Quantity = shipmentLine.Quantity, UnitPrice = source.UnitPrice, DiscountPercent = source.DiscountPercent, TaxRate = source.TaxRate };
				}).ToArray()
			};
			await _invoices.CreateAsync(transaction, invoice, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(invoice.Id, invoice), token);
			return await _invoices.GetByIdAsync(transaction, invoice.Id, token) ?? invoice;
		}, cancellationToken);
	}

	public async Task<SalesInvoice> CancelDraftAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesInvoicesCreate);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _invoices.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales invoice was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("sales invoice");
			if (before.Status != SalesInvoiceStatus.Draft) throw new InvalidOperationException("Only a draft invoice can be cancelled.");
			if (!await _invoices.CancelDraftAsync(transaction, id, version, token)) throw new ConcurrencyConflictException("sales invoice");
			var after = await _invoices.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales invoice could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<SalesInvoice> PostAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesInvoicesPost);
		var user = RequireUser();
		var result = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _invoices.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales invoice was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("sales invoice");
			if (before.Status != SalesInvoiceStatus.Draft) throw new InvalidOperationException("Only a draft sales invoice can be posted.");
			var order = await _orders.GetByIdAsync(transaction, before.SalesOrderId, token) ?? throw new InvalidOperationException("Sales order was not found.");
			foreach (var group in before.Lines.GroupBy(line => line.SalesOrderLineId))
			{
				var orderLine = order.Lines.Single(value => value.Id == group.Key);
				var quantity = group.Sum(value => value.Quantity);
				if (orderLine.InvoicedQuantity + quantity > orderLine.ShippedQuantity) throw new InvalidOperationException("Invoice quantity exceeds the shipped quantity.");
				await _orders.UpdateLineQuantitiesAsync(transaction, orderLine.Id, orderLine.ReservedQuantity, orderLine.ShippedQuantity, orderLine.InvoicedQuantity + quantity, token);
			}
			var postedAt = DateTime.UtcNow;
			if (!await _invoices.PostAsync(transaction, id, version, user.Id, postedAt, token)) throw new ConcurrencyConflictException("sales invoice");
			await DocumentIssuerSnapshotService.CaptureCurrentAsync(transaction, DocumentIssuerSnapshotType.SalesInvoice, id, postedAt, token);
			var reloadedOrder = await _orders.GetByIdAsync(transaction, order.Id, token) ?? throw new InvalidOperationException("Sales order could not be reloaded.");
			if (reloadedOrder.Status == SalesOrderStatus.Shipped && reloadedOrder.Lines.All(line => line.InvoicedQuantity >= line.ShippedQuantity && line.ShippedQuantity >= line.Quantity))
			{
				var completed = SalesOrderService.Copy(reloadedOrder); completed.Status = SalesOrderStatus.Completed;
				if (!await _orders.SetStatusAsync(transaction, completed, reloadedOrder.Version, SalesOrderStatus.Shipped, token)) throw new ConcurrencyConflictException("sales order");
				completed.Version = reloadedOrder.Version + 1;
				await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(order.Id, order, completed), token);
			}
			var after = await _invoices.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Sales invoice could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
		await _notifications.NotifyUsersAsync(new(NotificationType.Workflow, NotificationSeverity.Success, $"Invoice {result.InvoiceNumber} posted", $"Sales invoice {result.InvoiceNumber} for {result.CustomerName} was posted.", NotificationSourceTypes.SalesInvoice, result.Id, result.InvoiceNumber, user.Id), [user.Id], cancellationToken);
		return result;
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for sales invoicing.");
}
