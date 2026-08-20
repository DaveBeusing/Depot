// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesCreditNoteService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SalesCreditNoteRepository _creditNotes;
	private readonly SalesInvoiceRepository _invoices;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;

	public SalesCreditNoteService(IDatabaseTransactionRunner transactions, SalesCreditNoteRepository creditNotes, SalesInvoiceRepository invoices, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization, NotificationService notifications)
	{
		_transactions = transactions;
		_creditNotes = creditNotes;
		_invoices = invoices;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_notifications = notifications;
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.CreditNotesCreate);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.CreditNotesPost);

	public Task<PageResult<SalesCreditNote>> SearchAsync(string? searchText, SalesCreditNoteStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CreditNotesView);
		return _creditNotes.SearchAsync(searchText, status, pageNumber, pageSize, token);
	}

	public Task<SalesCreditNote?> GetByIdAsync(long id, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CreditNotesView);
		return _creditNotes.GetByIdAsync(id, token);
	}

	public async Task<SalesCreditNote> CreateFromInvoiceAsync(long invoiceId, string reason, CancellationToken token = default)
	{
		var invoice = await _invoices.GetByIdAsync(invoiceId, token) ?? throw new InvalidOperationException("Sales invoice was not found.");
		return await CreateFromInvoiceAsync(invoiceId, invoice.Lines.Select(line => new SalesCreditRequest(line.Id, line.Quantity)).ToArray(), reason, token);
	}

	public async Task<SalesCreditNote> CreateFromInvoiceAsync(long invoiceId, IReadOnlyCollection<SalesCreditRequest> requests, string reason, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CreditNotesCreate);
		if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A credit-note reason is required.", nameof(reason));
		if (requests.Count == 0 || requests.Any(request => request.Quantity <= 0)) throw new InvalidOperationException("A credit note requires at least one positive quantity.");
		if (requests.Select(request => request.SalesInvoiceLineId).Distinct().Count() != requests.Count) throw new InvalidOperationException("An invoice line can only occur once per credit note.");
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
		{
			var invoice = await _invoices.GetByIdAsync(transaction, invoiceId, cancellationToken) ?? throw new InvalidOperationException("Sales invoice was not found.");
			if (invoice.Status != SalesInvoiceStatus.Posted) throw new InvalidOperationException("Only a posted sales invoice can be credited.");
			var linesById = invoice.Lines.ToDictionary(line => line.Id);
			foreach (var request in requests)
			{
				if (!linesById.TryGetValue(request.SalesInvoiceLineId, out var invoiceLine)) throw new InvalidOperationException("A credit-note line does not belong to the selected invoice.");
				var existing = Convert.ToInt32(await transaction.Session.ExecuteScalarAsync(
					"SELECT COALESCE(SUM(cnl.Quantity),0) FROM SalesCreditNoteLines cnl INNER JOIN SalesCreditNotes cn ON cn.Id=cnl.SalesCreditNoteId WHERE cn.SalesInvoiceId=$InvoiceId AND cnl.SalesInvoiceLineId=$LineId;",
					cancellationToken,
					new DatabaseParameter("$InvoiceId", invoiceId),
					new DatabaseParameter("$LineId", request.SalesInvoiceLineId)) ?? 0);
				if (existing + request.Quantity > invoiceLine.Quantity) throw new InvalidOperationException("Credit quantity exceeds the remaining invoice quantity.");
			}

			var value = new SalesCreditNote
			{
				SalesInvoiceId = invoice.Id,
				CustomerId = invoice.CustomerId,
				CreditDate = DateTime.Today,
				Reason = reason.Trim(),
				CreatedByUserId = user.Id,
				Lines = requests.Select(request =>
				{
					var line = linesById[request.SalesInvoiceLineId];
					return new SalesCreditNoteLine { SalesInvoiceLineId = line.Id, Quantity = request.Quantity, UnitPrice = line.UnitPrice, DiscountPercent = line.DiscountPercent, TaxRate = line.TaxRate };
				}).ToArray()
			};
			await _creditNotes.CreateAsync(transaction, value, cancellationToken);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(value.Id, value), cancellationToken);
			return await _creditNotes.GetByIdAsync(transaction, value.Id, cancellationToken) ?? value;
		}, token);
	}

	public async Task<SalesCreditNote> PostAsync(long id, long version, CancellationToken token = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CreditNotesPost);
		var user = RequireUser();
		var result = await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
		{
			var before = await _creditNotes.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Credit note was not found.");
			if (before.Version != version) throw new ConcurrencyConflictException("sales credit note");
			if (before.Status != SalesCreditNoteStatus.Draft) throw new InvalidOperationException("Only a draft credit note can be posted.");
			var postedAt = DateTime.UtcNow;
			if (!await _creditNotes.PostAsync(transaction, id, version, user.Id, postedAt, cancellationToken)) throw new ConcurrencyConflictException("sales credit note");
			var after = await _creditNotes.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Credit note could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), cancellationToken);
			return after;
		}, token);

		var request = new NotificationRequest(
			NotificationType.Workflow,
			NotificationSeverity.Information,
			$"Credit note {result.CreditNoteNumber} posted",
			$"Credit note {result.CreditNoteNumber} was posted for sales invoice {result.SalesInvoiceId}.",
			NotificationSourceTypes.CreditNote,
			result.Id,
			result.CreditNoteNumber,
			user.Id);
		await _notifications.NotifyPermissionHoldersAsync(request, ApplicationPermission.SalesOrdersView, [user.Id], token);
		await _notifications.NotifyPermissionHoldersAsync(request, ApplicationPermission.CreditNotesView, [user.Id], token);
		return result;
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for credit notes.");
}
