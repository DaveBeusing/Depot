// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceAccountsPayableService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceAccountsPayableRepository _payables;
	private readonly FinanceGeneralLedgerService _generalLedger;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceAccountsPayableService(
		IDatabaseTransactionRunner transactions,
		FinanceAccountsPayableRepository payables,
		FinanceGeneralLedgerService generalLedger,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_payables = payables;
		_generalLedger = generalLedger;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinancePayablesView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.FinancePayablesManage);
	public bool CanCreateDocuments => _authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesCreate);
	public bool CanSubmitDocuments => _authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesSubmit);
	public bool CanApproveDocuments => _authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesApprove);
	public bool CanApproveMatchExceptions => _authorization.HasPermission(ApplicationPermission.FinanceSupplierMatchExceptionsApprove);
	public bool CanPostDocuments => _authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesPost);
	public bool CanReverseDocuments => _authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesReverse);
	public bool CanPostPayments => _authorization.HasPermission(ApplicationPermission.FinancePayablePaymentsPost);
	public bool CanReversePayments => _authorization.HasPermission(ApplicationPermission.FinancePayablePaymentsReverse);

	public Task<FinancePayablesConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		return _payables.GetConfigurationAsync(cancellationToken);
	}

	public async Task<FinancePayablesConfiguration> SaveConfigurationAsync(FinancePayablesConfiguration configuration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesManage);
		RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await ValidateConfigurationAsync(transaction, configuration, token);
			if (configuration.Id == 0)
			{
				if (await _payables.GetAnyConfigurationAsync(transaction, token) is not null) throw new InvalidOperationException("Accounts Payable configuration already exists. Update the existing configuration instead of creating a parallel configuration.");
				var id = await _payables.CreateConfigurationAsync(transaction, configuration, token);
				var created = await _payables.GetConfigurationAsync(transaction, id, token) ?? throw new InvalidOperationException("Accounts Payable configuration could not be reloaded.");
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}
			var before = await _payables.GetConfigurationAsync(transaction, configuration.Id, token) ?? throw new InvalidOperationException("Accounts Payable configuration was not found.");
			if (before.Version != configuration.Version) throw new ConcurrencyConflictException("accounts payable configuration");
			if (await _payables.UpdateConfigurationAsync(transaction, configuration, before.Version, token) != 1) throw new ConcurrencyConflictException("accounts payable configuration");
			var after = await _payables.GetConfigurationAsync(transaction, configuration.Id, token) ?? throw new InvalidOperationException("Accounts Payable configuration could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<PageResult<FinanceSupplierDocument>> SearchDocumentsAsync(string? searchText = null, FinancePayableDocumentStatus? status = null, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		return _payables.SearchDocumentsAsync(searchText, status, pageNumber, pageSize, cancellationToken);
	}

	public Task<FinanceSupplierDocument?> GetDocumentAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		return _payables.GetDocumentAsync(id, cancellationToken);
	}

	public async Task<FinanceSupplierDocument> SaveDraftAsync(FinanceSupplierDocumentDraft draft, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(draft);
		_authorization.RequirePermission(ApplicationPermission.FinanceSupplierInvoicesCreate);
		var user = RequireUser();
		var normalized = NormalizeDraft(draft);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _payables.SupplierExistsAsync(transaction, normalized.SupplierId, token)) throw new InvalidOperationException("The supplier was not found or is inactive.");
			FinanceSupplierDocument before;
			long documentId;
			if (normalized.Id == 0)
			{
				before = BuildDocument(normalized, user.Id, DateTime.UtcNow);
				documentId = await _payables.CreateDocumentAsync(transaction, before, token);
			}
			else
			{
				before = await _payables.LockDocumentAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Supplier document was not found.");
				if (before.Status != FinancePayableDocumentStatus.Draft) throw new InvalidOperationException("Only a draft supplier document can be edited.");
				if (before.Version != normalized.Version) throw new ConcurrencyConflictException("supplier document");
				var updated = BuildDocument(normalized, before.CreatedByUserId, before.CreatedAtUtc) with { Id = before.Id, Version = before.Version };
				if (await _payables.UpdateDraftAsync(transaction, updated, before.Version, token) != 1) throw new ConcurrencyConflictException("supplier document");
				documentId = before.Id;
				await _payables.DeleteLinesAsync(transaction, documentId, token);
			}

			var evaluated = await EvaluateLinesAsync(transaction, documentId, normalized.SupplierId, normalized.Kind, normalized.Lines, token);
			for (var index = 0; index < evaluated.Count; index++) await _payables.CreateLineAsync(transaction, evaluated[index] with { DocumentId = documentId, LineNumber = index + 1 }, token);
			var created = await _payables.GetDocumentAsync(transaction, documentId, token) ?? throw new InvalidOperationException("Supplier document could not be reloaded.");
			if (normalized.Id == 0) await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
			else await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(created.Id, before, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceSupplierDocument> SubmitAsync(long documentId, long expectedVersion, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceSupplierInvoicesSubmit);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _payables.LockDocumentAsync(transaction, documentId, token) ?? throw new InvalidOperationException("Supplier document was not found.");
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("supplier document");
			if (before.Status != FinancePayableDocumentStatus.Draft) throw new InvalidOperationException("Only a draft supplier document can be submitted.");
			var refreshed = await RefreshMatchesAsync(transaction, before, token);
			if (await _payables.SetSubmittedAsync(transaction, before.Id, before.Version, user.Id, DateTime.UtcNow, token) != 1) throw new ConcurrencyConflictException("supplier document");
			var after = (await _payables.GetDocumentAsync(transaction, before.Id, token) ?? throw new InvalidOperationException("Submitted supplier document could not be reloaded.")) with { Lines = refreshed };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(after.Id, "Submitted", before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceSupplierDocument> DecideAsync(long documentId, FinanceSupplierApprovalRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceSupplierInvoicesApprove);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _payables.LockDocumentAsync(transaction, documentId, token) ?? throw new InvalidOperationException("Supplier document was not found.");
			if (before.Version != request.ExpectedVersion) throw new ConcurrencyConflictException("supplier document");
			if (before.Status != FinancePayableDocumentStatus.PendingApproval) throw new InvalidOperationException("Only a pending supplier document can be decided.");
			var comment = NormalizeApprovalComment(before, request.Comment);
			var lines = await RefreshMatchesAsync(transaction, before, token);
			var hasException = lines.Any(line => line.MatchStatus == FinancePayableMatchStatus.Exception);
			var exceptionApproved = false;
			string? exceptionReason = null;
			if (request.Approve && hasException)
			{
				if (!request.ApproveMatchException) throw new InvalidOperationException("The supplier document has unresolved three-way matching exceptions.");
				_authorization.RequirePermission(ApplicationPermission.FinanceSupplierMatchExceptionsApprove);
				exceptionReason = FinanceValidation.Required(request.MatchExceptionReason, nameof(request.MatchExceptionReason), 500);
				exceptionApproved = true;
			}
			var status = request.Approve ? FinancePayableDocumentStatus.Approved : FinancePayableDocumentStatus.Rejected;
			if (await _payables.SetApprovalAsync(transaction, before.Id, before.Version, status, user.Id, DateTime.UtcNow, comment, exceptionApproved, exceptionReason, token) != 1) throw new ConcurrencyConflictException("supplier document");
			var after = await _payables.GetDocumentAsync(transaction, before.Id, token) ?? throw new InvalidOperationException("Supplier document decision could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(after.Id, request.Approve ? "Approved" : "Rejected", before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceSupplierDocument> PostAsync(long documentId, FinanceSupplierPostingRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceSupplierInvoicesPost);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(request));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var document = await _payables.LockDocumentAsync(transaction, documentId, token) ?? throw new InvalidOperationException("Supplier document was not found.");
			if (document.Status == FinancePayableDocumentStatus.Posted && document.PostingOperationId == request.OperationId) return document;
			if (document.Version != request.ExpectedVersion) throw new ConcurrencyConflictException("supplier document");
			if (document.Status != FinancePayableDocumentStatus.Approved) throw new InvalidOperationException("Only an approved supplier document can be posted.");
			var lines = await RefreshMatchesAsync(transaction, document, token);
			if (lines.Any(line => line.MatchStatus == FinancePayableMatchStatus.Exception) && !document.MatchExceptionApproved) throw new InvalidOperationException("Supplier document matching exceptions require explicit approval before posting.");

			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			var profileId = document.Kind == FinancePayableDocumentKind.Invoice ? configuration.InvoicePostingProfileId : configuration.CreditNotePostingProfileId;
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, profileId, token) ?? throw new InvalidOperationException("Accounts Payable posting profile was not found.");
			var periodId = await ResolvePeriodAsync(transaction, configuration, document.DocumentDate, token);
			var rateId = request.ExchangeRateId ?? await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, document.Currency, document.DocumentDate, token);
			var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
			{
				OperationId = request.OperationId,
				PostingProfileId = profile.Id,
				AccountingPeriodId = periodId,
				PostingDate = document.DocumentDate,
				Description = $"Supplier {document.Kind} {document.SupplierDocumentNumber}",
				SourceId = document.Id.ToString(CultureInfo.InvariantCulture),
				SourceReference = document.SupplierDocumentNumber,
				TransactionCurrency = document.Currency,
				ExchangeRateId = rateId,
				Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal)
				{
					[FinancePayablePostingAmountKeys.Gross] = document.GrossAmount,
					[FinancePayablePostingAmountKeys.Net] = document.NetAmount,
					[FinancePayablePostingAmountKeys.Tax] = document.TaxAmount
				},
				Dimensions = request.Dimensions.ToArray()
			}, user.Id, token);
			var sourceType = document.Kind == FinancePayableDocumentKind.Invoice ? FinancePayableSourceTypes.SupplierInvoice : FinancePayableSourceTypes.SupplierCreditNote;
			var kind = document.Kind == FinancePayableDocumentKind.Invoice ? FinancePayableOpenItemKind.Invoice : FinancePayableOpenItemKind.CreditNote;
			var openItem = new FinancePayableOpenItem
			{
				LegalEntityId = configuration.LegalEntityId,
				AccountingBookId = journal.AccountingBookId,
				SupplierId = document.SupplierId,
				Kind = kind,
				SourceType = sourceType,
				SourceId = document.Id.ToString(CultureInfo.InvariantCulture),
				SourceReference = document.SupplierDocumentNumber,
				DocumentDate = document.DocumentDate,
				DueDate = document.DueDate,
				Currency = document.Currency,
				OriginalAmount = document.GrossAmount,
				RemainingAmount = document.GrossAmount,
				JournalEntryId = journal.Id,
				OperationId = request.OperationId,
				CreatedAtUtc = DateTime.UtcNow,
				CreatedByUserId = user.Id
			};
			var openItemId = await _payables.CreateOpenItemAsync(transaction, openItem, token);
			if (await _payables.SetPostedAsync(transaction, document.Id, document.Version, request.OperationId, openItemId, journal.Id, user.Id, DateTime.UtcNow, token) != 1) throw new ConcurrencyConflictException("supplier document");
			var createdOpenItem = openItem with { Id = openItemId, Version = 1 };
			var after = await _payables.GetDocumentAsync(transaction, document.Id, token) ?? throw new InvalidOperationException("Posted supplier document could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(createdOpenItem.Id, createdOpenItem), token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(after.Id, "Posted", document, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceSupplierDocument> ReverseDocumentAsync(long documentId, FinancePayableReversalRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceSupplierInvoicesReverse);
		var user = RequireUser();
		ValidateReversal(request);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var document = await _payables.LockDocumentAsync(transaction, documentId, token) ?? throw new InvalidOperationException("Supplier document was not found.");
			if (document.Status == FinancePayableDocumentStatus.Reversed && document.ReversalOperationId == request.OperationId) return document;
			if (document.Status != FinancePayableDocumentStatus.Posted || !document.JournalEntryId.HasValue || !document.OpenItemId.HasValue) throw new InvalidOperationException("Only a posted supplier document can be reversed.");
			var item = await _payables.LockOpenItemAsync(transaction, document.OpenItemId.Value, token) ?? throw new InvalidOperationException("Supplier document open item was not found.");
			if (item.RemainingAmount != item.OriginalAmount) throw new InvalidOperationException("A settled or partially settled supplier document cannot be reversed until its allocations are reversed.");
			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			var profileId = document.Kind == FinancePayableDocumentKind.Invoice ? configuration.InvoicePostingProfileId : configuration.CreditNotePostingProfileId;
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, profileId, token) ?? throw new InvalidOperationException("Accounts Payable posting profile was not found.");
			var periodId = await ResolvePeriodAsync(transaction, configuration, request.PostingDate, token);
			var reversal = await _generalLedger.ReverseInTransactionAsync(transaction, document.JournalEntryId.Value, request.OperationId, periodId, request.PostingDate, profile.NumberSequenceCode, request.Reason, user.Id, token);
			if (await _payables.UpdateOpenItemRemainingAsync(transaction, item.Id, item.Version, 0m, true, token) != 1) throw new ConcurrencyConflictException("payable open item");
			if (await _payables.SetReversedAsync(transaction, document.Id, document.Version, request.OperationId, reversal.Id, user.Id, DateTime.UtcNow, token) != 1) throw new ConcurrencyConflictException("supplier document");
			var after = await _payables.GetDocumentAsync(transaction, document.Id, token) ?? throw new InvalidOperationException("Reversed supplier document could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(after.Id, "Reversed", document, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<PageResult<FinancePayableOpenItem>> SearchOpenItemsAsync(string? searchText = null, bool includeSettled = false, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		return _payables.SearchOpenItemsAsync(searchText, includeSettled, pageNumber, pageSize, cancellationToken);
	}

	public Task<FinancePayableOpenItem?> GetOpenItemAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		return _payables.GetOpenItemAsync(id, cancellationToken);
	}

	public async Task<IReadOnlyList<FinancePayableAgingSummary>> GetAgingAsync(DateOnly asOfDate, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		var rows = await _payables.GetAgingRowsAsync(asOfDate, cancellationToken);
		return rows.GroupBy(value => (value.SupplierId,value.SupplierName,value.Currency)).Select(group =>
		{
			decimal current=0m, d30=0m, d60=0m, d90=0m, over90=0m, debits=0m;
			foreach (var row in group)
			{
				if (row.Kind != FinancePayableOpenItemKind.Invoice) { debits += row.RemainingAmount; continue; }
				var days = asOfDate.DayNumber - row.DueDate.DayNumber;
				if (days <= 0) current += row.RemainingAmount; else if (days <= 30) d30 += row.RemainingAmount; else if (days <= 60) d60 += row.RemainingAmount; else if (days <= 90) d90 += row.RemainingAmount; else over90 += row.RemainingAmount;
			}
			return new FinancePayableAgingSummary(group.Key.SupplierId,group.Key.SupplierName,group.Key.Currency,current,d30,d60,d90,over90,debits);
		}).OrderBy(value => value.SupplierName,StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Currency.Value,StringComparer.Ordinal).ToArray();
	}

	public Task<IReadOnlyList<FinanceSupplierStatementRow>> GetSupplierStatementAsync(long supplierId, CurrencyCode currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablesView);
		if (supplierId <= 0) throw new ArgumentOutOfRangeException(nameof(supplierId));
		if (toDate < fromDate) throw new ArgumentException("Statement end date must be on or after the start date.",nameof(toDate));
		return _payables.GetSupplierStatementAsync(supplierId,currency,fromDate,toDate,cancellationToken);
	}

	public async Task<FinancePayablePayment> PostPaymentAsync(FinancePayablePaymentRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinancePayablePaymentsPost);
		var user = RequireUser();
		var normalized = NormalizePayment(request);
		var requestHash = HashPayment(normalized);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _payables.FindPaymentByOperationAsync(transaction,normalized.OperationId,token);
			if (existing is not null)
			{
				if (!string.Equals(existing.RequestHash,requestHash,StringComparison.Ordinal)) throw new InvalidOperationException("The payment operation ID is already assigned to different payment content.");
				return existing;
			}
			if (!await _payables.SupplierExistsAsync(transaction,normalized.SupplierId,token)) throw new InvalidOperationException("The payment supplier was not found or is inactive.");
			var configuration = await RequireActiveConfigurationAsync(transaction,token);
			var periodId = await ResolvePeriodAsync(transaction,configuration,normalized.PaymentDate,token);
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction,configuration.PaymentPostingProfileId,token) ?? throw new InvalidOperationException("Supplier payment posting profile was not found.");
			var rateId = normalized.ExchangeRateId ?? await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction,profile.Id,normalized.Currency,normalized.PaymentDate,token);
			var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction,new FinanceProfilePostingRequest
			{
				OperationId=normalized.OperationId,PostingProfileId=profile.Id,AccountingPeriodId=periodId,PostingDate=normalized.PaymentDate,Description=normalized.Description,SourceId=normalized.OperationId.ToString("D"),SourceReference=normalized.Reference,TransactionCurrency=normalized.Currency,ExchangeRateId=rateId,
				Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal) { [FinancePayablePostingAmountKeys.Payment]=normalized.Amount },Dimensions=normalized.Dimensions
			},user.Id,token);
			var now = DateTime.UtcNow;
			var openItem = new FinancePayableOpenItem { LegalEntityId=configuration.LegalEntityId,AccountingBookId=journal.AccountingBookId,SupplierId=normalized.SupplierId,Kind=FinancePayableOpenItemKind.Payment,SourceType=FinancePayableSourceTypes.SupplierPayment,SourceId=normalized.OperationId.ToString("D"),SourceReference=normalized.Reference,DocumentDate=normalized.PaymentDate,DueDate=normalized.PaymentDate,Currency=normalized.Currency,OriginalAmount=normalized.Amount,RemainingAmount=normalized.Amount,JournalEntryId=journal.Id,OperationId=normalized.OperationId,CreatedAtUtc=now,CreatedByUserId=user.Id };
			var openItemId = await _payables.CreateOpenItemAsync(transaction,openItem,token);
			var debit = openItem with { Id=openItemId,Version=1 };
			if (normalized.Allocations.Count > 0) debit = await ApplyAllocationsAsync(transaction,normalized.OperationId,debit,normalized.Allocations,normalized.PaymentDate,user.Id,token);
			var payment = new FinancePayablePayment { OperationId=normalized.OperationId,RequestHash=requestHash,SupplierId=normalized.SupplierId,Currency=normalized.Currency,PaymentDate=normalized.PaymentDate,Amount=normalized.Amount,Reference=normalized.Reference,Description=normalized.Description,OpenItemId=openItemId,JournalEntryId=journal.Id,CreatedAtUtc=now,CreatedByUserId=user.Id };
			var paymentId = await _payables.CreatePaymentAsync(transaction,payment,token);
			var created = payment with { Id=paymentId,Version=1 };
			await _auditEntries.CreateAsync(transaction,_audit.CreateCreatedEntry(created.Id,created),token);
			await _auditEntries.CreateAsync(transaction,_audit.CreateCreatedEntry(debit.Id,debit),token);
			return created;
		},cancellationToken);
	}

	public async Task<FinancePayableOpenItem> AllocateDebitAsync(Guid operationId, long debitOpenItemId, DateOnly allocationDate, IReadOnlyList<FinancePayableAllocationRequest> allocations, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePayablePaymentsPost);
		var user = RequireUser();
		if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.",nameof(operationId));
		if (debitOpenItemId <= 0 || allocations is null || allocations.Count == 0) throw new ArgumentException("A debit open item and at least one allocation are required.");
		var hash = HashAllocations(debitOpenItemId,allocationDate,allocations);
		return await _transactions.ExecuteAsync(async (transaction,token) =>
		{
			var previous = await _payables.FindAllocationOperationAsync(transaction,operationId,token);
			if (previous is not null)
			{
				if (!string.Equals(previous.RequestHash,hash,StringComparison.Ordinal)) throw new InvalidOperationException("The allocation operation ID is already assigned to different content.");
				return await _payables.LockOpenItemAsync(transaction,previous.DebitOpenItemId,token) ?? throw new InvalidOperationException("Idempotent allocation references a missing debit open item.");
			}
			var debit = await _payables.LockOpenItemAsync(transaction,debitOpenItemId,token) ?? throw new InvalidOperationException("Debit open item was not found.");
			if (debit.Direction != FinancePayableDirection.Debit || debit.IsVoided) throw new InvalidOperationException("Only an active debit payable balance can be allocated.");
			var result = await ApplyAllocationsAsync(transaction,operationId,debit,allocations,allocationDate,user.Id,token);
			await _payables.CreateAllocationOperationAsync(transaction,operationId,hash,debitOpenItemId,DateTime.UtcNow,user.Id,token);
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(result.Id,"Allocated",debit,result),token);
			return result;
		},cancellationToken);
	}

	public async Task<FinancePayablePayment> ReversePaymentAsync(long paymentId, FinancePayableReversalRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinancePayablePaymentsReverse);
		var user = RequireUser();
		ValidateReversal(request);
		return await _transactions.ExecuteAsync(async (transaction,token) =>
		{
			var payment = await _payables.LockPaymentAsync(transaction,paymentId,token) ?? throw new InvalidOperationException("Supplier payment was not found.");
			if (payment.IsReversed)
			{
				if (payment.ReversalOperationId == request.OperationId) return payment;
				throw new InvalidOperationException("Supplier payment has already been reversed.");
			}
			var configuration = await RequireActiveConfigurationAsync(transaction,token);
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction,configuration.PaymentPostingProfileId,token) ?? throw new InvalidOperationException("Supplier payment posting profile was not found.");
			var periodId = await ResolvePeriodAsync(transaction,configuration,request.PostingDate,token);
			var reversal = await _generalLedger.ReverseInTransactionAsync(transaction,payment.JournalEntryId,request.OperationId,periodId,request.PostingDate,profile.NumberSequenceCode,request.Reason,user.Id,token);
			var allocations = await _payables.GetActiveAllocationsForDebitAsync(transaction,payment.OpenItemId,token);
			foreach (var allocation in allocations)
			{
				var credit = await _payables.LockOpenItemAsync(transaction,allocation.CreditOpenItemId,token) ?? throw new InvalidOperationException("Allocated payable open item was not found.");
				if (await _payables.UpdateOpenItemRemainingAsync(transaction,credit.Id,credit.Version,credit.RemainingAmount+allocation.Amount,false,token) != 1) throw new ConcurrencyConflictException("payable open item");
			}
			var now=DateTime.UtcNow;
			await _payables.ReverseAllocationsForDebitAsync(transaction,payment.OpenItemId,request.OperationId,now,user.Id,token);
			var debit = await _payables.LockOpenItemAsync(transaction,payment.OpenItemId,token) ?? throw new InvalidOperationException("Payment open item was not found.");
			if (await _payables.UpdateOpenItemRemainingAsync(transaction,debit.Id,debit.Version,0m,true,token) != 1) throw new ConcurrencyConflictException("payable open item");
			if (await _payables.MarkPaymentReversedAsync(transaction,payment,payment.Version,request.OperationId,reversal.Id,now,user.Id,token) != 1) throw new ConcurrencyConflictException("payable payment");
			var after=payment with { Version=payment.Version+1,IsReversed=true,ReversalOperationId=request.OperationId,ReversalJournalEntryId=reversal.Id,ReversedAtUtc=now,ReversedByUserId=user.Id };
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(payment.Id,"Reversed",payment,after),token);
			return after;
		},cancellationToken);
	}

	private async Task<IReadOnlyList<FinanceSupplierDocumentLine>> RefreshMatchesAsync(DatabaseTransactionContext transaction, FinanceSupplierDocument document, CancellationToken cancellationToken)
	{
		var drafts = document.Lines.Select(line => new FinanceSupplierDocumentLineDraft { PurchaseOrderLineId=line.PurchaseOrderLineId,GoodsReceiptLineId=line.GoodsReceiptLineId,Description=line.Description,Quantity=line.Quantity,UnitPrice=line.UnitPrice,TaxAmount=line.TaxAmount }).ToArray();
		var refreshed = await EvaluateLinesAsync(transaction,document.Id,document.SupplierId,document.Kind,drafts,cancellationToken);
		for (var index=0;index<refreshed.Count;index++)
		{
			var persisted = refreshed[index] with { Id=document.Lines[index].Id,DocumentId=document.Id,LineNumber=document.Lines[index].LineNumber };
			await _payables.UpdateLineMatchAsync(transaction,persisted,cancellationToken);
			refreshed[index]=persisted;
		}
		return refreshed;
	}

	private async Task<List<FinanceSupplierDocumentLine>> EvaluateLinesAsync(DatabaseTransactionContext transaction, long documentId, long supplierId, FinancePayableDocumentKind kind, IReadOnlyList<FinanceSupplierDocumentLineDraft> lines, CancellationToken cancellationToken)
	{
		var result = new List<FinanceSupplierDocumentLine>(lines.Count);
		for (var index=0;index<lines.Count;index++)
		{
			var line=lines[index];
			var matchStatus=FinancePayableMatchStatus.NotRequired;
			decimal? orderedPrice=null,received=null,previous=null;
			decimal quantityVariance=0m,priceVariance=0m;
			if (kind==FinancePayableDocumentKind.Invoice && line.PurchaseOrderLineId.HasValue)
			{
				var reference=await _payables.GetPurchaseMatchReferenceAsync(transaction,line.PurchaseOrderLineId.Value,line.GoodsReceiptLineId,cancellationToken) ?? throw new InvalidOperationException($"Purchase-order match reference for line {index+1} was not found or points to a reversed goods receipt.");
				if (reference.SupplierId!=supplierId) throw new InvalidOperationException($"Purchase-order line {line.PurchaseOrderLineId.Value} belongs to a different supplier.");
				previous=await _payables.GetPreviouslyInvoicedQuantityAsync(transaction,line.PurchaseOrderLineId.Value,documentId,cancellationToken);
				orderedPrice=reference.OrderedUnitPrice;
				received=reference.ReceivedQuantity;
				var available=Math.Max(0m,reference.ReceivedQuantity-previous.Value);
				quantityVariance=Math.Max(0m,line.Quantity-available);
				priceVariance=line.UnitPrice-reference.OrderedUnitPrice;
				matchStatus=quantityVariance==0m && priceVariance==0m ? FinancePayableMatchStatus.Matched : FinancePayableMatchStatus.Exception;
			}
			result.Add(new FinanceSupplierDocumentLine { DocumentId=documentId,LineNumber=index+1,PurchaseOrderLineId=line.PurchaseOrderLineId,GoodsReceiptLineId=line.GoodsReceiptLineId,Description=line.Description,Quantity=line.Quantity,UnitPrice=line.UnitPrice,NetAmount=line.NetAmount,TaxAmount=line.TaxAmount,GrossAmount=line.GrossAmount,MatchStatus=matchStatus,OrderedUnitPrice=orderedPrice,ReceivedQuantity=received,PreviouslyInvoicedQuantity=previous,QuantityVariance=quantityVariance,PriceVariance=priceVariance });
		}
		return result;
	}

	private async Task<FinancePayableOpenItem> ApplyAllocationsAsync(DatabaseTransactionContext transaction, Guid operationId, FinancePayableOpenItem debit, IReadOnlyList<FinancePayableAllocationRequest> allocations, DateOnly date, long userId, CancellationToken cancellationToken)
	{
		var total=allocations.Sum(value=>value.Amount);
		if (total<=0m || total>debit.RemainingAmount) throw new InvalidOperationException("Allocation total must be positive and cannot exceed the available debit balance.");
		if (allocations.Select(value=>value.CreditOpenItemId).Distinct().Count()!=allocations.Count) throw new InvalidOperationException("A credit open item can appear only once in an allocation operation.");
		foreach (var allocation in allocations)
		{
			if (allocation.Amount<=0m) throw new InvalidOperationException("Allocation amounts must be positive.");
			var credit=await _payables.LockOpenItemAsync(transaction,allocation.CreditOpenItemId,cancellationToken) ?? throw new InvalidOperationException("Credit payable open item was not found.");
			if (credit.Direction!=FinancePayableDirection.Credit || credit.IsVoided) throw new InvalidOperationException("Allocations can settle only active credit payables.");
			if (credit.SupplierId!=debit.SupplierId || credit.Currency!=debit.Currency || credit.AccountingBookId!=debit.AccountingBookId || credit.LegalEntityId!=debit.LegalEntityId) throw new InvalidOperationException("Allocation debit and credit must belong to the same supplier, currency, accounting book and legal entity.");
			if (allocation.Amount>credit.RemainingAmount) throw new InvalidOperationException("Allocation amount exceeds the payable open item's remaining balance.");
			if (await _payables.UpdateOpenItemRemainingAsync(transaction,credit.Id,credit.Version,credit.RemainingAmount-allocation.Amount,false,cancellationToken)!=1) throw new ConcurrencyConflictException("payable open item");
			await _payables.CreateAllocationAsync(transaction,new FinancePayableAllocation { OperationId=operationId,DebitOpenItemId=debit.Id,CreditOpenItemId=credit.Id,Amount=allocation.Amount,AllocationDate=date,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=userId },cancellationToken);
		}
		if (await _payables.UpdateOpenItemRemainingAsync(transaction,debit.Id,debit.Version,debit.RemainingAmount-total,false,cancellationToken)!=1) throw new ConcurrencyConflictException("payable open item");
		return debit with { Version=debit.Version+1,RemainingAmount=debit.RemainingAmount-total };
	}

	private async Task ValidateConfigurationAsync(DatabaseTransactionContext transaction, FinancePayablesConfiguration configuration, CancellationToken cancellationToken)
	{
		if (configuration.LegalEntityId==Guid.Empty || configuration.FiscalCalendarId==Guid.Empty) throw new ArgumentException("Accounts Payable configuration requires a legal entity and fiscal calendar.",nameof(configuration));
		if (configuration.InvoicePostingProfileId<=0 || configuration.CreditNotePostingProfileId<=0 || configuration.PaymentPostingProfileId<=0) throw new ArgumentException("All Accounts Payable posting profiles are required.",nameof(configuration));
		var calendar=await _payables.GetFiscalCalendarAsync(transaction,configuration.FiscalCalendarId,cancellationToken) ?? throw new InvalidOperationException("Accounts Payable fiscal calendar was not found.");
		if (!calendar.IsActive || calendar.LegalEntityId!=configuration.LegalEntityId) throw new InvalidOperationException("Accounts Payable fiscal calendar must be active and belong to the configured legal entity.");
		var profiles=new[]
		{
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction,configuration.InvoicePostingProfileId,cancellationToken),FinancePayableSourceTypes.SupplierInvoice,FinancePayablePostingAmountKeys.Gross),
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction,configuration.CreditNotePostingProfileId,cancellationToken),FinancePayableSourceTypes.SupplierCreditNote,FinancePayablePostingAmountKeys.Gross),
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction,configuration.PaymentPostingProfileId,cancellationToken),FinancePayableSourceTypes.SupplierPayment,FinancePayablePostingAmountKeys.Payment)
		};
		Guid? bookId=null;
		foreach (var (profile,sourceType,amountKey) in profiles)
		{
			if (profile is null || !profile.IsActive) throw new InvalidOperationException($"Accounts Payable posting profile for '{sourceType}' is missing or inactive.");
			if (profile.LegalEntityId!=configuration.LegalEntityId) throw new InvalidOperationException("All Accounts Payable posting profiles must belong to the configured legal entity.");
			if (!string.Equals(profile.SourceType,sourceType,StringComparison.Ordinal) || !string.Equals(profile.SourceEvent,"Posted",StringComparison.Ordinal)) throw new InvalidOperationException($"Posting profile '{profile.Code}' must use source '{sourceType}' and event 'Posted'.");
			if (!profile.Lines.Any(line=>string.Equals(line.AmountKey,amountKey,StringComparison.Ordinal))) throw new InvalidOperationException($"Posting profile '{profile.Code}' does not contain required amount key '{amountKey}'.");
			bookId ??= profile.AccountingBookId;
			if (bookId!=profile.AccountingBookId) throw new InvalidOperationException("All Accounts Payable posting profiles must use the same accounting book.");
		}
	}

	private async Task<FinancePayablesConfiguration> RequireActiveConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken)
	{
		var configurations=await _payables.GetActiveConfigurationsAsync(transaction,cancellationToken);
		if (configurations.Count==0) throw new InvalidOperationException("Accounts Payable is not configured and active.");
		if (configurations.Count>1) throw new InvalidOperationException("Accounts Payable posting is ambiguous because more than one active configuration exists.");
		return configurations[0];
	}

	private async Task<Guid> ResolvePeriodAsync(DatabaseTransactionContext transaction, FinancePayablesConfiguration configuration, DateOnly date, CancellationToken cancellationToken)
	{
		var periods=await _payables.FindOpenPeriodsAsync(transaction,configuration.FiscalCalendarId,date,cancellationToken);
		if (periods.Count==0) throw new InvalidOperationException($"No open accounting period contains {date:yyyy-MM-dd} in the Accounts Payable fiscal calendar.");
		if (periods.Count>1) throw new InvalidOperationException($"More than one open accounting period contains {date:yyyy-MM-dd}; period resolution must be unambiguous.");
		return periods[0].Id;
	}

	private FinanceSupplierDocument BuildDocument(FinanceSupplierDocumentDraft draft, long createdBy, DateTime createdAt)
	{
		var net=draft.Lines.Sum(line=>line.NetAmount);
		var tax=draft.Lines.Sum(line=>line.TaxAmount);
		return new FinanceSupplierDocument { Kind=draft.Kind,SupplierId=draft.SupplierId,SupplierDocumentNumber=draft.SupplierDocumentNumber,InternalReference=draft.InternalReference,DocumentDate=draft.DocumentDate,DueDate=draft.DueDate,Currency=draft.Currency,Status=FinancePayableDocumentStatus.Draft,NetAmount=net,TaxAmount=tax,GrossAmount=net+tax,CreatedByUserId=createdBy,CreatedAtUtc=createdAt };
	}

	private static FinanceSupplierDocumentDraft NormalizeDraft(FinanceSupplierDocumentDraft draft)
	{
		if (draft.SupplierId<=0) throw new ArgumentOutOfRangeException(nameof(draft.SupplierId));
		if (draft.DueDate<draft.DocumentDate) throw new ArgumentException("Supplier document due date cannot be before its document date.",nameof(draft));
		if (draft.Lines.Count==0) throw new InvalidOperationException("Supplier document requires at least one line.");
		var lines=draft.Lines.Select(line =>
		{
			if (line.Quantity<=0m || line.UnitPrice<0m || line.TaxAmount<0m) throw new InvalidOperationException("Supplier document quantities must be positive and monetary amounts cannot be negative.");
			if (line.GoodsReceiptLineId.HasValue && !line.PurchaseOrderLineId.HasValue) throw new InvalidOperationException("A goods-receipt match requires a purchase-order line.");
			return line with { Description=FinanceValidation.Required(line.Description,nameof(line.Description),500) };
		}).ToArray();
		return draft with { SupplierDocumentNumber=FinanceValidation.Required(draft.SupplierDocumentNumber,nameof(draft.SupplierDocumentNumber),200),InternalReference=Optional(draft.InternalReference,200),Lines=lines };
	}

	private string? NormalizeApprovalComment(FinanceSupplierDocument document, string? comment)
	{
		var isSelfDecision=document.CreatedByUserId==_authorization.CurrentUser?.Id;
		var isAdministrator=_authorization.IsInRole(SystemRoleCatalog.AdministratorCode);
		return AdministratorOverrideAudit.NormalizeDecisionComment(isSelfDecision,isAdministrator,Optional(comment,500));
	}

	private static FinancePayablePaymentRequest NormalizePayment(FinancePayablePaymentRequest request)
	{
		if (request.OperationId==Guid.Empty) throw new ArgumentException("An operation ID is required.",nameof(request));
		if (request.SupplierId<=0 || request.Amount<=0m) throw new ArgumentOutOfRangeException(nameof(request),"Supplier and positive payment amount are required.");
		if (request.Allocations.Any(value=>value.Amount<=0m) || request.Allocations.Sum(value=>value.Amount)>request.Amount) throw new InvalidOperationException("Payment allocations must be positive and cannot exceed the payment amount.");
		return request with { Description=FinanceValidation.Required(request.Description,nameof(request.Description),500),Reference=Optional(request.Reference,200),Allocations=request.Allocations.ToArray(),Dimensions=request.Dimensions.ToArray() };
	}

	private static void ValidateReversal(FinancePayableReversalRequest request)
	{
		if (request.OperationId==Guid.Empty) throw new ArgumentException("A reversal operation ID is required.",nameof(request));
		FinanceValidation.Required(request.Reason,nameof(request.Reason),500);
	}

	private static string HashPayment(FinancePayablePaymentRequest request)
	{
		var builder=new StringBuilder();
		Append(builder,request.SupplierId.ToString(CultureInfo.InvariantCulture));Append(builder,request.Currency.Value);Append(builder,request.PaymentDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));Append(builder,request.Amount.ToString("G29",CultureInfo.InvariantCulture));Append(builder,request.Reference);Append(builder,request.Description);Append(builder,request.ExchangeRateId?.ToString("D"));
		foreach (var allocation in request.Allocations.OrderBy(value=>value.CreditOpenItemId)) { Append(builder,allocation.CreditOpenItemId.ToString(CultureInfo.InvariantCulture));Append(builder,allocation.Amount.ToString("G29",CultureInfo.InvariantCulture)); }
		foreach (var dimension in request.Dimensions.OrderBy(value=>value.DimensionId)) { Append(builder,dimension.DimensionId.ToString("D"));Append(builder,dimension.DimensionValueId.ToString("D")); }
		return Hash(builder);
	}
	private static string HashAllocations(long debitOpenItemId, DateOnly date, IReadOnlyList<FinancePayableAllocationRequest> allocations)
	{
		var builder=new StringBuilder();Append(builder,debitOpenItemId.ToString(CultureInfo.InvariantCulture));Append(builder,date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));foreach(var allocation in allocations.OrderBy(value=>value.CreditOpenItemId)){Append(builder,allocation.CreditOpenItemId.ToString(CultureInfo.InvariantCulture));Append(builder,allocation.Amount.ToString("G29",CultureInfo.InvariantCulture));}return Hash(builder);
	}
	private static string Hash(StringBuilder builder)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	private static void Append(StringBuilder builder,string? value)=>builder.Append(value??"<null>").Append('\u001f');
	private static string? Optional(string? value,int maximumLength){if(string.IsNullOrWhiteSpace(value))return null;var normalized=value.Trim();if(normalized.Length>maximumLength)throw new ArgumentException($"Value cannot exceed {maximumLength} characters.");return normalized;}
	private User RequireUser()=>_authorization.CurrentUser is { IsActive:true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for Accounts Payable operations.");
}
