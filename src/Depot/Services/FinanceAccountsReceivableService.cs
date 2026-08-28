// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceAccountsReceivableService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceAccountsReceivableRepository _receivables;
	private readonly FinanceGeneralLedgerService _generalLedger;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceAccountsReceivableService(
		IDatabaseTransactionRunner transactions,
		FinanceAccountsReceivableRepository receivables,
		FinanceGeneralLedgerService generalLedger,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_receivables = receivables;
		_generalLedger = generalLedger;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinanceReceivablesView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.FinanceReceivablesManage);
	public bool CanPostPayments => _authorization.HasPermission(ApplicationPermission.FinanceReceivablePaymentsPost);
	public bool CanReversePayments => _authorization.HasPermission(ApplicationPermission.FinanceReceivablePaymentsReverse);
	public bool CanPostWriteOffs => _authorization.HasPermission(ApplicationPermission.FinanceReceivableWriteOffsPost);
	public bool CanReverseWriteOffs => _authorization.HasPermission(ApplicationPermission.FinanceReceivableWriteOffsReverse);
	public bool CanViewDunning => _authorization.HasPermission(ApplicationPermission.FinanceDunningView);
	public bool CanManageDunning => _authorization.HasPermission(ApplicationPermission.FinanceDunningManage);

	public Task<FinanceReceivablesConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablesView);
		return _receivables.GetConfigurationAsync(cancellationToken);
	}

	public async Task<FinanceReceivablesConfiguration> SaveConfigurationAsync(FinanceReceivablesConfiguration configuration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablesManage);
		RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await ValidateConfigurationAsync(transaction, configuration, token);
			if (configuration.Id == 0)
			{
				if (await _receivables.GetAnyConfigurationAsync(transaction, token) is not null)
					throw new InvalidOperationException("Accounts Receivable configuration already exists. Update the existing configuration instead of creating a parallel configuration.");
				var id = await _receivables.CreateConfigurationAsync(transaction, configuration, token);
				var created = await _receivables.GetConfigurationAsync(transaction, id, token) ?? throw new InvalidOperationException("Accounts Receivable configuration could not be reloaded.");
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}

			var before = await _receivables.GetConfigurationAsync(transaction, configuration.Id, token) ?? throw new InvalidOperationException("Accounts Receivable configuration was not found.");
			if (before.Version != configuration.Version) throw new ConcurrencyConflictException("accounts receivable configuration");
			if (await _receivables.UpdateConfigurationAsync(transaction, configuration, before.Version, token) != 1) throw new ConcurrencyConflictException("accounts receivable configuration");
			var after = await _receivables.GetConfigurationAsync(transaction, configuration.Id, token) ?? throw new InvalidOperationException("Accounts Receivable configuration could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<PageResult<FinanceReceivableOpenItem>> SearchOpenItemsAsync(string? searchText = null, bool includeSettled = false, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablesView);
		return _receivables.SearchOpenItemsAsync(searchText, includeSettled, pageNumber, pageSize, cancellationToken);
	}

	public Task<FinanceReceivableOpenItem?> GetOpenItemAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablesView);
		return _receivables.GetOpenItemAsync(id, cancellationToken);
	}

	public async Task<IReadOnlyList<FinanceReceivableAgingSummary>> GetAgingAsync(DateOnly asOfDate, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablesView);
		var rows = await _receivables.GetAgingRowsAsync(asOfDate, cancellationToken);
		return rows
			.GroupBy(value => (value.CustomerId, value.CustomerName, value.Currency))
			.Select(group =>
			{
				decimal current = 0m;
				decimal days1To30 = 0m;
				decimal days31To60 = 0m;
				decimal days61To90 = 0m;
				decimal over90 = 0m;
				decimal credits = 0m;
				foreach (var row in group)
				{
					if (row.Kind != FinanceReceivableOpenItemKind.Invoice)
					{
						credits += row.RemainingAmount;
						continue;
					}
					var days = asOfDate.DayNumber - row.DueDate.DayNumber;
					if (days <= 0) current += row.RemainingAmount;
					else if (days <= 30) days1To30 += row.RemainingAmount;
					else if (days <= 60) days31To60 += row.RemainingAmount;
					else if (days <= 90) days61To90 += row.RemainingAmount;
					else over90 += row.RemainingAmount;
				}
				return new FinanceReceivableAgingSummary(group.Key.CustomerId, group.Key.CustomerName, group.Key.Currency, current, days1To30, days31To60, days61To90, over90, credits);
			})
			.OrderBy(value => value.CustomerName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(value => value.Currency.Value, StringComparer.Ordinal)
			.ToArray();
	}

	public Task<IReadOnlyList<FinanceCustomerStatementRow>> GetCustomerStatementAsync(long customerId, CurrencyCode currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablesView);
		if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
		if (toDate < fromDate) throw new ArgumentException("Statement end date must be on or after the start date.", nameof(toDate));
		return _receivables.GetCustomerStatementAsync(customerId, currency, fromDate, toDate, cancellationToken);
	}

	public async Task<FinanceReceivablePayment> PostPaymentAsync(FinanceReceivablePaymentRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablePaymentsPost);
		var user = RequireUser();
		var normalized = NormalizePayment(request);
		var requestHash = HashPayment(normalized);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _receivables.FindPaymentByOperationAsync(transaction, normalized.OperationId, token);
			if (existing is not null)
			{
				if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) throw new InvalidOperationException("The payment operation ID is already assigned to different payment content.");
				return existing;
			}
			if (!await _receivables.CustomerExistsAsync(transaction, normalized.CustomerId, token)) throw new InvalidOperationException("The payment customer was not found or is inactive.");

			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			var periodId = await ResolvePeriodAsync(transaction, configuration, normalized.PaymentDate, token);
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.PaymentPostingProfileId, token) ?? throw new InvalidOperationException("Payment posting profile was not found.");
			var rateId = normalized.ExchangeRateId ?? await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, normalized.Currency, normalized.PaymentDate, token);
			var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
			{
				OperationId = normalized.OperationId,
				PostingProfileId = profile.Id,
				AccountingPeriodId = periodId,
				PostingDate = normalized.PaymentDate,
				Description = normalized.Description,
				SourceId = normalized.OperationId.ToString("D"),
				SourceReference = normalized.Reference,
				TransactionCurrency = normalized.Currency,
				ExchangeRateId = rateId,
				Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal) { [FinanceReceivablePostingAmountKeys.Payment] = normalized.Amount },
				Dimensions = normalized.Dimensions
			}, user.Id, token);

			var now = DateTime.UtcNow;
			var openItem = new FinanceReceivableOpenItem
			{
				LegalEntityId = configuration.LegalEntityId,
				AccountingBookId = journal.AccountingBookId,
				CustomerId = normalized.CustomerId,
				Kind = FinanceReceivableOpenItemKind.Payment,
				SourceType = FinanceReceivableSourceTypes.Payment,
				SourceId = normalized.OperationId.ToString("D"),
				SourceReference = normalized.Reference,
				DocumentDate = normalized.PaymentDate,
				DueDate = normalized.PaymentDate,
				Currency = normalized.Currency,
				OriginalAmount = normalized.Amount,
				RemainingAmount = normalized.Amount,
				JournalEntryId = journal.Id,
				OperationId = normalized.OperationId,
				CreatedAtUtc = now,
				CreatedByUserId = user.Id
			};
			var openItemId = await _receivables.CreateOpenItemAsync(transaction, openItem, token);
			var createdOpenItem = openItem with { Id = openItemId, Version = 1 };
			var currentOpenItem = normalized.Allocations.Count > 0
				? await ApplyAllocationsAsync(transaction, normalized.OperationId, createdOpenItem, normalized.Allocations, normalized.PaymentDate, user.Id, token)
				: createdOpenItem;

			var payment = new FinanceReceivablePayment
			{
				OperationId = normalized.OperationId,
				RequestHash = requestHash,
				CustomerId = normalized.CustomerId,
				Currency = normalized.Currency,
				PaymentDate = normalized.PaymentDate,
				Amount = normalized.Amount,
				Reference = normalized.Reference,
				Description = normalized.Description,
				OpenItemId = openItemId,
				JournalEntryId = journal.Id,
				CreatedAtUtc = now,
				CreatedByUserId = user.Id
			};
			var paymentId = await _receivables.CreatePaymentAsync(transaction, payment, token);
			var created = payment with { Id = paymentId, Version = 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(currentOpenItem.Id, currentOpenItem), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceReceivableOpenItem> AllocateCreditAsync(Guid operationId, long creditOpenItemId, DateOnly allocationDate, IReadOnlyList<FinanceReceivableAllocationRequest> allocations, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablePaymentsPost);
		var user = RequireUser();
		if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
		if (creditOpenItemId <= 0) throw new ArgumentOutOfRangeException(nameof(creditOpenItemId));
		if (allocations is null || allocations.Count == 0) throw new ArgumentException("At least one allocation is required.", nameof(allocations));
		var hash = HashAllocations(creditOpenItemId, allocationDate, allocations);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var previous = await _receivables.FindAllocationOperationAsync(transaction, operationId, token);
			if (previous is not null)
			{
				if (!string.Equals(previous.RequestHash, hash, StringComparison.Ordinal)) throw new InvalidOperationException("The allocation operation ID is already assigned to different allocation content.");
				return await _receivables.LockOpenItemAsync(transaction, previous.CreditOpenItemId, token) ?? throw new InvalidOperationException("The idempotent allocation references a missing credit open item.");
			}

			var credit = await _receivables.LockOpenItemAsync(transaction, creditOpenItemId, token) ?? throw new InvalidOperationException("Credit open item was not found.");
			if (credit.Direction != FinanceReceivableDirection.Credit || credit.IsVoided) throw new InvalidOperationException("Only an active credit open item can be allocated.");
			var result = await ApplyAllocationsAsync(transaction, operationId, credit, allocations, allocationDate, user.Id, token);
			await _receivables.CreateAllocationOperationAsync(transaction, operationId, hash, creditOpenItemId, DateTime.UtcNow, user.Id, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(result.Id, "Allocated", credit, result), token);
			return result;
		}, cancellationToken);
	}

	public async Task<FinanceReceivablePayment> ReversePaymentAsync(long paymentId, FinanceReceivableReversalRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivablePaymentsReverse);
		var user = RequireUser();
		ValidateReversal(request);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var payment = await _receivables.LockPaymentAsync(transaction, paymentId, token) ?? throw new InvalidOperationException("Payment was not found.");
			if (payment.IsReversed)
			{
				if (payment.ReversalOperationId == request.OperationId) return payment;
				throw new InvalidOperationException("Payment has already been reversed.");
			}

			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.PaymentPostingProfileId, token) ?? throw new InvalidOperationException("Payment posting profile was not found.");
			var periodId = await ResolvePeriodAsync(transaction, configuration, request.PostingDate, token);
			var reversal = await _generalLedger.ReverseInTransactionAsync(transaction, payment.JournalEntryId, request.OperationId, periodId, request.PostingDate, profile.NumberSequenceCode, request.Reason, user.Id, token);

			var allocations = await _receivables.GetActiveAllocationsForCreditAsync(transaction, payment.OpenItemId, token);
			foreach (var allocation in allocations)
			{
				var debit = await _receivables.LockOpenItemAsync(transaction, allocation.DebitOpenItemId, token) ?? throw new InvalidOperationException("Allocated debit open item was not found.");
				if (await _receivables.UpdateOpenItemRemainingAsync(transaction, debit.Id, debit.Version, debit.RemainingAmount + allocation.Amount, false, token) != 1) throw new ConcurrencyConflictException("receivable open item");
			}
			var reversedAt = DateTime.UtcNow;
			await _receivables.ReverseAllocationsForCreditAsync(transaction, payment.OpenItemId, request.OperationId, reversedAt, user.Id, token);
			var credit = await _receivables.LockOpenItemAsync(transaction, payment.OpenItemId, token) ?? throw new InvalidOperationException("Payment open item was not found.");
			if (await _receivables.UpdateOpenItemRemainingAsync(transaction, credit.Id, credit.Version, 0m, true, token) != 1) throw new ConcurrencyConflictException("receivable open item");
			if (await _receivables.MarkPaymentReversedAsync(transaction, payment, payment.Version, request.OperationId, reversal.Id, reversedAt, user.Id, token) != 1) throw new ConcurrencyConflictException("receivable payment");
			var after = payment with { Version = payment.Version + 1, IsReversed = true, ReversalOperationId = request.OperationId, ReversalJournalEntryId = reversal.Id, ReversedAtUtc = reversedAt, ReversedByUserId = user.Id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(payment.Id, "Reversed", payment, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceReceivableWriteOff> PostWriteOffAsync(FinanceReceivableWriteOffRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivableWriteOffsPost);
		var user = RequireUser();
		var normalized = NormalizeWriteOff(request);
		var requestHash = HashWriteOff(normalized);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _receivables.FindWriteOffByOperationAsync(transaction, normalized.OperationId, token);
			if (existing is not null)
			{
				if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) throw new InvalidOperationException("The write-off operation ID is already assigned to different content.");
				return existing;
			}

			var item = await _receivables.LockOpenItemAsync(transaction, normalized.OpenItemId, token) ?? throw new InvalidOperationException("Receivable open item was not found.");
			if (item.Direction != FinanceReceivableDirection.Debit || item.IsVoided) throw new InvalidOperationException("Only an active debit receivable can be written off.");
			if (normalized.Amount > item.RemainingAmount) throw new InvalidOperationException("Write-off amount cannot exceed the remaining receivable.");
			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			if (configuration.LegalEntityId != item.LegalEntityId) throw new InvalidOperationException("Write-off configuration does not match the receivable legal entity.");
			var periodId = await ResolvePeriodAsync(transaction, configuration, normalized.PostingDate, token);
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.WriteOffPostingProfileId, token) ?? throw new InvalidOperationException("Write-off posting profile was not found.");
			var rateId = normalized.ExchangeRateId ?? await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, item.Currency, normalized.PostingDate, token);
			var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
			{
				OperationId = normalized.OperationId,
				PostingProfileId = profile.Id,
				AccountingPeriodId = periodId,
				PostingDate = normalized.PostingDate,
				Description = $"Write-off {item.SourceReference ?? item.SourceId}: {normalized.Reason}",
				SourceId = normalized.OperationId.ToString("D"),
				SourceReference = item.SourceReference,
				TransactionCurrency = item.Currency,
				ExchangeRateId = rateId,
				Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal) { [FinanceReceivablePostingAmountKeys.WriteOff] = normalized.Amount },
				Dimensions = normalized.Dimensions
			}, user.Id, token);
			if (await _receivables.UpdateOpenItemRemainingAsync(transaction, item.Id, item.Version, item.RemainingAmount - normalized.Amount, false, token) != 1) throw new ConcurrencyConflictException("receivable open item");
			var now = DateTime.UtcNow;
			var value = new FinanceReceivableWriteOff { OperationId = normalized.OperationId, RequestHash = requestHash, OpenItemId = item.Id, Amount = normalized.Amount, PostingDate = normalized.PostingDate, Reason = normalized.Reason, JournalEntryId = journal.Id, CreatedAtUtc = now, CreatedByUserId = user.Id };
			var id = await _receivables.CreateWriteOffAsync(transaction, value, token);
			var created = value with { Id = id, Version = 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceReceivableWriteOff> ReverseWriteOffAsync(long writeOffId, FinanceReceivableReversalRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceReceivableWriteOffsReverse);
		var user = RequireUser();
		ValidateReversal(request);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var writeOff = await _receivables.LockWriteOffAsync(transaction, writeOffId, token) ?? throw new InvalidOperationException("Write-off was not found.");
			if (writeOff.IsReversed)
			{
				if (writeOff.ReversalOperationId == request.OperationId) return writeOff;
				throw new InvalidOperationException("Write-off has already been reversed.");
			}

			var item = await _receivables.LockOpenItemAsync(transaction, writeOff.OpenItemId, token) ?? throw new InvalidOperationException("Written-off receivable was not found.");
			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.WriteOffPostingProfileId, token) ?? throw new InvalidOperationException("Write-off posting profile was not found.");
			var periodId = await ResolvePeriodAsync(transaction, configuration, request.PostingDate, token);
			var reversal = await _generalLedger.ReverseInTransactionAsync(transaction, writeOff.JournalEntryId, request.OperationId, periodId, request.PostingDate, profile.NumberSequenceCode, request.Reason, user.Id, token);
			if (await _receivables.UpdateOpenItemRemainingAsync(transaction, item.Id, item.Version, item.RemainingAmount + writeOff.Amount, false, token) != 1) throw new ConcurrencyConflictException("receivable open item");
			var now = DateTime.UtcNow;
			if (await _receivables.MarkWriteOffReversedAsync(transaction, writeOff, writeOff.Version, request.OperationId, reversal.Id, now, user.Id, token) != 1) throw new ConcurrencyConflictException("receivable write-off");
			var after = writeOff with { Version = writeOff.Version + 1, IsReversed = true, ReversalOperationId = request.OperationId, ReversalJournalEntryId = reversal.Id, ReversedAtUtc = now, ReversedByUserId = user.Id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(writeOff.Id, "Reversed", writeOff, after), token);
			return after;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceDunningPolicy>> GetDunningPoliciesAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceDunningView);
		return _receivables.GetDunningPoliciesAsync(cancellationToken);
	}

	public async Task<FinanceDunningPolicy> SaveDunningPolicyAsync(FinanceDunningPolicy policy, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(policy);
		_authorization.RequirePermission(ApplicationPermission.FinanceDunningManage);
		RequireUser();
		var normalized = NormalizePolicy(policy);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var configuration = await RequireActiveConfigurationAsync(transaction, token);
			if (normalized.LegalEntityId != configuration.LegalEntityId) throw new InvalidOperationException("Dunning policy must belong to the configured Accounts Receivable legal entity.");
			if (normalized.Id == 0)
			{
				var id = await _receivables.CreateDunningPolicyAsync(transaction, normalized, token);
				var created = await _receivables.GetDunningPolicyAsync(transaction, id, token) ?? throw new InvalidOperationException("Dunning policy could not be reloaded.");
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}

			var before = await _receivables.GetDunningPolicyAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Dunning policy was not found.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("dunning policy");
			if (await _receivables.UpdateDunningPolicyAsync(transaction, normalized, before.Version, token) != 1) throw new ConcurrencyConflictException("dunning policy");
			var after = await _receivables.GetDunningPolicyAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Dunning policy could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceDunningRun> RunDunningAsync(FinanceDunningRunRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceDunningManage);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty || request.PolicyId <= 0) throw new ArgumentException("Dunning run requires an operation ID and policy.", nameof(request));
		var hash = HashDunning(request);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _receivables.FindDunningRunByOperationAsync(transaction, request.OperationId, token);
			if (existing is not null)
			{
				if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal)) throw new InvalidOperationException("The dunning operation ID is already assigned to different run content.");
				return existing;
			}

			var policy = await _receivables.GetDunningPolicyAsync(transaction, request.PolicyId, token) ?? throw new InvalidOperationException("Dunning policy was not found.");
			if (!policy.IsActive) throw new InvalidOperationException("Dunning policy is inactive.");
			var candidates = await _receivables.GetDunningCandidatesAsync(transaction, policy.LegalEntityId, request.AsOfDate, token);
			var levels = policy.Levels.OrderBy(level => level.MinimumDaysOverdue).ToArray();
			var lines = new List<FinanceDunningRunLine>();
			foreach (var item in candidates)
			{
				var days = request.AsOfDate.DayNumber - item.DueDate.DayNumber;
				var level = levels.LastOrDefault(value => value.MinimumDaysOverdue <= days);
				if (level is null) continue;
				lines.Add(new FinanceDunningRunLine { OpenItemId = item.Id, CustomerId = item.CustomerId, CustomerName = item.CustomerName, Currency = item.Currency, OutstandingAmount = item.RemainingAmount, DaysOverdue = days, LevelNumber = level.LevelNumber, LevelCode = level.Code });
			}

			var run = new FinanceDunningRun { OperationId = request.OperationId, RequestHash = hash, PolicyId = policy.Id, AsOfDate = request.AsOfDate, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id, Lines = lines };
			var id = await _receivables.CreateDunningRunAsync(transaction, run, token);
			var created = run with { Id = id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
			return created;
		}, cancellationToken);
	}

	internal async Task<FinanceReceivableOpenItem?> TryPostSalesInvoiceAsync(DatabaseTransactionContext transaction, SalesInvoice invoice, long userId, CancellationToken cancellationToken)
	{
		var configuration = await TryGetActiveConfigurationAsync(transaction, cancellationToken);
		if (configuration is null) return null;
		var sourceId = invoice.Id.ToString(CultureInfo.InvariantCulture);
		var existing = await _receivables.FindOpenItemBySourceAsync(transaction, FinanceReceivableSourceTypes.SalesInvoice, sourceId, cancellationToken);
		if (existing is not null) return existing;
		return await PostSalesSourceAsync(
			transaction,
			configuration,
			configuration.InvoicePostingProfileId,
			FinanceReceivableOpenItemKind.Invoice,
			sourceId,
			invoice.InvoiceNumber,
			invoice.CustomerId,
			DateOnly.FromDateTime(invoice.InvoiceDate),
			DateOnly.FromDateTime(invoice.DueDate),
			new CurrencyCode(invoice.Currency),
			invoice.NetAmount,
			invoice.TaxAmount,
			invoice.GrossAmount,
			DeterministicOperationId($"AR:{FinanceReceivableSourceTypes.SalesInvoice}:{sourceId}:Posted"),
			userId,
			cancellationToken);
	}

	internal async Task<FinanceReceivableOpenItem?> TryPostSalesCreditNoteAsync(DatabaseTransactionContext transaction, SalesCreditNote creditNote, SalesInvoice invoice, long userId, CancellationToken cancellationToken)
	{
		var configuration = await TryGetActiveConfigurationAsync(transaction, cancellationToken);
		if (configuration is null) return null;
		var sourceId = creditNote.Id.ToString(CultureInfo.InvariantCulture);
		var existing = await _receivables.FindOpenItemBySourceAsync(transaction, FinanceReceivableSourceTypes.SalesCreditNote, sourceId, cancellationToken);
		if (existing is not null) return existing;
		var operationId = DeterministicOperationId($"AR:{FinanceReceivableSourceTypes.SalesCreditNote}:{sourceId}:Posted");
		var creditDate = DateOnly.FromDateTime(creditNote.CreditDate);
		var credit = await PostSalesSourceAsync(
			transaction,
			configuration,
			configuration.CreditNotePostingProfileId,
			FinanceReceivableOpenItemKind.CreditNote,
			sourceId,
			creditNote.CreditNoteNumber,
			creditNote.CustomerId,
			creditDate,
			creditDate,
			new CurrencyCode(invoice.Currency),
			creditNote.NetAmount,
			creditNote.TaxAmount,
			creditNote.GrossAmount,
			operationId,
			userId,
			cancellationToken);
		var invoiceOpenItem = await _receivables.FindOpenItemBySourceAsync(transaction, FinanceReceivableSourceTypes.SalesInvoice, invoice.Id.ToString(CultureInfo.InvariantCulture), cancellationToken);
		if (invoiceOpenItem is not null && invoiceOpenItem.RemainingAmount > 0m && credit.RemainingAmount > 0m)
		{
			var amount = Math.Min(invoiceOpenItem.RemainingAmount, credit.RemainingAmount);
			credit = await ApplyAllocationsAsync(transaction, operationId, credit, [new FinanceReceivableAllocationRequest(invoiceOpenItem.Id, amount)], creditDate, userId, cancellationToken);
		}
		return credit;
	}

	private async Task<FinanceReceivableOpenItem> PostSalesSourceAsync(
		DatabaseTransactionContext transaction,
		FinanceReceivablesConfiguration configuration,
		long profileId,
		FinanceReceivableOpenItemKind kind,
		string sourceId,
		string reference,
		long customerId,
		DateOnly documentDate,
		DateOnly dueDate,
		CurrencyCode currency,
		decimal net,
		decimal tax,
		decimal gross,
		Guid operationId,
		long userId,
		CancellationToken cancellationToken)
	{
		var periodId = await ResolvePeriodAsync(transaction, configuration, documentDate, cancellationToken);
		var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, profileId, cancellationToken) ?? throw new InvalidOperationException("Accounts Receivable source posting profile was not found.");
		var rateId = await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, currency, documentDate, cancellationToken);
		var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
		{
			OperationId = operationId,
			PostingProfileId = profile.Id,
			AccountingPeriodId = periodId,
			PostingDate = documentDate,
			Description = $"{kind} {reference}",
			SourceId = sourceId,
			SourceReference = reference,
			TransactionCurrency = currency,
			ExchangeRateId = rateId,
			Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal)
			{
				[FinanceReceivablePostingAmountKeys.Gross] = gross,
				[FinanceReceivablePostingAmountKeys.Net] = net,
				[FinanceReceivablePostingAmountKeys.Tax] = tax
			}
		}, userId, cancellationToken);
		var value = new FinanceReceivableOpenItem
		{
			LegalEntityId = configuration.LegalEntityId,
			AccountingBookId = journal.AccountingBookId,
			CustomerId = customerId,
			Kind = kind,
			SourceType = kind == FinanceReceivableOpenItemKind.Invoice ? FinanceReceivableSourceTypes.SalesInvoice : FinanceReceivableSourceTypes.SalesCreditNote,
			SourceId = sourceId,
			SourceReference = reference,
			DocumentDate = documentDate,
			DueDate = dueDate,
			Currency = currency,
			OriginalAmount = gross,
			RemainingAmount = gross,
			JournalEntryId = journal.Id,
			OperationId = operationId,
			CreatedAtUtc = DateTime.UtcNow,
			CreatedByUserId = userId
		};
		var id = await _receivables.CreateOpenItemAsync(transaction, value, cancellationToken);
		var created = value with { Id = id, Version = 1 };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), cancellationToken);
		return created;
	}

	private async Task<FinanceReceivableOpenItem> ApplyAllocationsAsync(DatabaseTransactionContext transaction, Guid operationId, FinanceReceivableOpenItem credit, IReadOnlyList<FinanceReceivableAllocationRequest> allocations, DateOnly date, long userId, CancellationToken cancellationToken)
	{
		var total = allocations.Sum(value => value.Amount);
		if (total <= 0m || total > credit.RemainingAmount) throw new InvalidOperationException("Allocation total must be positive and cannot exceed the available credit.");
		if (allocations.Select(value => value.DebitOpenItemId).Distinct().Count() != allocations.Count) throw new InvalidOperationException("A debit open item can appear only once in an allocation operation.");
		foreach (var allocation in allocations)
		{
			if (allocation.Amount <= 0m) throw new InvalidOperationException("Allocation amounts must be positive.");
			var debit = await _receivables.LockOpenItemAsync(transaction, allocation.DebitOpenItemId, cancellationToken) ?? throw new InvalidOperationException("Debit open item was not found.");
			if (debit.Direction != FinanceReceivableDirection.Debit || debit.IsVoided) throw new InvalidOperationException("Allocations can settle only active debit receivables.");
			if (debit.CustomerId != credit.CustomerId || debit.Currency != credit.Currency || debit.AccountingBookId != credit.AccountingBookId || debit.LegalEntityId != credit.LegalEntityId) throw new InvalidOperationException("Allocation debit and credit must belong to the same customer, currency, accounting book and legal entity.");
			if (allocation.Amount > debit.RemainingAmount) throw new InvalidOperationException("Allocation amount exceeds the debit open item's remaining balance.");
			if (await _receivables.UpdateOpenItemRemainingAsync(transaction, debit.Id, debit.Version, debit.RemainingAmount - allocation.Amount, false, cancellationToken) != 1) throw new ConcurrencyConflictException("receivable open item");
			await _receivables.CreateAllocationAsync(transaction, new FinanceReceivableAllocation { OperationId = operationId, DebitOpenItemId = debit.Id, CreditOpenItemId = credit.Id, Amount = allocation.Amount, AllocationDate = date, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = userId }, cancellationToken);
		}
		if (await _receivables.UpdateOpenItemRemainingAsync(transaction, credit.Id, credit.Version, credit.RemainingAmount - total, false, cancellationToken) != 1) throw new ConcurrencyConflictException("receivable open item");
		return credit with { Version = credit.Version + 1, RemainingAmount = credit.RemainingAmount - total };
	}

	private async Task ValidateConfigurationAsync(DatabaseTransactionContext transaction, FinanceReceivablesConfiguration configuration, CancellationToken cancellationToken)
	{
		if (configuration.LegalEntityId == Guid.Empty || configuration.FiscalCalendarId == Guid.Empty) throw new ArgumentException("Accounts Receivable configuration requires a legal entity and fiscal calendar.", nameof(configuration));
		if (configuration.InvoicePostingProfileId <= 0 || configuration.CreditNotePostingProfileId <= 0 || configuration.PaymentPostingProfileId <= 0 || configuration.WriteOffPostingProfileId <= 0) throw new ArgumentException("All Accounts Receivable posting profiles are required.", nameof(configuration));
		var calendar = await _receivables.GetFiscalCalendarAsync(transaction, configuration.FiscalCalendarId, cancellationToken) ?? throw new InvalidOperationException("Accounts Receivable fiscal calendar was not found.");
		if (!calendar.IsActive || calendar.LegalEntityId != configuration.LegalEntityId) throw new InvalidOperationException("Accounts Receivable fiscal calendar must be active and belong to the configured legal entity.");
		var profiles = new[]
		{
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.InvoicePostingProfileId, cancellationToken), FinanceReceivableSourceTypes.SalesInvoice, FinanceReceivablePostingAmountKeys.Gross),
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.CreditNotePostingProfileId, cancellationToken), FinanceReceivableSourceTypes.SalesCreditNote, FinanceReceivablePostingAmountKeys.Gross),
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.PaymentPostingProfileId, cancellationToken), FinanceReceivableSourceTypes.Payment, FinanceReceivablePostingAmountKeys.Payment),
			(await _generalLedger.GetPostingProfileInTransactionAsync(transaction, configuration.WriteOffPostingProfileId, cancellationToken), FinanceReceivableSourceTypes.WriteOff, FinanceReceivablePostingAmountKeys.WriteOff)
		};
		Guid? bookId = null;
		foreach (var (profile, sourceType, amountKey) in profiles)
		{
			if (profile is null || !profile.IsActive) throw new InvalidOperationException($"Accounts Receivable posting profile for '{sourceType}' is missing or inactive.");
			if (profile.LegalEntityId != configuration.LegalEntityId) throw new InvalidOperationException("All Accounts Receivable posting profiles must belong to the configured legal entity.");
			if (!string.Equals(profile.SourceType, sourceType, StringComparison.Ordinal) || !string.Equals(profile.SourceEvent, "Posted", StringComparison.Ordinal)) throw new InvalidOperationException($"Posting profile '{profile.Code}' must use source '{sourceType}' and event 'Posted'.");
			if (!profile.Lines.Any(line => string.Equals(line.AmountKey, amountKey, StringComparison.Ordinal))) throw new InvalidOperationException($"Posting profile '{profile.Code}' does not contain required amount key '{amountKey}'.");
			bookId ??= profile.AccountingBookId;
			if (bookId != profile.AccountingBookId) throw new InvalidOperationException("All Accounts Receivable posting profiles must use the same accounting book.");
		}
	}

	private async Task<FinanceReceivablesConfiguration?> TryGetActiveConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken)
	{
		var configurations = await _receivables.GetActiveConfigurationsAsync(transaction, cancellationToken);
		if (configurations.Count == 0) return null;
		if (configurations.Count > 1) throw new InvalidOperationException("Accounts Receivable source posting is ambiguous because more than one active configuration exists.");
		return configurations[0];
	}

	private async Task<FinanceReceivablesConfiguration> RequireActiveConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		await TryGetActiveConfigurationAsync(transaction, cancellationToken) ?? throw new InvalidOperationException("Accounts Receivable is not configured and active.");

	private async Task<Guid> ResolvePeriodAsync(DatabaseTransactionContext transaction, FinanceReceivablesConfiguration configuration, DateOnly date, CancellationToken cancellationToken)
	{
		var periods = await _receivables.FindOpenPeriodsAsync(transaction, configuration.FiscalCalendarId, date, cancellationToken);
		if (periods.Count == 0) throw new InvalidOperationException($"No open accounting period contains {date:yyyy-MM-dd} in the Accounts Receivable fiscal calendar.");
		if (periods.Count > 1) throw new InvalidOperationException($"More than one open accounting period contains {date:yyyy-MM-dd}; period resolution must be unambiguous.");
		return periods[0].Id;
	}

	private static FinanceReceivablePaymentRequest NormalizePayment(FinanceReceivablePaymentRequest request)
	{
		if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(request));
		if (request.CustomerId <= 0) throw new ArgumentOutOfRangeException(nameof(request));
		if (request.Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(request), "Payment amount must be positive.");
		if (request.Allocations.Any(value => value.Amount <= 0m)) throw new InvalidOperationException("Payment allocations must be positive.");
		if (request.Allocations.Sum(value => value.Amount) > request.Amount) throw new InvalidOperationException("Payment allocations cannot exceed the payment amount.");
		return request with { Description = FinanceValidation.Required(request.Description, nameof(request.Description), 500), Reference = Optional(request.Reference, 200), Allocations = request.Allocations.ToArray(), Dimensions = request.Dimensions.ToArray() };
	}

	private static FinanceReceivableWriteOffRequest NormalizeWriteOff(FinanceReceivableWriteOffRequest request)
	{
		if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(request));
		if (request.OpenItemId <= 0) throw new ArgumentOutOfRangeException(nameof(request));
		if (request.Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(request), "Write-off amount must be positive.");
		return request with { Reason = FinanceValidation.Required(request.Reason, nameof(request.Reason), 500), Dimensions = request.Dimensions.ToArray() };
	}

	private static FinanceDunningPolicy NormalizePolicy(FinanceDunningPolicy policy)
	{
		if (policy.LegalEntityId == Guid.Empty) throw new ArgumentException("Dunning policy requires a legal entity.", nameof(policy));
		if (policy.Levels.Count == 0) throw new InvalidOperationException("Dunning policy requires at least one level.");
		var levels = policy.Levels.OrderBy(value => value.LevelNumber).Select(value => value with { Code = FinanceValidation.Required(value.Code, nameof(value.Code), 50).ToUpperInvariant(), Name = FinanceValidation.Required(value.Name, nameof(value.Name), 200) }).ToArray();
		if (levels.Any(value => value.LevelNumber <= 0 || value.MinimumDaysOverdue < 0)) throw new InvalidOperationException("Dunning level number must be positive and days overdue cannot be negative.");
		if (levels.Select(value => value.LevelNumber).Distinct().Count() != levels.Length || levels.Select(value => value.MinimumDaysOverdue).Distinct().Count() != levels.Length) throw new InvalidOperationException("Dunning level numbers and overdue thresholds must be unique.");
		return policy with { Code = FinanceValidation.Required(policy.Code, nameof(policy.Code), 50).ToUpperInvariant(), Name = FinanceValidation.Required(policy.Name, nameof(policy.Name), 200), Levels = levels };
	}

	private static void ValidateReversal(FinanceReceivableReversalRequest request)
	{
		if (request.OperationId == Guid.Empty) throw new ArgumentException("A reversal operation ID is required.", nameof(request));
		FinanceValidation.Required(request.Reason, nameof(request.Reason), 500);
	}

	private static string HashPayment(FinanceReceivablePaymentRequest request)
	{
		var builder = new StringBuilder();
		Append(builder, request.CustomerId.ToString(CultureInfo.InvariantCulture));
		Append(builder, request.Currency.Value);
		Append(builder, request.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		Append(builder, request.Amount.ToString("G29", CultureInfo.InvariantCulture));
		Append(builder, request.Reference);
		Append(builder, request.Description);
		Append(builder, request.ExchangeRateId?.ToString("D"));
		foreach (var allocation in request.Allocations.OrderBy(value => value.DebitOpenItemId))
		{
			Append(builder, allocation.DebitOpenItemId.ToString(CultureInfo.InvariantCulture));
			Append(builder, allocation.Amount.ToString("G29", CultureInfo.InvariantCulture));
		}
		foreach (var dimension in request.Dimensions.OrderBy(value => value.DimensionId))
		{
			Append(builder, dimension.DimensionId.ToString("D"));
			Append(builder, dimension.DimensionValueId.ToString("D"));
		}
		return Hash(builder);
	}

	private static string HashWriteOff(FinanceReceivableWriteOffRequest request)
	{
		var builder = new StringBuilder();
		Append(builder, request.OpenItemId.ToString(CultureInfo.InvariantCulture));
		Append(builder, request.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		Append(builder, request.Amount.ToString("G29", CultureInfo.InvariantCulture));
		Append(builder, request.Reason);
		Append(builder, request.ExchangeRateId?.ToString("D"));
		foreach (var dimension in request.Dimensions.OrderBy(value => value.DimensionId))
		{
			Append(builder, dimension.DimensionId.ToString("D"));
			Append(builder, dimension.DimensionValueId.ToString("D"));
		}
		return Hash(builder);
	}

	private static string HashAllocations(long creditOpenItemId, DateOnly date, IReadOnlyList<FinanceReceivableAllocationRequest> allocations)
	{
		var builder = new StringBuilder();
		Append(builder, creditOpenItemId.ToString(CultureInfo.InvariantCulture));
		Append(builder, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		foreach (var allocation in allocations.OrderBy(value => value.DebitOpenItemId))
		{
			Append(builder, allocation.DebitOpenItemId.ToString(CultureInfo.InvariantCulture));
			Append(builder, allocation.Amount.ToString("G29", CultureInfo.InvariantCulture));
		}
		return Hash(builder);
	}

	private static string HashDunning(FinanceDunningRunRequest request)
	{
		var builder = new StringBuilder();
		Append(builder, request.PolicyId.ToString(CultureInfo.InvariantCulture));
		Append(builder, request.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		return Hash(builder);
	}

	private static string Hash(StringBuilder builder) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	private static void Append(StringBuilder builder, string? value) => builder.Append(value ?? "<null>").Append('\u001f');
	private static Guid DeterministicOperationId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
	private static string? Optional(string? value, int maximumLength)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var normalized = value.Trim();
		if (normalized.Length > maximumLength) throw new ArgumentException($"Value cannot exceed {maximumLength} characters.");
		return normalized;
	}
	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for Accounts Receivable operations.");
}
