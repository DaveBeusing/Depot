// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceBudgetStatus
{
	Draft = 1,
	PendingApproval = 2,
	Approved = 3,
	Locked = 4,
	Superseded = 5,
	Archived = 6
}

public enum FinanceBudgetSourceKind
{
	Manual = 1,
	PriorYear = 2,
	VersionCopy = 3,
	CsvImport = 4
}

public sealed record FinanceBudgetVersion
{
	public Guid Id { get; init; }
	public long Version { get; init; } = 1;
	public Guid LegalEntityId { get; init; }
	public Guid AccountingBookId { get; init; }
	public Guid FiscalCalendarId { get; init; }
	public int FiscalYear { get; init; }
	public string Name { get; init; } = string.Empty;
	public int BudgetVersionNumber { get; init; } = 1;
	public CurrencyCode Currency { get; init; } = new("USD");
	public FinanceBudgetStatus Status { get; init; } = FinanceBudgetStatus.Draft;
	public long OwnerUserId { get; init; }
	public string? Description { get; init; }
	public FinanceBudgetSourceKind SourceKind { get; init; } = FinanceBudgetSourceKind.Manual;
	public Guid? SourceBudgetVersionId { get; init; }
	public Guid? ApprovalInstanceId { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public DateTime UpdatedAtUtc { get; init; }
	public long UpdatedByUserId { get; init; }

	public bool IsEditable => Status == FinanceBudgetStatus.Draft;
	public bool IsImmutable => Status is FinanceBudgetStatus.Approved or FinanceBudgetStatus.Locked or FinanceBudgetStatus.Superseded or FinanceBudgetStatus.Archived;
}

public sealed record FinanceBudgetLine
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public Guid BudgetVersionId { get; init; }
	public Guid AccountId { get; init; }
	public Guid AccountingPeriodId { get; init; }
	public Guid? DimensionId { get; init; }
	public Guid? DimensionValueId { get; init; }
	public decimal Amount { get; init; }
	public string? SourceEvidence { get; init; }
}

public sealed record FinanceBudgetListFilter(
	Guid? LegalEntityId = null,
	Guid? AccountingBookId = null,
	int? FiscalYear = null,
	FinanceBudgetStatus? Status = null,
	string? SearchText = null);

public sealed record FinanceBudgetImportRow(
	int RowNumber,
	Guid AccountId,
	Guid AccountingPeriodId,
	Guid? DimensionId,
	Guid? DimensionValueId,
	decimal Amount,
	string? SourceEvidence,
	IReadOnlyList<string> Errors)
{
	public bool IsValid => Errors.Count == 0;
}

public sealed record FinanceBudgetImportPreview(
	IReadOnlyList<FinanceBudgetImportRow> Rows,
	int ValidRowCount,
	int InvalidRowCount,
	int DuplicateRowCount)
{
	public bool CanApply => Rows.Count > 0 && InvalidRowCount == 0 && DuplicateRowCount == 0;
}

public sealed record FinanceBudgetVarianceRow(
	Guid AccountId,
	string AccountNumber,
	string AccountName,
	Guid AccountingPeriodId,
	string PeriodCode,
	Guid? DimensionId,
	Guid? DimensionValueId,
	decimal Actual,
	decimal Budget)
{
	public decimal Variance => Actual - Budget;
	public decimal? VariancePercent => Budget == 0m ? null : Variance / Math.Abs(Budget);
}

public sealed record FinanceBudgetSummary(
	Guid BudgetVersionId,
	decimal TotalBudget,
	int LineCount,
	IReadOnlyList<FinanceBudgetSummaryPeriod> Periods);

public sealed record FinanceBudgetSummaryPeriod(Guid AccountingPeriodId, string PeriodCode, decimal BudgetAmount, int LineCount);

public sealed record FinanceBudgetOption(Guid Id, string Code, string Name);
public sealed record FinanceBudgetPeriodOption(Guid Id, string Code, DateOnly StartDate, DateOnly EndDate);
public sealed record FinanceBudgetAccountOption(Guid Id, string Number, string Name, FinanceAccountType AccountType);
public sealed record FinanceBudgetDimensionOption(Guid Id, string Code, string Name);
public sealed record FinanceBudgetDimensionValueOption(Guid Id, Guid DimensionId, string Code, string Name);
