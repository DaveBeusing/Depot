// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceReportKind { TrialBalance, GeneralLedger, BalanceSheet, ProfitLoss, CashFlow, AccountsReceivableAging, AccountsPayableAging, TaxSummary, InventoryValuation, CostOfGoodsSold }
public enum FinanceStatementSection { Unclassified, CurrentAssets, NonCurrentAssets, CurrentLiabilities, NonCurrentLiabilities, Equity, Revenue, CostOfGoodsSold, OperatingExpenses, OtherIncomeExpense }
public enum FinanceCashFlowCategory { None, Operating, Investing, Financing }
public enum FinanceTaxReportCategory { None, OutputTax, InputTax, TaxPayable, TaxReceivable, OtherTax }

public sealed record FinanceReportingAccountMapping
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public Guid AccountingBookId { get; init; }
	public Guid AccountId { get; init; }
	public FinanceStatementSection StatementSection { get; init; }
	public FinanceCashFlowCategory CashFlowCategory { get; init; }
	public FinanceTaxReportCategory TaxCategory { get; init; }
	public bool IsCashAccount { get; init; }
	public bool IsCostOfGoodsSold { get; init; }
	public int SortOrder { get; init; }
	public bool IsActive { get; init; } = true;
}

public sealed record FinanceReportingAccountRecord(Guid Id, string Number, string Name, FinanceAccountType AccountType, bool IsActive);

public sealed record FinanceReportParameters
{
	public FinanceReportKind Kind { get; init; }
	public Guid AccountingBookId { get; init; }
	public DateOnly? FromDate { get; init; }
	public DateOnly? ToDate { get; init; }
	public DateOnly? AsOfDate { get; init; }
	public Guid? DimensionId { get; init; }
	public Guid? DimensionValueId { get; init; }
	public bool IncludeZeroBalances { get; init; }
}

public sealed record FinanceReportRow
{
	public string Key { get; init; } = string.Empty;
	public string Group { get; init; } = string.Empty;
	public string Label { get; init; } = string.Empty;
	public string? Currency { get; init; }
	public string? AccountNumber { get; init; }
	public string? AccountName { get; init; }
	public DateOnly? Date { get; init; }
	public string? Reference { get; init; }
	public string? Source { get; init; }
	public decimal OpeningBalance { get; init; }
	public decimal Debit { get; init; }
	public decimal Credit { get; init; }
	public decimal Balance { get; init; }
	public decimal Amount { get; init; }
	public int Quantity { get; init; }
	public decimal Current { get; init; }
	public decimal Days1To30 { get; init; }
	public decimal Days31To60 { get; init; }
	public decimal Days61To90 { get; init; }
	public decimal Over90 { get; init; }
	public decimal Credits { get; init; }
}

public sealed record FinanceReportResult
{
	public FinanceReportParameters Parameters { get; init; } = new();
	public CurrencyCode ReportingCurrency { get; init; } = new("USD");
	public IReadOnlyList<FinanceReportRow> Rows { get; init; } = [];
	public IReadOnlyList<string> Warnings { get; init; } = [];
	public DateTime GeneratedAtUtc { get; init; }
	public long? SnapshotId { get; init; }
	public string? ContentHash { get; init; }
}

public sealed record FinanceReportSnapshot
{
	public long Id { get; init; }
	public Guid OperationId { get; init; }
	public FinanceReportKind Kind { get; init; }
	public Guid AccountingBookId { get; init; }
	public DateOnly? FromDate { get; init; }
	public DateOnly? ToDate { get; init; }
	public DateOnly? AsOfDate { get; init; }
	public Guid? DimensionId { get; init; }
	public Guid? DimensionValueId { get; init; }
	public string ParameterHash { get; init; } = string.Empty;
	public string ContentHash { get; init; } = string.Empty;
	public string ContentCsv { get; init; } = string.Empty;
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
}

internal sealed record FinanceReportingBookRecord(Guid Id, Guid LegalEntityId, Guid ChartOfAccountsId, CurrencyCode ReportingCurrency, bool IsActive);
internal sealed record FinanceTrialBalanceSourceRow(Guid AccountId, string AccountNumber, string AccountName, FinanceAccountType AccountType, decimal Opening, decimal Debit, decimal Credit);
internal sealed record FinanceGeneralLedgerSourceRow(long JournalEntryId, DateOnly PostingDate, string EntryNumber, Guid AccountId, string AccountNumber, string AccountName, string Description, string SourceType, string SourceId, string SourceEvent, string? SourceReference, decimal Debit, decimal Credit);
internal sealed record FinanceCashFlowSourceRow(long JournalEntryId, Guid AccountId, string AccountNumber, string AccountName, bool IsCashAccount, FinanceCashFlowCategory Category, decimal Debit, decimal Credit);
internal sealed record FinanceInventoryValuationSourceRow(long ItemId, int Quantity, decimal ReportingValue);
