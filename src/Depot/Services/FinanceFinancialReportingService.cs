// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceFinancialReportingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceFinancialReportingRepository _reports;
	private readonly FinanceFinancialReportingInventoryRepository _inventory;
	private readonly FinanceAccountsReceivableService _receivables;
	private readonly FinanceAccountsPayableService _payables;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceFinancialReportingService(IDatabaseTransactionRunner transactions, FinanceFinancialReportingRepository reports, FinanceFinancialReportingInventoryRepository inventory, FinanceAccountsReceivableService receivables, FinanceAccountsPayableService payables, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_reports = reports;
		_inventory = inventory;
		_receivables = receivables;
		_payables = payables;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingManage);
	public bool CanExport => _authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingExport);
	public bool CanCreateSnapshots => _authorization.HasPermission(ApplicationPermission.FinanceReportSnapshotsCreate);

	public Task<IReadOnlyList<FinanceReportingAccountMapping>> GetMappingsAsync(Guid accountingBookId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFinancialReportingView);
		if (accountingBookId == Guid.Empty) throw new ArgumentException("Accounting book is required.", nameof(accountingBookId));
		return _reports.GetMappingsAsync(accountingBookId, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceReportingAccountRecord>> GetAccountsAsync(Guid accountingBookId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFinancialReportingManage);
		if (accountingBookId == Guid.Empty) throw new ArgumentException("Accounting book is required.", nameof(accountingBookId));
		return _reports.GetAccountsAsync(accountingBookId, cancellationToken);
	}

	public async Task<FinanceReportingAccountMapping> SaveMappingAsync(FinanceReportingAccountMapping mapping, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(mapping);
		_authorization.RequirePermission(ApplicationPermission.FinanceFinancialReportingManage);
		RequireUser();
		if (mapping.AccountingBookId == Guid.Empty || mapping.AccountId == Guid.Empty) throw new ArgumentException("Accounting book and account are required.", nameof(mapping));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var book = await RequireBookAsync(transaction, mapping.AccountingBookId, token);
			var account = await _reports.GetAccountAsync(transaction, book.Id, mapping.AccountId, token) ?? throw new InvalidOperationException("Reporting account does not belong to the selected accounting book.");
			ValidateMapping(mapping, account);
			var existingForAccount = await _reports.FindMappingAsync(transaction, mapping.AccountingBookId, mapping.AccountId, token);
			if (mapping.Id == 0)
			{
				if (existingForAccount is not null) throw new InvalidOperationException("A reporting mapping already exists for this account.");
				var id = await _reports.CreateMappingAsync(transaction, mapping, token);
				var created = mapping with { Id = id, Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
				return created;
			}
			var before = await _reports.GetMappingAsync(transaction, mapping.Id, token) ?? throw new InvalidOperationException("Reporting mapping was not found.");
			if (before.AccountingBookId != mapping.AccountingBookId || before.AccountId != mapping.AccountId) throw new InvalidOperationException("Reporting mapping identity cannot be changed.");
			if (before.Version != mapping.Version) throw new ConcurrencyConflictException("finance reporting mapping");
			if (await _reports.UpdateMappingAsync(transaction, mapping, before.Version, token) != 1) throw new ConcurrencyConflictException("finance reporting mapping");
			var after = mapping with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceReportResult> GenerateAsync(FinanceReportParameters parameters, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		_authorization.RequirePermission(ApplicationPermission.FinanceFinancialReportingView);
		ValidateParameters(parameters);
		var generatedAt = DateTime.UtcNow;
		return parameters.Kind switch
		{
			FinanceReportKind.AccountsReceivableAging => await GenerateReceivableAgingAsync(parameters, generatedAt, cancellationToken),
			FinanceReportKind.AccountsPayableAging => await GeneratePayableAgingAsync(parameters, generatedAt, cancellationToken),
			_ => await _transactions.ExecuteAsync((transaction, token) => GenerateInTransactionAsync(transaction, parameters, generatedAt, token), cancellationToken)
		};
	}

	public async Task<FinanceReportSnapshot> CreateSnapshotAsync(Guid operationId, FinanceReportResult result, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(result);
		_authorization.RequirePermission(ApplicationPermission.FinanceReportSnapshotsCreate);
		var user = RequireUser();
		if (operationId == Guid.Empty) throw new ArgumentException("Snapshot operation ID is required.", nameof(operationId));
		var csv = ExportCsvCore(result);
		var parameterHash = HashParameters(result.Parameters);
		var contentHash = Hash(csv);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await RequireBookAsync(transaction, result.Parameters.AccountingBookId, token);
			var existing = await _reports.FindSnapshotByOperationAsync(transaction, operationId, token);
			if (existing is not null)
			{
				if (!string.Equals(existing.ParameterHash, parameterHash, StringComparison.Ordinal) || !string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal)) throw new InvalidOperationException("Snapshot operation ID is already assigned to different report content.");
				return existing;
			}
			var value = new FinanceReportSnapshot { OperationId = operationId, Kind = result.Parameters.Kind, AccountingBookId = result.Parameters.AccountingBookId, FromDate = result.Parameters.FromDate, ToDate = result.Parameters.ToDate, AsOfDate = result.Parameters.AsOfDate, DimensionId = result.Parameters.DimensionId, DimensionValueId = result.Parameters.DimensionValueId, ParameterHash = parameterHash, ContentHash = contentHash, ContentCsv = csv, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id };
			var id = await _reports.CreateSnapshotAsync(transaction, value, token);
			var created = value with { Id = id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceReportSnapshot>> GetRecentSnapshotsAsync(Guid? accountingBookId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFinancialReportingView);
		return _reports.GetRecentSnapshotsAsync(accountingBookId, 50, cancellationToken);
	}

	public string ExportCsv(FinanceReportResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		_authorization.RequirePermission(ApplicationPermission.FinanceFinancialReportingExport);
		return ExportCsvCore(result);
	}

	private async Task<FinanceReportResult> GenerateInTransactionAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var book = await RequireBookAsync(transaction, parameters.AccountingBookId, cancellationToken);
		return parameters.Kind switch
		{
			FinanceReportKind.TrialBalance => BuildTrialBalance(parameters, book, await GetTrialBalanceAsync(transaction, parameters, cancellationToken), generatedAt),
			FinanceReportKind.GeneralLedger => BuildGeneralLedger(parameters, book, await _reports.GetGeneralLedgerAsync(transaction, parameters, cancellationToken), generatedAt),
			FinanceReportKind.BalanceSheet => await BuildBalanceSheetAsync(transaction, parameters, book, generatedAt, cancellationToken),
			FinanceReportKind.ProfitLoss => await BuildProfitLossAsync(transaction, parameters, book, generatedAt, cancellationToken),
			FinanceReportKind.CashFlow => await BuildCashFlowAsync(transaction, parameters, book, generatedAt, cancellationToken),
			FinanceReportKind.TaxSummary => await BuildTaxSummaryAsync(transaction, parameters, book, generatedAt, cancellationToken),
			FinanceReportKind.InventoryValuation => await BuildInventoryValuationAsync(transaction, parameters, book, generatedAt, cancellationToken),
			FinanceReportKind.CostOfGoodsSold => await BuildCostOfGoodsSoldAsync(transaction, parameters, book, generatedAt, cancellationToken),
			_ => throw new NotSupportedException($"Finance report '{parameters.Kind}' is not supported in the ledger transaction path.")
		};
	}

	private Task<IReadOnlyList<FinanceTrialBalanceSourceRow>> GetTrialBalanceAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, CancellationToken cancellationToken)
	{
		var normalized = parameters.FromDate.HasValue ? parameters : parameters with { FromDate = DateOnly.MinValue };
		return _reports.GetTrialBalanceAsync(transaction, normalized, cancellationToken);
	}

	private static FinanceReportResult BuildTrialBalance(FinanceReportParameters parameters, FinanceReportingBookRecord book, IReadOnlyList<FinanceTrialBalanceSourceRow> source, DateTime generatedAt)
	{
		var rows = source.Select(value => new FinanceReportRow { Key = value.AccountId.ToString("D"), Group = value.AccountType.ToString(), Label = $"{value.AccountNumber} {value.AccountName}", Currency = book.ReportingCurrency.Value, AccountNumber = value.AccountNumber, AccountName = value.AccountName, OpeningBalance = value.Opening, Debit = value.Debit, Credit = value.Credit, Balance = value.Opening + value.Debit - value.Credit, Amount = value.Opening + value.Debit - value.Credit }).Where(value => parameters.IncludeZeroBalances || value.OpeningBalance != 0m || value.Debit != 0m || value.Credit != 0m).ToArray();
		return Result(parameters, book, rows, [], generatedAt);
	}

	private static FinanceReportResult BuildGeneralLedger(FinanceReportParameters parameters, FinanceReportingBookRecord book, IReadOnlyList<FinanceGeneralLedgerSourceRow> source, DateTime generatedAt)
	{
		var rows = source.Select(value => new FinanceReportRow { Key = $"{value.JournalEntryId}:{value.AccountId:D}", Group = value.AccountNumber, Label = value.Description, Currency = book.ReportingCurrency.Value, AccountNumber = value.AccountNumber, AccountName = value.AccountName, Date = value.PostingDate, Reference = value.SourceReference ?? value.EntryNumber, Source = $"{value.SourceType}/{value.SourceEvent}/{value.SourceId}", Debit = value.Debit, Credit = value.Credit, Amount = value.Debit - value.Credit, Balance = value.Debit - value.Credit }).ToArray();
		return Result(parameters, book, rows, [], generatedAt);
	}

	private async Task<FinanceReportResult> BuildBalanceSheetAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, FinanceReportingBookRecord book, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var asOf = parameters.AsOfDate ?? parameters.ToDate!.Value;
		var source = await GetTrialBalanceAsync(transaction, parameters with { FromDate = DateOnly.MinValue, ToDate = asOf, AsOfDate = asOf }, cancellationToken);
		var mappings = (await _reports.GetMappingsAsync(transaction, book.Id, cancellationToken)).ToDictionary(value => value.AccountId);
		var warnings = new List<string>();
		var rows = new List<FinanceReportRow>();
		foreach (var value in source.Where(value => value.AccountType is FinanceAccountType.Asset or FinanceAccountType.Liability or FinanceAccountType.Equity))
		{
			var raw = value.Opening + value.Debit - value.Credit;
			if (!parameters.IncludeZeroBalances && raw == 0m) continue;
			var amount = value.AccountType == FinanceAccountType.Asset ? raw : -raw;
			var section = mappings.TryGetValue(value.AccountId, out var mapping) && mapping.StatementSection != FinanceStatementSection.Unclassified ? mapping.StatementSection : DefaultSection(value.AccountType);
			if (!mappings.ContainsKey(value.AccountId) && amount != 0m) warnings.Add($"Account {value.AccountNumber} has no explicit financial-statement mapping; a broad account-type section was used.");
			rows.Add(new FinanceReportRow { Key = value.AccountId.ToString("D"), Group = section.ToString(), Label = $"{value.AccountNumber} {value.AccountName}", Currency = book.ReportingCurrency.Value, AccountNumber = value.AccountNumber, AccountName = value.AccountName, Amount = amount, Balance = amount });
		}
		return Result(parameters, book, rows, warnings.Distinct().ToArray(), generatedAt);
	}

	private async Task<FinanceReportResult> BuildProfitLossAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, FinanceReportingBookRecord book, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var source = await GetTrialBalanceAsync(transaction, parameters, cancellationToken);
		var mappings = (await _reports.GetMappingsAsync(transaction, book.Id, cancellationToken)).ToDictionary(value => value.AccountId);
		var warnings = new List<string>();
		var rows = new List<FinanceReportRow>();
		foreach (var value in source.Where(value => value.AccountType is FinanceAccountType.Revenue or FinanceAccountType.Expense))
		{
			var amount = value.AccountType == FinanceAccountType.Revenue ? value.Credit - value.Debit : value.Debit - value.Credit;
			if (!parameters.IncludeZeroBalances && amount == 0m) continue;
			var section = mappings.TryGetValue(value.AccountId, out var mapping) && mapping.StatementSection != FinanceStatementSection.Unclassified ? mapping.StatementSection : value.AccountType == FinanceAccountType.Revenue ? FinanceStatementSection.Revenue : FinanceStatementSection.OperatingExpenses;
			if (!mappings.ContainsKey(value.AccountId) && amount != 0m) warnings.Add($"Account {value.AccountNumber} has no explicit profit/loss mapping; a broad account-type section was used.");
			rows.Add(new FinanceReportRow { Key = value.AccountId.ToString("D"), Group = section.ToString(), Label = $"{value.AccountNumber} {value.AccountName}", Currency = book.ReportingCurrency.Value, AccountNumber = value.AccountNumber, AccountName = value.AccountName, Debit = value.Debit, Credit = value.Credit, Amount = amount, Balance = amount });
		}
		return Result(parameters, book, rows, warnings.Distinct().ToArray(), generatedAt);
	}

	private async Task<FinanceReportResult> BuildCashFlowAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, FinanceReportingBookRecord book, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var source = await _reports.GetCashFlowSourceAsync(transaction, parameters, cancellationToken);
		var rows = new List<FinanceReportRow>();
		var warnings = new List<string>();
		foreach (var entry in source.GroupBy(value => value.JournalEntryId))
		{
			var cashNet = entry.Where(value => value.IsCashAccount).Sum(value => value.Debit - value.Credit);
			if (cashNet == 0m) continue;
			var counterparts = entry.Where(value => !value.IsCashAccount).ToArray();
			if (counterparts.Length == 0) { warnings.Add($"Journal {entry.Key} changes mapped cash without a non-cash counterpart classification."); continue; }
			foreach (var counterpart in counterparts)
			{
				var amount = -(counterpart.Debit - counterpart.Credit);
				var category = counterpart.Category;
				if (category == FinanceCashFlowCategory.None) warnings.Add($"Account {counterpart.AccountNumber} has no cash-flow category.");
				rows.Add(new FinanceReportRow { Key = $"{entry.Key}:{counterpart.AccountId:D}", Group = category == FinanceCashFlowCategory.None ? "Unclassified" : category.ToString(), Label = $"{counterpart.AccountNumber} {counterpart.AccountName}", Currency = book.ReportingCurrency.Value, AccountNumber = counterpart.AccountNumber, AccountName = counterpart.AccountName, Amount = amount, Balance = amount });
			}
		}
		return Result(parameters, book, rows, warnings.Distinct().ToArray(), generatedAt);
	}

	private async Task<FinanceReportResult> BuildTaxSummaryAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, FinanceReportingBookRecord book, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var source = await GetTrialBalanceAsync(transaction, parameters, cancellationToken);
		var mappings = (await _reports.GetMappingsAsync(transaction, book.Id, cancellationToken)).Where(value => value.TaxCategory != FinanceTaxReportCategory.None).ToDictionary(value => value.AccountId);
		var rows = source.Where(value => mappings.ContainsKey(value.AccountId)).Select(value => new FinanceReportRow { Key = value.AccountId.ToString("D"), Group = mappings[value.AccountId].TaxCategory.ToString(), Label = $"{value.AccountNumber} {value.AccountName}", Currency = book.ReportingCurrency.Value, AccountNumber = value.AccountNumber, AccountName = value.AccountName, Debit = value.Debit, Credit = value.Credit, Amount = value.Debit - value.Credit, Balance = value.Debit - value.Credit }).ToArray();
		var warnings = mappings.Count == 0 ? new[] { "No tax-report mappings are configured. Generic Finance does not infer tax accounts from names or account numbers." } : [];
		return Result(parameters, book, rows, warnings, generatedAt);
	}

	private async Task<FinanceReportResult> BuildInventoryValuationAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, FinanceReportingBookRecord book, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var asOf = parameters.AsOfDate ?? parameters.ToDate!.Value;
		var values = await _inventory.GetValuationAsync(transaction, book.Id, asOf, cancellationToken);
		var items = (await _reports.GetItemsAsync(transaction, values.Select(value => value.ItemId).ToArray(), cancellationToken)).ToDictionary(value => value.ItemId);
		var rows = values.Select(value => { var item = items.GetValueOrDefault(value.ItemId); return new FinanceReportRow { Key = value.ItemId.ToString(CultureInfo.InvariantCulture), Group = "Inventory", Label = item == default ? $"Item {value.ItemId}" : $"{item.ItemNumber} {item.ItemName}", Currency = book.ReportingCurrency.Value, Quantity = value.Quantity, Amount = value.ReportingValue, Balance = value.ReportingValue }; }).Where(value => parameters.IncludeZeroBalances || value.Quantity != 0 || value.Amount != 0m).ToArray();
		return Result(parameters, book, rows, [], generatedAt);
	}

	private async Task<FinanceReportResult> BuildCostOfGoodsSoldAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, FinanceReportingBookRecord book, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var source = await GetTrialBalanceAsync(transaction, parameters, cancellationToken);
		var mappings = (await _reports.GetMappingsAsync(transaction, book.Id, cancellationToken)).Where(value => value.IsCostOfGoodsSold).ToDictionary(value => value.AccountId);
		var rows = source.Where(value => mappings.ContainsKey(value.AccountId)).Select(value => new FinanceReportRow { Key = value.AccountId.ToString("D"), Group = "CostOfGoodsSold", Label = $"{value.AccountNumber} {value.AccountName}", Currency = book.ReportingCurrency.Value, AccountNumber = value.AccountNumber, AccountName = value.AccountName, Debit = value.Debit, Credit = value.Credit, Amount = value.Debit - value.Credit, Balance = value.Debit - value.Credit }).ToArray();
		var warnings = mappings.Count == 0 ? new[] { "No Cost of Goods Sold account mappings are configured." } : [];
		return Result(parameters, book, rows, warnings, generatedAt);
	}

	private async Task<FinanceReportResult> GenerateReceivableAgingAsync(FinanceReportParameters parameters, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var book = await _transactions.ExecuteAsync((transaction, token) => RequireBookAsync(transaction, parameters.AccountingBookId, token), cancellationToken);
		var asOf = parameters.AsOfDate ?? parameters.ToDate!.Value;
		var source = await _receivables.GetAgingAsync(asOf, cancellationToken);
		var rows = source.Select(value => new FinanceReportRow { Key = value.CustomerId.ToString(CultureInfo.InvariantCulture), Group = "AccountsReceivable", Label = value.CustomerName, Currency = value.Currency.Value, Current = value.Current, Days1To30 = value.Days1To30, Days31To60 = value.Days31To60, Days61To90 = value.Days61To90, Over90 = value.DaysOver90, Credits = value.UnappliedCredits, Amount = value.NetExposure, Balance = value.NetExposure }).ToArray();
		return Result(parameters, book, rows, ["Accounts Receivable aging is presented in each open item's transaction currency; F6 does not invent historical FX conversion for the subledger."], generatedAt);
	}

	private async Task<FinanceReportResult> GeneratePayableAgingAsync(FinanceReportParameters parameters, DateTime generatedAt, CancellationToken cancellationToken)
	{
		var book = await _transactions.ExecuteAsync((transaction, token) => RequireBookAsync(transaction, parameters.AccountingBookId, token), cancellationToken);
		var asOf = parameters.AsOfDate ?? parameters.ToDate!.Value;
		var source = await _payables.GetAgingAsync(asOf, cancellationToken);
		var rows = source.Select(value => new FinanceReportRow { Key = value.SupplierId.ToString(CultureInfo.InvariantCulture), Group = "AccountsPayable", Label = value.SupplierName, Currency = value.Currency.Value, Current = value.Current, Days1To30 = value.Days1To30, Days31To60 = value.Days31To60, Days61To90 = value.Days61To90, Over90 = value.DaysOver90, Credits = value.UnappliedDebits, Amount = value.NetExposure, Balance = value.NetExposure }).ToArray();
		return Result(parameters, book, rows, ["Accounts Payable aging is presented in each open item's transaction currency; F6 does not invent historical FX conversion for the subledger."], generatedAt);
	}

	private async Task<FinanceReportingBookRecord> RequireBookAsync(DatabaseTransactionContext transaction, Guid bookId, CancellationToken cancellationToken)
	{
		if (bookId == Guid.Empty) throw new ArgumentException("Accounting book is required.");
		var book = await _reports.GetBookAsync(transaction, bookId, cancellationToken) ?? throw new InvalidOperationException("Accounting book was not found.");
		if (!book.IsActive) throw new InvalidOperationException("Accounting book is inactive.");
		return book;
	}

	private static FinanceReportResult Result(FinanceReportParameters parameters, FinanceReportingBookRecord book, IReadOnlyList<FinanceReportRow> rows, IReadOnlyList<string> warnings, DateTime generatedAt) => new() { Parameters = parameters, ReportingCurrency = book.ReportingCurrency, Rows = rows, Warnings = warnings, GeneratedAtUtc = generatedAt };
	private static FinanceStatementSection DefaultSection(FinanceAccountType type) => type switch { FinanceAccountType.Asset => FinanceStatementSection.CurrentAssets, FinanceAccountType.Liability => FinanceStatementSection.CurrentLiabilities, FinanceAccountType.Equity => FinanceStatementSection.Equity, FinanceAccountType.Revenue => FinanceStatementSection.Revenue, FinanceAccountType.Expense => FinanceStatementSection.OperatingExpenses, _ => FinanceStatementSection.Unclassified };

	private static void ValidateMapping(FinanceReportingAccountMapping mapping, FinanceReportingAccountRecord account)
	{
		if (!account.IsActive && mapping.IsActive) throw new InvalidOperationException("An inactive account cannot have an active reporting mapping.");
		if (mapping.IsCashAccount && mapping.CashFlowCategory != FinanceCashFlowCategory.None) throw new InvalidOperationException("Cash accounts must not classify themselves as operating, investing or financing counterpart accounts.");
		if (mapping.IsCostOfGoodsSold && account.AccountType != FinanceAccountType.Expense) throw new InvalidOperationException("Cost of Goods Sold mapping requires an expense account.");
		var validSection = mapping.StatementSection == FinanceStatementSection.Unclassified || account.AccountType switch
		{
			FinanceAccountType.Asset => mapping.StatementSection is FinanceStatementSection.CurrentAssets or FinanceStatementSection.NonCurrentAssets,
			FinanceAccountType.Liability => mapping.StatementSection is FinanceStatementSection.CurrentLiabilities or FinanceStatementSection.NonCurrentLiabilities,
			FinanceAccountType.Equity => mapping.StatementSection == FinanceStatementSection.Equity,
			FinanceAccountType.Revenue => mapping.StatementSection is FinanceStatementSection.Revenue or FinanceStatementSection.OtherIncomeExpense,
			FinanceAccountType.Expense => mapping.StatementSection is FinanceStatementSection.CostOfGoodsSold or FinanceStatementSection.OperatingExpenses or FinanceStatementSection.OtherIncomeExpense,
			_ => false
		};
		if (!validSection) throw new InvalidOperationException("Financial-statement section is incompatible with the account type.");
	}

	private static void ValidateParameters(FinanceReportParameters value)
	{
		if (value.AccountingBookId == Guid.Empty) throw new ArgumentException("Accounting book is required.", nameof(value));
		if (value.DimensionId.HasValue != value.DimensionValueId.HasValue) throw new ArgumentException("Dimension and dimension value must be supplied together.", nameof(value));
		var periodRequired = value.Kind is FinanceReportKind.GeneralLedger or FinanceReportKind.ProfitLoss or FinanceReportKind.CashFlow or FinanceReportKind.TaxSummary or FinanceReportKind.CostOfGoodsSold;
		if (periodRequired && (!value.FromDate.HasValue || !value.ToDate.HasValue)) throw new ArgumentException($"{value.Kind} requires from and to dates.", nameof(value));
		if (value.FromDate.HasValue && value.ToDate.HasValue && value.ToDate < value.FromDate) throw new ArgumentException("Report end date must be on or after start date.", nameof(value));
		var asOfRequired = value.Kind is FinanceReportKind.BalanceSheet or FinanceReportKind.AccountsReceivableAging or FinanceReportKind.AccountsPayableAging or FinanceReportKind.InventoryValuation;
		if (asOfRequired && !value.AsOfDate.HasValue && !value.ToDate.HasValue) throw new ArgumentException($"{value.Kind} requires an as-of date.", nameof(value));
		if (value.Kind == FinanceReportKind.TrialBalance && !value.ToDate.HasValue && !value.AsOfDate.HasValue) throw new ArgumentException("Trial Balance requires an end or as-of date.", nameof(value));
	}

	private static string ExportCsvCore(FinanceReportResult result)
	{
		var builder = new StringBuilder();
		builder.AppendLine("Key,Group,Label,Currency,AccountNumber,AccountName,Date,Reference,Source,OpeningBalance,Debit,Credit,Balance,Amount,Quantity,Current,Days1To30,Days31To60,Days61To90,Over90,Credits");
		foreach (var row in result.Rows)
		{
			var values = new[] { row.Key, row.Group, row.Label, row.Currency, row.AccountNumber, row.AccountName, row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Reference, row.Source, Number(row.OpeningBalance), Number(row.Debit), Number(row.Credit), Number(row.Balance), Number(row.Amount), row.Quantity.ToString(CultureInfo.InvariantCulture), Number(row.Current), Number(row.Days1To30), Number(row.Days31To60), Number(row.Days61To90), Number(row.Over90), Number(row.Credits) };
			builder.AppendLine(string.Join(',', values.Select(Csv)));
		}
		return builder.ToString();
	}

	private static string HashParameters(FinanceReportParameters value) => Hash(string.Join('|', (int)value.Kind, value.AccountingBookId.ToString("D"), value.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.AsOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.DimensionId?.ToString("D"), value.DimensionValueId?.ToString("D"), value.IncludeZeroBalances));
	private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
	private static string Number(decimal value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
	private static string Csv(string? value) { var text = value ?? string.Empty; return text.IndexOfAny([',','"','\r','\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text; }
	private User RequireUser() => _authorization.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated user is required.");
}
