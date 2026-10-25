// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceGeneralLedgerService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceGeneralLedgerRepository _ledger;
	private readonly FinancePostingProfileRepository _profiles;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceGeneralLedgerService(
		IDatabaseTransactionRunner transactions,
		FinanceGeneralLedgerRepository ledger,
		FinancePostingProfileRepository profiles,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_ledger = ledger;
		_profiles = profiles;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerView);
	public bool CanPost => _authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerPost);
	public bool CanPostManualJournal => _authorization.HasPermission(ApplicationPermission.FinanceManualJournalsPost);
	public bool CanReverse => _authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerReverse);
	public bool CanManagePostingProfiles => _authorization.HasPermission(ApplicationPermission.FinancePostingProfilesManage);
	public bool CanViewPeriods => _authorization.HasPermission(ApplicationPermission.FinancePeriodsView);
	public bool CanManagePeriods => _authorization.HasPermission(ApplicationPermission.FinancePeriodsManage);

	public Task<FinanceJournalEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceGeneralLedgerView);
		return _ledger.GetByIdAsync(id, cancellationToken);
	}

	public Task<PageResult<FinanceJournalEntrySummary>> SearchAsync(
		Guid? accountingBookId = null,
		DateOnly? fromDate = null,
		DateOnly? toDate = null,
		string? sourceType = null,
		int pageNumber = 1,
		int pageSize = 100,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceGeneralLedgerView);
		if (fromDate.HasValue && toDate.HasValue && toDate.Value < fromDate.Value) throw new ArgumentException("The end date must be on or after the start date.", nameof(toDate));
		return _ledger.SearchAsync(accountingBookId, fromDate, toDate, sourceType, pageNumber, pageSize, cancellationToken);
	}

	public Task<IReadOnlyList<AccountingPeriod>> GetPeriodsForDateAsync(DateOnly date, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePeriodsView);
		return _ledger.GetPeriodsForDateAsync(date, cancellationToken);
	}

	public async Task<AccountingPeriod> SetPeriodStatusAsync(
		Guid periodId,
		AccountingPeriodStatus status,
		CancellationToken cancellationToken = default)
	{
		if (periodId == Guid.Empty) throw new ArgumentException("An accounting period is required.", nameof(periodId));
		if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
		_authorization.RequirePermission(ApplicationPermission.FinancePeriodsManage);
		RequireUser();

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _ledger.LockPeriodAsync(transaction, periodId, token)
				?? throw new InvalidOperationException("Accounting period was not found.");
			if (before.Status != status &&
				await _ledger.UpdatePeriodStatusAsync(transaction, periodId, before.Status, status, token) != 1)
				throw new ConcurrencyConflictException("accounting period");

			var after = await _ledger.LockPeriodAsync(transaction, periodId, token)
				?? throw new InvalidOperationException("Accounting period could not be reloaded.");
			return new AccountingPeriod(after.Id, after.FiscalCalendarId, after.Code, after.StartDate, after.EndDate, after.Status);
		}, cancellationToken);
	}

	public Task<FinancePostingProfile?> GetPostingProfileAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePostingProfilesView);
		return _profiles.GetByIdAsync(id, cancellationToken);
	}

	public Task<IReadOnlyList<FinancePostingProfile>> GetPostingProfilesAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePostingProfilesView);
		return _profiles.GetAllAsync(cancellationToken);
	}

	internal Task<FinancePostingProfile?> GetPostingProfileInTransactionAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		_profiles.GetByIdAsync(transaction, id, cancellationToken);

	internal async Task<Guid?> ResolveExchangeRateIdForProfileAsync(
		DatabaseTransactionContext transaction,
		long postingProfileId,
		CurrencyCode transactionCurrency,
		DateOnly postingDate,
		CancellationToken cancellationToken)
	{
		var profile = await _profiles.GetByIdAsync(transaction, postingProfileId, cancellationToken) ?? throw new InvalidOperationException("Posting profile was not found.");
		var book = await _ledger.GetBookAsync(transaction, profile.AccountingBookId, cancellationToken) ?? throw new InvalidOperationException("Accounting book was not found.");
		if (transactionCurrency == book.ReportingCurrency) return null;
		var rate = await _ledger.FindLatestExchangeRateAsync(transaction, transactionCurrency, book.ReportingCurrency, postingDate, cancellationToken);
		return rate?.Id ?? throw new InvalidOperationException($"No persisted exchange rate is available for {transactionCurrency.Value}->{book.ReportingCurrency.Value} on or before {postingDate:yyyy-MM-dd}.");
	}

	public Task<FinancePostingFlowContext> GetPostingFlowContextAsync(
		Guid accountingBookId,
		Guid journalId,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePostingProfilesView);
		if (accountingBookId == Guid.Empty) throw new ArgumentException("Accounting book is required.", nameof(accountingBookId));
		if (journalId == Guid.Empty) throw new ArgumentException("Journal is required.", nameof(journalId));
		return _ledger.GetPostingFlowContextAsync(accountingBookId, journalId, cancellationToken);
	}

	public async Task<FinancePostingProfile> SavePostingProfileAsync(FinancePostingProfile profile, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		_authorization.RequirePermission(ApplicationPermission.FinancePostingProfilesManage);
		RequireUser();
		var normalized = NormalizeProfile(profile);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await ValidateProfileReferencesAsync(transaction, normalized, token);
			if (normalized.Id == 0)
			{
				var id = await _profiles.CreateAsync(transaction, normalized, token);
				var created = await _profiles.GetByIdAsync(transaction, id, token) ?? throw new InvalidOperationException("Posting profile could not be reloaded after creation.");
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}

			var before = await _profiles.GetByIdAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Posting profile was not found.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("finance posting profile");
			if (!await _profiles.UpdateAsync(transaction, normalized, before.Version, token)) throw new ConcurrencyConflictException("finance posting profile");
			var after = await _profiles.GetByIdAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Posting profile could not be reloaded after update.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceJournalEntry> PostAsync(FinancePostingRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceGeneralLedgerPost);
		if (request.EntryKind == FinanceJournalEntryKind.Manual) _authorization.RequirePermission(ApplicationPermission.FinanceManualJournalsPost);
		if (request.EntryKind == FinanceJournalEntryKind.Reversal) throw new InvalidOperationException("Reversal entries must be created through the explicit reversal workflow.");
		var user = RequireUser();
		var normalized = NormalizeRequest(request);
		return await _transactions.ExecuteAsync((transaction, token) => PostCoreAsync(transaction, normalized, user.Id, null, token), cancellationToken);
	}

	public async Task<FinanceJournalEntry> PostFromProfileAsync(FinanceProfilePostingRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceGeneralLedgerPost);
		var user = RequireUser();
		return await _transactions.ExecuteAsync((transaction, token) => PostFromProfileInTransactionAsync(transaction, request, user.Id, token), cancellationToken);
	}

	internal Task<FinanceJournalEntry> PostFromProfileInTransactionAsync(
		DatabaseTransactionContext transaction,
		FinanceProfilePostingRequest request,
		long postedByUserId,
		CancellationToken cancellationToken) =>
		PostFromProfileCoreAsync(transaction, request, postedByUserId, cancellationToken, null, null);

	internal Task<FinanceJournalEntry> PostInventoryCarryingValueInTransactionAsync(
		DatabaseTransactionContext transaction,
		FinanceProfilePostingRequest request,
		CurrencyCode reportingCurrency,
		IReadOnlyDictionary<string, decimal> reportingAmounts,
		long postedByUserId,
		CancellationToken cancellationToken) =>
		PostFromProfileCoreAsync(transaction, request, postedByUserId, cancellationToken, reportingCurrency, reportingAmounts);

	private async Task<FinanceJournalEntry> PostFromProfileCoreAsync(
		DatabaseTransactionContext transaction,
		FinanceProfilePostingRequest request,
		long postedByUserId,
		CancellationToken cancellationToken,
		CurrencyCode? historicalCurrency,
		IReadOnlyDictionary<string, decimal>? historicalAmounts)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(request));
		if (request.PostingProfileId <= 0) throw new ArgumentException("A posting profile is required.", nameof(request));
		var profile = await _profiles.GetByIdAsync(transaction, request.PostingProfileId, cancellationToken) ?? throw new InvalidOperationException("Posting profile was not found.");
		if (!profile.IsActive) throw new InvalidOperationException("The posting profile is inactive.");
		var historical = historicalAmounts is not null;
		var carryingAmounts = historicalAmounts ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
		var historicalLines = new List<FinanceReportingLine>();
		var reportingMinorUnits = 0;
		var transactionMinorUnits = 0;
		if (historical)
		{
			if (profile.SourceType != FinanceInventoryAccountingEvents.SourceType ||
				profile.SourceEvent is not (FinanceInventoryAccountingEvents.SalesShipment or FinanceInventoryAccountingEvents.InventoryAdjustment) ||
				request.ExchangeRateId.HasValue || historicalCurrency is null)
				throw new InvalidOperationException("Historical carrying values are reserved for inventory issues and adjustments.");
			reportingMinorUnits = (await _ledger.GetCurrencyAsync(transaction, historicalCurrency, cancellationToken)
				?? throw new InvalidOperationException("Reporting currency was not found.")).MinorUnits;
			transactionMinorUnits = (await _ledger.GetCurrencyAsync(transaction, request.TransactionCurrency, cancellationToken)
				?? throw new InvalidOperationException("Transaction currency was not found.")).MinorUnits;
			foreach (var key in profile.Lines.Select(line => line.AmountKey).Distinct(StringComparer.Ordinal))
				foreach (var direction in new[] { FinancePostingDirection.Debit, FinancePostingDirection.Credit })
					if (profile.Lines.Where(line => line.AmountKey == key && line.Direction == direction).Sum(line => line.Multiplier) != 1m)
						throw new InvalidOperationException("Inventory carrying-value profiles must allocate each amount fully to both debit and credit accounts.");
		}
		var cumulativeWeights = new Dictionary<(string, FinancePostingDirection), decimal>();
		var lines = new List<FinancePostingLine>();
		foreach (var profileLine in profile.Lines.OrderBy(value => value.LineNumber))
		{
			if (!request.Amounts.TryGetValue(profileLine.AmountKey, out var sourceAmount)) throw new InvalidOperationException($"Posting amount '{profileLine.AmountKey}' is required by profile '{profile.Code}'.");
			if (sourceAmount < 0m) throw new InvalidOperationException($"Posting amount '{profileLine.AmountKey}' cannot be negative.");
			if (historical && profileLine.Multiplier <= 0m) throw new InvalidOperationException("Inventory profile allocation weights must be positive.");
			var amount = sourceAmount * profileLine.Multiplier;
			if (historical)
			{
				if (!carryingAmounts.TryGetValue(profileLine.AmountKey, out var sourceReporting) || sourceReporting < 0m)
					throw new InvalidOperationException("A non-negative historical reporting amount is required for every inventory amount key.");
				ValidateCurrencyPrecision(sourceAmount, transactionMinorUnits, "inventory transaction amount");
				ValidateCurrencyPrecision(sourceReporting, reportingMinorUnits, "inventory reporting amount");
				var key = (profileLine.AmountKey, profileLine.Direction);
				var before = cumulativeWeights.GetValueOrDefault(key);
				var after = before + profileLine.Multiplier;
				amount = RoundCurrency(sourceAmount * after, transactionMinorUnits) - RoundCurrency(sourceAmount * before, transactionMinorUnits);
				var reporting = RoundCurrency(sourceReporting * after, reportingMinorUnits) - RoundCurrency(sourceReporting * before, reportingMinorUnits);
				cumulativeWeights[key] = after;
				historicalLines.Add(new FinanceReportingLine(
					profileLine.Direction == FinancePostingDirection.Debit ? reporting : 0m,
					profileLine.Direction == FinancePostingDirection.Credit ? reporting : 0m));
			}
			else if (amount == 0m) continue;
			lines.Add(new FinancePostingLine
			{
				AccountId = profileLine.AccountId,
				Description = profileLine.Description,
				Debit = profileLine.Direction == FinancePostingDirection.Debit ? amount : 0m,
				Credit = profileLine.Direction == FinancePostingDirection.Credit ? amount : 0m,
				Dimensions = request.Dimensions
			});
		}
		var resolved = NormalizeRequest(new FinancePostingRequest
		{
			OperationId = request.OperationId,
			AccountingBookId = profile.AccountingBookId,
			JournalId = profile.JournalId,
			AccountingPeriodId = request.AccountingPeriodId,
			NumberSequenceCode = profile.NumberSequenceCode,
			PostingDate = request.PostingDate,
			Description = request.Description,
			SourceType = profile.SourceType,
			SourceId = request.SourceId,
			SourceEvent = profile.SourceEvent,
			SourceReference = request.SourceReference,
			TransactionCurrency = request.TransactionCurrency,
			ExchangeRateId = request.ExchangeRateId,
			EntryKind = FinanceJournalEntryKind.Standard,
			Lines = lines
		}, allowZeroAmounts: historical);
		var historicalValues = historical && historicalCurrency is not null
			? new FinanceHistoricalReporting(historicalCurrency, historicalLines)
			: null;
		return await PostCoreAsync(transaction, resolved, postedByUserId, null, cancellationToken, historicalValues);
	}

	public async Task<FinanceJournalEntry> ReverseAsync(
		long originalEntryId,
		Guid operationId,
		Guid accountingPeriodId,
		DateOnly postingDate,
		string numberSequenceCode,
		string reason,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceGeneralLedgerReverse);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(
			(transaction, token) => ReverseCoreAsync(transaction, originalEntryId, operationId, accountingPeriodId, postingDate, numberSequenceCode, reason, user.Id, requireManualJournal: true, token),
			cancellationToken);
	}

	internal Task<FinanceJournalEntry> ReverseInTransactionAsync(
		DatabaseTransactionContext transaction,
		long originalEntryId,
		Guid operationId,
		Guid accountingPeriodId,
		DateOnly postingDate,
		string numberSequenceCode,
		string reason,
		long postedByUserId,
		CancellationToken cancellationToken) =>
		ReverseCoreAsync(transaction, originalEntryId, operationId, accountingPeriodId, postingDate, numberSequenceCode, reason, postedByUserId, requireManualJournal: false, cancellationToken);

	private async Task<FinanceJournalEntry> ReverseCoreAsync(
		DatabaseTransactionContext transaction,
		long originalEntryId,
		Guid operationId,
		Guid accountingPeriodId,
		DateOnly postingDate,
		string numberSequenceCode,
		string reason,
		long postedByUserId,
		bool requireManualJournal,
		CancellationToken cancellationToken)
	{
		if (originalEntryId <= 0) throw new ArgumentOutOfRangeException(nameof(originalEntryId));
		if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
		if (accountingPeriodId == Guid.Empty) throw new ArgumentException("An accounting period is required.", nameof(accountingPeriodId));
		var normalizedSequence = Required(numberSequenceCode, nameof(numberSequenceCode), 50).ToUpperInvariant();
		var normalizedReason = Required(reason, nameof(reason), 500);

		var original = await _ledger.LockEntryAsync(transaction, originalEntryId, cancellationToken) ?? throw new InvalidOperationException("Journal entry was not found.");
		// Only explicitly manual entries have a standalone GL lifecycle. Profile postings
		// belong to their source workflow, including sources introduced by future integrations.
		if (requireManualJournal && original.EntryKind != FinanceJournalEntryKind.Manual)
			throw new InvalidOperationException("Only standalone manual journals can be reversed directly. Reverse this journal through its source workflow.");
		if (original.EntryKind == FinanceJournalEntryKind.Reversal) throw new InvalidOperationException("A reversal entry cannot itself be reversed. Reverse the original business correction through an explicit new workflow.");
		var existingReversalId = await _ledger.GetReversalEntryIdAsync(transaction, originalEntryId, cancellationToken);
		if (existingReversalId.HasValue)
		{
			var existingReversal = await _ledger.GetByIdAsync(transaction, existingReversalId.Value, cancellationToken) ?? throw new InvalidOperationException("The reversal link references a missing journal entry.");
			if (existingReversal.OperationId == operationId) return existingReversal;
			throw new InvalidOperationException("This journal entry has already been reversed.");
		}

		var period = await ValidateBookJournalPeriodAsync(transaction, original.AccountingBookId, original.JournalId, accountingPeriodId, postingDate, cancellationToken);
		var requestHash = HashReversal(original, accountingPeriodId, postingDate, normalizedSequence, normalizedReason);
		var operation = await _ledger.FindByOperationAsync(transaction, operationId, cancellationToken);
		if (operation is not null) return await ResolveExistingAsync(transaction, operation, requestHash, "operation ID", cancellationToken);
		var source = await _ledger.FindBySourceAsync(transaction, original.AccountingBookId, nameof(FinanceJournalEntry), original.Id.ToString(CultureInfo.InvariantCulture), "Reversal", cancellationToken);
		if (source is not null) return await ResolveExistingAsync(transaction, source, requestHash, "source posting", cancellationToken);

		var sequence = await RequireNumberSequenceAsync(transaction, period.LegalEntityId, normalizedSequence, cancellationToken);
		var reversal = new FinanceJournalEntry
		{
			EntryNumber = FormatEntryNumber(sequence),
			OperationId = operationId,
			RequestHash = requestHash,
			AccountingBookId = original.AccountingBookId,
			JournalId = original.JournalId,
			AccountingPeriodId = accountingPeriodId,
			PostingDate = postingDate,
			PostedAtUtc = DateTime.UtcNow,
			PostedByUserId = postedByUserId,
			Description = $"Reversal of {original.EntryNumber}: {normalizedReason}",
			SourceType = nameof(FinanceJournalEntry),
			SourceId = original.Id.ToString(CultureInfo.InvariantCulture),
			SourceEvent = "Reversal",
			SourceReference = original.EntryNumber,
			TransactionCurrency = original.TransactionCurrency,
			ReportingCurrency = original.ReportingCurrency,
			ExchangeRateId = original.ExchangeRateId,
			ExchangeRate = original.ExchangeRate,
			EntryKind = FinanceJournalEntryKind.Reversal,
			ReversalOfEntryId = original.Id,
			Lines = original.Lines.Select((line, index) => new FinanceJournalEntryLine
			{
				LineNumber = index + 1,
				AccountId = line.AccountId,
				Description = line.Description,
				TransactionDebit = line.TransactionCredit,
				TransactionCredit = line.TransactionDebit,
				ReportingDebit = line.ReportingCredit,
				ReportingCredit = line.ReportingDebit,
				Dimensions = line.Dimensions
			}).ToArray()
		};
		if (await _ledger.AdvanceNumberSequenceAsync(transaction, sequence.Id, sequence.NextNumber, cancellationToken) != 1) throw new ConcurrencyConflictException("finance number sequence");
		var reversalId = await _ledger.CreateEntryAsync(transaction, reversal, cancellationToken);
		await _ledger.CreateReversalLinkAsync(transaction, original.Id, reversalId, cancellationToken);
		var created = await _ledger.GetByIdAsync(transaction, reversalId, cancellationToken) ?? throw new InvalidOperationException("Reversal entry could not be reloaded.");
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), cancellationToken);
		await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(original.Id, "Reversed", original, original), cancellationToken);
		return created;
	}

	private async Task<FinanceJournalEntry> PostCoreAsync(DatabaseTransactionContext transaction, FinancePostingRequest request, long postedByUserId, long? reversalOfEntryId, CancellationToken cancellationToken, FinanceHistoricalReporting? historical = null)
	{
		var requestHash = HashRequest(request, historical);
		var operation = await _ledger.FindByOperationAsync(transaction, request.OperationId, cancellationToken);
		if (operation is not null) return await ResolveExistingAsync(transaction, operation, requestHash, "operation ID", cancellationToken);
		var source = await _ledger.FindBySourceAsync(transaction, request.AccountingBookId, request.SourceType, request.SourceId, request.SourceEvent, cancellationToken);
		if (source is not null) return await ResolveExistingAsync(transaction, source, requestHash, "source posting", cancellationToken);

		var period = await ValidateBookJournalPeriodAsync(transaction, request.AccountingBookId, request.JournalId, request.AccountingPeriodId, request.PostingDate, cancellationToken);
		var book = await _ledger.GetBookAsync(transaction, request.AccountingBookId, cancellationToken) ?? throw new InvalidOperationException("Accounting book was not found.");
		var transactionCurrency = await _ledger.GetCurrencyAsync(transaction, request.TransactionCurrency, cancellationToken) ?? throw new InvalidOperationException($"Transaction currency '{request.TransactionCurrency}' is not configured.");
		var reportingCurrency = await _ledger.GetCurrencyAsync(transaction, book.ReportingCurrency, cancellationToken) ?? throw new InvalidOperationException($"Reporting currency '{book.ReportingCurrency}' is not configured.");
		if (!transactionCurrency.IsActive || !reportingCurrency.IsActive) throw new InvalidOperationException("Both transaction and reporting currencies must be active.");

		if (historical is not null && (historical.Currency != book.ReportingCurrency || historical.Lines.Count != request.Lines.Count))
			throw new InvalidOperationException("Inventory carrying-value evidence does not match the accounting book and journal lines.");
		var rate = historical is null
			? await ResolveExchangeRateAsync(transaction, request, book.ReportingCurrency, cancellationToken)
			: new FinanceResolvedRate(null, HistoricalSummaryRate(request.Lines.Sum(line => line.Debit), historical.Lines.Sum(line => line.Debit)));
		var requiredDimensions = await _ledger.GetRequiredDimensionsAsync(transaction, cancellationToken);
		var accountCache = new Dictionary<Guid, FinanceAccount>();
		var dimensionCache = new Dictionary<(Guid DimensionId, Guid ValueId), FinanceDimensionValueRecord>();
		var entryLines = new List<FinanceJournalEntryLine>(request.Lines.Count);
		decimal transactionDebit = 0m;
		decimal transactionCredit = 0m;
		decimal reportingDebit = 0m;
		decimal reportingCredit = 0m;

		for (var index = 0; index < request.Lines.Count; index++)
		{
			var line = request.Lines[index];
			ValidateCurrencyPrecision(line.Debit, transactionCurrency.MinorUnits, $"line {index + 1} debit");
			ValidateCurrencyPrecision(line.Credit, transactionCurrency.MinorUnits, $"line {index + 1} credit");
			if (!accountCache.TryGetValue(line.AccountId, out var account))
			{
				account = await _ledger.GetAccountAsync(transaction, line.AccountId, cancellationToken) ?? throw new InvalidOperationException($"Account '{line.AccountId}' was not found.");
				accountCache.Add(line.AccountId, account);
			}
			if (!account.IsActive) throw new InvalidOperationException($"Account '{account.Number}' is inactive.");
			if (!account.AllowDirectPosting) throw new InvalidOperationException($"Account '{account.Number}' does not allow direct posting.");
			if (account.ChartOfAccountsId != book.ChartOfAccountsId) throw new InvalidOperationException($"Account '{account.Number}' does not belong to the accounting book's chart of accounts.");
			await ValidateDimensionsAsync(transaction, line, requiredDimensions, dimensionCache, cancellationToken);
			var lineReportingDebit = historical?.Lines[index].Debit ?? RoundCurrency(line.Debit * rate.Rate, reportingCurrency.MinorUnits);
			var lineReportingCredit = historical?.Lines[index].Credit ?? RoundCurrency(line.Credit * rate.Rate, reportingCurrency.MinorUnits);
			if (lineReportingDebit < 0m || lineReportingCredit < 0m ||
				(line.Debit > 0m && lineReportingCredit > 0m) || (line.Credit > 0m && lineReportingDebit > 0m))
				throw new InvalidOperationException("Historical reporting line directions do not match their transaction lines.");
			ValidateCurrencyPrecision(lineReportingDebit, reportingCurrency.MinorUnits, $"line {index + 1} reporting debit");
			ValidateCurrencyPrecision(lineReportingCredit, reportingCurrency.MinorUnits, $"line {index + 1} reporting credit");
			transactionDebit += line.Debit;
			transactionCredit += line.Credit;
			reportingDebit += lineReportingDebit;
			reportingCredit += lineReportingCredit;
			entryLines.Add(new FinanceJournalEntryLine
			{
				LineNumber = index + 1,
				AccountId = line.AccountId,
				Description = line.Description,
				TransactionDebit = line.Debit,
				TransactionCredit = line.Credit,
				ReportingDebit = lineReportingDebit,
				ReportingCredit = lineReportingCredit,
				Dimensions = line.Dimensions
			});
		}
		if ((historical is null ? transactionDebit <= 0m : transactionDebit < 0m) || transactionDebit != transactionCredit) throw new InvalidOperationException("Journal entry is not balanced in transaction currency.");
		if ((historical is null ? reportingDebit <= 0m : reportingDebit < 0m) || reportingDebit != reportingCredit) throw new InvalidOperationException("Journal entry is not balanced in reporting currency after currency rounding. Add an explicit configured rounding line instead of relying on an implicit adjustment.");

		var sequence = await RequireNumberSequenceAsync(transaction, period.LegalEntityId, request.NumberSequenceCode, cancellationToken);
		var entry = new FinanceJournalEntry
		{
			EntryNumber = FormatEntryNumber(sequence),
			OperationId = request.OperationId,
			RequestHash = requestHash,
			AccountingBookId = request.AccountingBookId,
			JournalId = request.JournalId,
			AccountingPeriodId = request.AccountingPeriodId,
			PostingDate = request.PostingDate,
			PostedAtUtc = DateTime.UtcNow,
			PostedByUserId = postedByUserId,
			Description = request.Description,
			SourceType = request.SourceType,
			SourceId = request.SourceId,
			SourceEvent = request.SourceEvent,
			SourceReference = request.SourceReference,
			TransactionCurrency = request.TransactionCurrency,
			ReportingCurrency = book.ReportingCurrency,
			ExchangeRateId = rate.ExchangeRateId,
			ExchangeRate = rate.Rate,
			EntryKind = request.EntryKind,
			ReversalOfEntryId = reversalOfEntryId,
			Lines = entryLines
		};
		if (await _ledger.AdvanceNumberSequenceAsync(transaction, sequence.Id, sequence.NextNumber, cancellationToken) != 1) throw new ConcurrencyConflictException("finance number sequence");
		var id = await _ledger.CreateEntryAsync(transaction, entry, cancellationToken);
		var created = await _ledger.GetByIdAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Journal entry could not be reloaded after posting.");
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), cancellationToken);
		return created;
	}

	private async Task<FinancePeriodRecord> ValidateBookJournalPeriodAsync(DatabaseTransactionContext transaction, Guid bookId, Guid journalId, Guid periodId, DateOnly postingDate, CancellationToken cancellationToken)
	{
		var book = await _ledger.GetBookAsync(transaction, bookId, cancellationToken) ?? throw new InvalidOperationException("Accounting book was not found.");
		if (!book.IsActive) throw new InvalidOperationException("Accounting book is inactive.");
		var journal = await _ledger.GetJournalAsync(transaction, journalId, cancellationToken) ?? throw new InvalidOperationException("Journal was not found.");
		if (!journal.IsActive) throw new InvalidOperationException("Journal is inactive.");
		if (journal.AccountingBookId != book.Id) throw new InvalidOperationException("Journal does not belong to the selected accounting book.");
		var period = await _ledger.LockPeriodAsync(transaction, periodId, cancellationToken) ?? throw new InvalidOperationException("Accounting period was not found.");
		if (period.LegalEntityId != book.LegalEntityId) throw new InvalidOperationException("Accounting period does not belong to the accounting book's legal entity.");
		if (period.Status != AccountingPeriodStatus.Open) throw new InvalidOperationException("Accounting period is closed for posting.");
		if (postingDate < period.StartDate || postingDate > period.EndDate) throw new InvalidOperationException("Posting date is outside the selected accounting period.");
		return period;
	}

	private async Task<FinanceResolvedRate> ResolveExchangeRateAsync(DatabaseTransactionContext transaction, FinancePostingRequest request, CurrencyCode reportingCurrency, CancellationToken cancellationToken)
	{
		if (request.TransactionCurrency == reportingCurrency)
		{
			if (request.ExchangeRateId.HasValue) throw new InvalidOperationException("An exchange-rate reference must not be supplied when transaction and reporting currencies are identical.");
			return new FinanceResolvedRate(null, 1m);
		}
		if (!request.ExchangeRateId.HasValue) throw new InvalidOperationException("An exchange-rate reference is required when transaction and reporting currencies differ.");
		var exchangeRate = await _ledger.GetExchangeRateAsync(transaction, request.ExchangeRateId.Value, cancellationToken) ?? throw new InvalidOperationException("Exchange rate was not found.");
		if (exchangeRate.BaseCurrency != request.TransactionCurrency || exchangeRate.QuoteCurrency != reportingCurrency) throw new InvalidOperationException("Exchange rate pair does not match transaction and reporting currencies.");
		if (DateOnly.FromDateTime(exchangeRate.EffectiveAtUtc.UtcDateTime) > request.PostingDate) throw new InvalidOperationException("Exchange rate cannot become effective after the posting date.");
		return new FinanceResolvedRate(exchangeRate.Id, exchangeRate.Rate);
	}

	private async Task ValidateDimensionsAsync(
		DatabaseTransactionContext transaction,
		FinancePostingLine line,
		IReadOnlyList<AccountingDimension> requiredDimensions,
		Dictionary<(Guid DimensionId, Guid ValueId), FinanceDimensionValueRecord> cache,
		CancellationToken cancellationToken)
	{
		var assignments = line.Dimensions.ToDictionary(value => value.DimensionId);
		foreach (var required in requiredDimensions)
			if (!assignments.ContainsKey(required.Id)) throw new InvalidOperationException($"Required accounting dimension '{required.Code}' is missing from a journal line.");
		foreach (var assignment in line.Dimensions)
		{
			var key = (assignment.DimensionId, assignment.DimensionValueId);
			if (!cache.TryGetValue(key, out var value))
			{
				value = await _ledger.GetDimensionValueAsync(transaction, assignment.DimensionId, assignment.DimensionValueId, cancellationToken) ?? throw new InvalidOperationException("Accounting dimension value was not found or does not belong to the selected dimension.");
				cache.Add(key, value);
			}
			if (!value.IsDimensionActive || !value.IsValueActive) throw new InvalidOperationException("Accounting dimension and value must both be active.");
		}
	}

	private async Task<FinanceNumberSequenceRecord> RequireNumberSequenceAsync(DatabaseTransactionContext transaction, Guid legalEntityId, string code, CancellationToken cancellationToken)
	{
		var sequence = await _ledger.LockNumberSequenceAsync(transaction, legalEntityId, code, cancellationToken) ?? throw new InvalidOperationException($"Finance number sequence '{code}' was not found for the legal entity.");
		if (!sequence.IsActive) throw new InvalidOperationException("Finance number sequence is inactive.");
		if (!string.Equals(sequence.DocumentType, FinanceNumberSequenceDocumentTypes.GeneralLedger, StringComparison.Ordinal)) throw new InvalidOperationException($"Number sequence '{sequence.Code}' is not configured for General Ledger entries.");
		return sequence;
	}

	private async Task ValidateProfileReferencesAsync(DatabaseTransactionContext transaction, FinancePostingProfile profile, CancellationToken cancellationToken)
	{
		var book = await _ledger.GetBookAsync(transaction, profile.AccountingBookId, cancellationToken) ?? throw new InvalidOperationException("Accounting book was not found.");
		if (book.LegalEntityId != profile.LegalEntityId) throw new InvalidOperationException("Posting profile legal entity does not match its accounting book.");
		if (!book.IsActive) throw new InvalidOperationException("Accounting book is inactive.");
		var journal = await _ledger.GetJournalAsync(transaction, profile.JournalId, cancellationToken) ?? throw new InvalidOperationException("Journal was not found.");
		if (!journal.IsActive || journal.AccountingBookId != book.Id) throw new InvalidOperationException("Posting profile journal must be active and belong to its accounting book.");
		await RequireNumberSequenceAsync(transaction, profile.LegalEntityId, profile.NumberSequenceCode, cancellationToken);
		foreach (var line in profile.Lines)
		{
			var account = await _ledger.GetAccountAsync(transaction, line.AccountId, cancellationToken) ?? throw new InvalidOperationException($"Posting profile account '{line.AccountId}' was not found.");
			if (!account.IsActive || !account.AllowDirectPosting || account.ChartOfAccountsId != book.ChartOfAccountsId) throw new InvalidOperationException($"Posting profile account '{account.Number}' is not an active directly postable account in the accounting book's chart.");
		}
	}

	private async Task<FinanceJournalEntry> ResolveExistingAsync(DatabaseTransactionContext transaction, FinanceExistingPosting existing, string requestHash, string keyName, CancellationToken cancellationToken)
	{
		if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) throw new InvalidOperationException($"The {keyName} is already assigned to a different finance posting request.");
		return await _ledger.GetByIdAsync(transaction, existing.Id, cancellationToken) ?? throw new InvalidOperationException("The idempotency record references a missing journal entry.");
	}

	private static FinancePostingRequest NormalizeRequest(FinancePostingRequest request, bool allowZeroAmounts = false)
	{
		if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(request));
		if (request.AccountingBookId == Guid.Empty) throw new ArgumentException("An accounting book is required.", nameof(request));
		if (request.JournalId == Guid.Empty) throw new ArgumentException("A journal is required.", nameof(request));
		if (request.AccountingPeriodId == Guid.Empty) throw new ArgumentException("An accounting period is required.", nameof(request));
		if (request.TransactionCurrency is null) throw new ArgumentException("A transaction currency is required.", nameof(request));
		if (request.EntryKind is not (FinanceJournalEntryKind.Standard or FinanceJournalEntryKind.Manual or FinanceJournalEntryKind.Reversal)) throw new ArgumentOutOfRangeException(nameof(request));
		if (request.Lines.Count < 2) throw new InvalidOperationException("A journal entry requires at least two posting lines.");
		var normalizedLines = request.Lines.Select((line, index) =>
		{
			if (line.AccountId == Guid.Empty) throw new InvalidOperationException($"Journal line {index + 1} requires an account.");
			if (line.Debit < 0m || line.Credit < 0m) throw new InvalidOperationException($"Journal line {index + 1} cannot contain a negative debit or credit amount.");
			if ((line.Debit > 0m) == (line.Credit > 0m) && !(allowZeroAmounts && line.Debit == 0m && line.Credit == 0m)) throw new InvalidOperationException($"Journal line {index + 1} must contain exactly one positive debit or credit amount.");
			var dimensions = line.Dimensions ?? [];
			if (dimensions.Any(value => value.DimensionId == Guid.Empty || value.DimensionValueId == Guid.Empty)) throw new InvalidOperationException($"Journal line {index + 1} contains an empty accounting dimension identifier.");
			if (dimensions.Select(value => value.DimensionId).Distinct().Count() != dimensions.Count) throw new InvalidOperationException($"Journal line {index + 1} assigns the same accounting dimension more than once.");
			return line with { Description = Optional(line.Description, 500), Dimensions = dimensions.ToArray() };
		}).ToArray();
		return request with
		{
			NumberSequenceCode = Required(request.NumberSequenceCode, nameof(request.NumberSequenceCode), 50).ToUpperInvariant(),
			Description = Required(request.Description, nameof(request.Description), 500),
			SourceType = Required(request.SourceType, nameof(request.SourceType), 100),
			SourceId = Required(request.SourceId, nameof(request.SourceId), 200),
			SourceEvent = Required(request.SourceEvent, nameof(request.SourceEvent), 100),
			SourceReference = Optional(request.SourceReference, 200),
			Lines = normalizedLines
		};
	}

	private static FinancePostingProfile NormalizeProfile(FinancePostingProfile profile)
	{
		if (profile.Id < 0) throw new ArgumentOutOfRangeException(nameof(profile));
		if (profile.LegalEntityId == Guid.Empty || profile.AccountingBookId == Guid.Empty || profile.JournalId == Guid.Empty) throw new ArgumentException("Posting profile requires a legal entity, accounting book and journal.", nameof(profile));
		if (profile.Lines.Count < 2) throw new InvalidOperationException("A posting profile requires at least two lines.");
		var lines = profile.Lines.Select((line, index) =>
		{
			if (line.AccountId == Guid.Empty) throw new InvalidOperationException($"Posting profile line {index + 1} requires an account.");
			if (line.LineNumber <= 0) throw new InvalidOperationException("Posting profile line numbers must be positive.");
			if (line.Multiplier <= 0m) throw new InvalidOperationException("Posting profile multipliers must be positive.");
			if (!Enum.IsDefined(line.Direction)) throw new InvalidOperationException("Posting profile line direction is invalid.");
			return line with { AmountKey = Required(line.AmountKey, nameof(line.AmountKey), 100), Description = Optional(line.Description, 500) };
		}).OrderBy(value => value.LineNumber).ToArray();
		if (lines.Select(value => value.LineNumber).Distinct().Count() != lines.Length) throw new InvalidOperationException("Posting profile line numbers must be unique.");
		return profile with
		{
			Code = Required(profile.Code, nameof(profile.Code), 50).ToUpperInvariant(),
			Name = Required(profile.Name, nameof(profile.Name), 200),
			SourceType = Required(profile.SourceType, nameof(profile.SourceType), 100),
			SourceEvent = Required(profile.SourceEvent, nameof(profile.SourceEvent), 100),
			NumberSequenceCode = Required(profile.NumberSequenceCode, nameof(profile.NumberSequenceCode), 50).ToUpperInvariant(),
			Lines = lines
		};
	}

	private static string FormatEntryNumber(FinanceNumberSequenceRecord sequence)
	{
		var numeric = sequence.NextNumber.ToString(CultureInfo.InvariantCulture);
		if (numeric.Length > sequence.NumericLength) throw new InvalidOperationException($"Finance number sequence '{sequence.Code}' is exhausted.");
		return string.Concat(sequence.Prefix, numeric.PadLeft(sequence.NumericLength, '0'));
	}

	private static void ValidateCurrencyPrecision(decimal amount, int minorUnits, string name)
	{
		var scale = (decimal.GetBits(amount)[3] >> 16) & 0x7F;
		if (scale > minorUnits && amount != decimal.Round(amount, minorUnits, MidpointRounding.AwayFromZero)) throw new InvalidOperationException($"{name} exceeds the configured currency precision of {minorUnits} minor units.");
	}

	private static decimal RoundCurrency(decimal amount, int minorUnits) => decimal.Round(amount, minorUnits, MidpointRounding.AwayFromZero);

	private static string HashRequest(FinancePostingRequest request, FinanceHistoricalReporting? historical = null)
	{
		var builder = new StringBuilder();
		Append(builder, request.AccountingBookId.ToString("D"));
		Append(builder, request.JournalId.ToString("D"));
		Append(builder, request.AccountingPeriodId.ToString("D"));
		Append(builder, request.NumberSequenceCode);
		Append(builder, request.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		Append(builder, request.Description);
		Append(builder, request.SourceType);
		Append(builder, request.SourceId);
		Append(builder, request.SourceEvent);
		Append(builder, request.SourceReference);
		Append(builder, request.TransactionCurrency.Value);
		Append(builder, request.ExchangeRateId?.ToString("D"));
		Append(builder, ((int)request.EntryKind).ToString(CultureInfo.InvariantCulture));
		foreach (var line in request.Lines)
		{
			Append(builder, line.AccountId.ToString("D"));
			Append(builder, line.Description);
			Append(builder, line.Debit.ToString("G29", CultureInfo.InvariantCulture));
			Append(builder, line.Credit.ToString("G29", CultureInfo.InvariantCulture));
			foreach (var dimension in line.Dimensions.OrderBy(value => value.DimensionId))
			{
				Append(builder, dimension.DimensionId.ToString("D"));
				Append(builder, dimension.DimensionValueId.ToString("D"));
			}
		}
		if (historical is not null)
		{
			Append(builder, "inventory-carrying-value");
			Append(builder, historical.Currency.Value);
			foreach (var line in historical.Lines)
			{
				Append(builder, line.Debit.ToString("G29", CultureInfo.InvariantCulture));
				Append(builder, line.Credit.ToString("G29", CultureInfo.InvariantCulture));
			}
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private static string HashReversal(FinanceJournalEntry original, Guid periodId, DateOnly postingDate, string sequenceCode, string reason)
	{
		var builder = new StringBuilder();
		Append(builder, "reversal");
		Append(builder, original.Id.ToString(CultureInfo.InvariantCulture));
		Append(builder, original.RequestHash);
		Append(builder, periodId.ToString("D"));
		Append(builder, postingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		Append(builder, sequenceCode);
		Append(builder, reason);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private static void Append(StringBuilder builder, string? value) => builder.Append(value ?? "<null>").Append('\u001f');
	private static string Required(string value, string parameterName, int maximumLength) => FinanceValidation.Required(value, parameterName, maximumLength);
	private static string? Optional(string? value, int maximumLength)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var normalized = value.Trim();
		if (normalized.Length > maximumLength) throw new ArgumentException($"Value cannot exceed {maximumLength} characters.");
		return normalized;
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for Finance posting.");

	private static decimal HistoricalSummaryRate(decimal transactionAmount, decimal reportingAmount)
	{
		if (transactionAmount <= 0m || reportingAmount <= 0m) return 1m;
		var summary = decimal.Round(reportingAmount / transactionAmount, 12, MidpointRounding.ToEven);
		return summary > 0m ? summary : 1m;
	}

	private sealed record FinanceReportingLine(decimal Debit, decimal Credit);
	private sealed record FinanceHistoricalReporting(CurrencyCode Currency, IReadOnlyList<FinanceReportingLine> Lines);

	private sealed record FinanceResolvedRate(Guid? ExchangeRateId, decimal Rate);
}
