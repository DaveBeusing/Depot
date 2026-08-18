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

	public SalesCreditNoteService(IDatabaseTransactionRunner transactions, SalesCreditNoteRepository creditNotes, SalesInvoiceRepository invoices, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_creditNotes = creditNotes;
		_invoices = invoices;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
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
		_authorization.RequirePermission(ApplicationPermission.CreditNotesCreate);
		if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A credit-note reason is required.", nameof(reason));
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
		{
			var invoice = await _invoices.GetByIdAsync(transaction, invoiceId, cancellationToken) ?? throw new InvalidOperationException("Sales invoice was not found.");
			if (invoice.Status != SalesInvoiceStatus.Posted) throw new InvalidOperationException("Only a posted sales invoice can be credited.");
			var existing = await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM SalesCreditNotes WHERE SalesInvoiceId=$InvoiceId AND Status=$Posted;", cancellationToken, new DatabaseParameter("$InvoiceId", invoiceId), new DatabaseParameter("$Posted", (int)SalesCreditNoteStatus.Posted));
			if (Convert.ToInt64(existing ?? 0) > 0) throw new InvalidOperationException("This invoice already has a posted credit note.");
			var value = new SalesCreditNote
			{
				SalesInvoiceId = invoice.Id,
				CustomerId = invoice.CustomerId,
				CreditDate = DateTime.Today,
				Reason = reason.Trim(),
				CreatedByUserId = user.Id,
				Lines = invoice.Lines.Select(line => new SalesCreditNoteLine { SalesInvoiceLineId = line.Id, Quantity = line.Quantity, UnitPrice = line.UnitPrice, DiscountPercent = line.DiscountPercent, TaxRate = line.TaxRate }).ToArray()
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
		return await _transactions.ExecuteAsync(async (transaction, cancellationToken) =>
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
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for credit notes.");
}
