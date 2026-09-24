// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ProjectStatus
{
	Draft = 1,
	Active = 2,
	OnHold = 3,
	Closed = 4,
	Cancelled = 5
}

public enum ProjectPhaseStatus
{
	Planned = 1,
	Active = 2,
	Completed = 3,
	Cancelled = 4
}

public enum ProjectAttributionEntityKind
{
	PurchaseOrder = 1,
	SupplierDocument = 2,
	SalesOrder = 3,
	SalesInvoice = 4,
	JournalEntry = 5
}

public sealed record ProjectRecord
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public Guid LegalEntityId { get; init; }
	public long OwnerUserId { get; init; }
	public long? CustomerId { get; init; }
	public DateOnly? PlannedStartDate { get; init; }
	public DateOnly? PlannedEndDate { get; init; }
	public ProjectStatus Status { get; init; } = ProjectStatus.Draft;
	public string? Description { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public DateTime UpdatedAtUtc { get; init; }
	public long UpdatedByUserId { get; init; }
	public DateTime? ClosedAtUtc { get; init; }
	public long? ClosedByUserId { get; init; }
	public DateTime? CancelledAtUtc { get; init; }
	public long? CancelledByUserId { get; init; }

	public bool AcceptsOperationalActivity => Status is ProjectStatus.Draft or ProjectStatus.Active or ProjectStatus.OnHold;
}

public sealed record ProjectPhase
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public long ProjectId { get; init; }
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public DateOnly? PlannedStartDate { get; init; }
	public DateOnly? PlannedEndDate { get; init; }
	public ProjectPhaseStatus Status { get; init; } = ProjectPhaseStatus.Planned;
	public string? Description { get; init; }
}

public sealed record ProjectAttribution
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public long ProjectId { get; init; }
	public long? ProjectPhaseId { get; init; }
	public ProjectAttributionEntityKind EntityKind { get; init; }
	public long EntityId { get; init; }
	public string? SourceType { get; init; }
	public string? SourceId { get; init; }
	public long? JournalEntryId { get; init; }
	public bool IsImmutable { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
}

public sealed record ProjectAttributionRequest(
	long ProjectId,
	long? ProjectPhaseId,
	ProjectAttributionEntityKind EntityKind,
	long EntityId);

public sealed record ProjectBudgetLink
{
	public long Id { get; init; }
	public long ProjectId { get; init; }
	public long? ProjectPhaseId { get; init; }
	public long FinanceBudgetLineId { get; init; }
	public string? CategoryCode { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
}

public sealed record ProjectBudgetLineOption(
	long FinanceBudgetLineId,
	long BudgetVersionId,
	string BudgetName,
	int FiscalYear,
	string AccountNumber,
	string AccountName,
	string PeriodCode,
	decimal Amount);

public sealed record ProjectActualRow(
	long JournalEntryId,
	string EntryNumber,
	DateOnly PostingDate,
	Guid AccountingPeriodId,
	Guid AccountId,
	string AccountNumber,
	string AccountName,
	FinanceAccountType AccountType,
	CurrencyCode ReportingCurrency,
	decimal Amount,
	long? ProjectPhaseId,
	string SourceType,
	string SourceId);

public sealed record ProjectCommitmentRow(
	long PurchaseOrderId,
	string OrderNumber,
	string SupplierName,
	DateTime OrderDate,
	DateTime? ExpectedDeliveryDate,
	long PurchaseOrderLineId,
	string PartNumber,
	string Description,
	int OrderedQuantity,
	int ReceivedQuantity,
	decimal UnitPrice,
	decimal RemainingAmount,
	long? ProjectPhaseId);

public sealed record ProjectBudgetVarianceRow(
	Guid AccountingPeriodId,
	string PeriodCode,
	Guid AccountId,
	string AccountNumber,
	string AccountName,
	long? ProjectPhaseId,
	string? CategoryCode,
	decimal Actual,
	decimal Budget)
{
	public decimal Variance => Actual - Budget;
	public decimal? VariancePercent => Budget == 0m ? null : Variance / Math.Abs(Budget);
}

public sealed record ProjectCurrencyTotals(
	CurrencyCode Currency,
	decimal Revenue,
	decimal Cost)
{
	public decimal Margin => Revenue - Cost;
}

public sealed record ProjectFinancialSummary(
	long ProjectId,
	IReadOnlyList<ProjectCurrencyTotals> CurrencyTotals,
	decimal OpenPurchaseCommitments,
	int ActualEntryCount,
	int CommitmentLineCount);

public sealed record ProjectListFilter(
	Guid? LegalEntityId = null,
	long? OwnerUserId = null,
	ProjectStatus? Status = null,
	string? SearchText = null);
