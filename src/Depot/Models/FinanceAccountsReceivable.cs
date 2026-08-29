// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceReceivableOpenItemKind
{
	Invoice = 1,
	CreditNote = 2,
	Payment = 3
}

public enum FinanceReceivableDirection
{
	Debit = 1,
	Credit = 2
}

public enum FinanceReceivableSettlementStatus
{
	Open = 0,
	PartiallySettled = 1,
	Settled = 2,
	Voided = 3
}

public static class FinanceReceivablePostingAmountKeys
{
	public const string Gross = "Gross";
	public const string Net = "Net";
	public const string Tax = "Tax";
	public const string Payment = "Payment";
	public const string WriteOff = "WriteOff";
}

public static class FinanceReceivableSourceTypes
{
	public const string SalesInvoice = nameof(SalesInvoice);
	public const string SalesCreditNote = nameof(SalesCreditNote);
	public const string Payment = nameof(FinanceReceivablePayment);
	public const string WriteOff = nameof(FinanceReceivableWriteOff);
}

public sealed record FinanceReceivablesConfiguration
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid LegalEntityId { get; init; }
	public required Guid FiscalCalendarId { get; init; }
	public required long InvoicePostingProfileId { get; init; }
	public required long CreditNotePostingProfileId { get; init; }
	public required long PaymentPostingProfileId { get; init; }
	public required long WriteOffPostingProfileId { get; init; }
	public bool IsActive { get; init; } = true;
}

public sealed record FinanceReceivableOpenItem
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid LegalEntityId { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required long CustomerId { get; init; }
	public string? CustomerName { get; init; }
	public FinanceReceivableOpenItemKind Kind { get; init; }
	public required string SourceType { get; init; }
	public required string SourceId { get; init; }
	public string? SourceReference { get; init; }
	public required DateOnly DocumentDate { get; init; }
	public required DateOnly DueDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal OriginalAmount { get; init; }
	public decimal RemainingAmount { get; init; }
	public required long JournalEntryId { get; init; }
	public required Guid OperationId { get; init; }
	public bool IsVoided { get; init; }
	public required DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }

	public FinanceReceivableDirection Direction => Kind == FinanceReceivableOpenItemKind.Invoice ? FinanceReceivableDirection.Debit : FinanceReceivableDirection.Credit;
	public decimal SignedRemainingAmount => Direction == FinanceReceivableDirection.Debit ? RemainingAmount : -RemainingAmount;
	public FinanceReceivableSettlementStatus SettlementStatus => IsVoided
		? FinanceReceivableSettlementStatus.Voided
		: RemainingAmount == 0m
			? FinanceReceivableSettlementStatus.Settled
			: RemainingAmount < OriginalAmount
				? FinanceReceivableSettlementStatus.PartiallySettled
				: FinanceReceivableSettlementStatus.Open;
}

public sealed record FinanceReceivableAllocation
{
	public long Id { get; init; }
	public required Guid OperationId { get; init; }
	public required long DebitOpenItemId { get; init; }
	public required long CreditOpenItemId { get; init; }
	public decimal Amount { get; init; }
	public required DateOnly AllocationDate { get; init; }
	public required DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public bool IsReversed => ReversedAtUtc.HasValue;
}

public sealed record FinanceReceivablePayment
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid OperationId { get; init; }
	public required string RequestHash { get; init; }
	public required long CustomerId { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required DateOnly PaymentDate { get; init; }
	public decimal Amount { get; init; }
	public string? Reference { get; init; }
	public required string Description { get; init; }
	public required long OpenItemId { get; init; }
	public required long JournalEntryId { get; init; }
	public required DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
	public bool IsReversed { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public long? ReversalJournalEntryId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
}

public sealed record FinanceReceivableWriteOff
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid OperationId { get; init; }
	public required string RequestHash { get; init; }
	public required long OpenItemId { get; init; }
	public decimal Amount { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required string Reason { get; init; }
	public required long JournalEntryId { get; init; }
	public required DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
	public bool IsReversed { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public long? ReversalJournalEntryId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
}

public sealed record FinanceReceivableAllocationRequest(long DebitOpenItemId, decimal Amount);

public sealed record FinanceReceivablePaymentRequest
{
	public required Guid OperationId { get; init; }
	public required long CustomerId { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required DateOnly PaymentDate { get; init; }
	public decimal Amount { get; init; }
	public string? Reference { get; init; }
	public required string Description { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
	public IReadOnlyList<FinanceReceivableAllocationRequest> Allocations { get; init; } = [];
}

public sealed record FinanceReceivableWriteOffRequest
{
	public required Guid OperationId { get; init; }
	public required long OpenItemId { get; init; }
	public required DateOnly PostingDate { get; init; }
	public decimal Amount { get; init; }
	public required string Reason { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
}

public sealed record FinanceReceivableReversalRequest
{
	public required Guid OperationId { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required string Reason { get; init; }
}

public sealed record FinanceDunningPolicy
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid LegalEntityId { get; init; }
	public required string Code { get; init; }
	public required string Name { get; init; }
	public bool IsActive { get; init; } = true;
	public IReadOnlyList<FinanceDunningLevel> Levels { get; init; } = [];
}

public sealed record FinanceDunningLevel
{
	public long Id { get; init; }
	public long PolicyId { get; init; }
	public int LevelNumber { get; init; }
	public int MinimumDaysOverdue { get; init; }
	public required string Code { get; init; }
	public required string Name { get; init; }
}

public sealed record FinanceDunningRunRequest
{
	public required Guid OperationId { get; init; }
	public required long PolicyId { get; init; }
	public required DateOnly AsOfDate { get; init; }
}

public sealed record FinanceDunningRun
{
	public long Id { get; init; }
	public required Guid OperationId { get; init; }
	public required string RequestHash { get; init; }
	public required long PolicyId { get; init; }
	public required DateOnly AsOfDate { get; init; }
	public required DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
	public IReadOnlyList<FinanceDunningRunLine> Lines { get; init; } = [];
}

public sealed record FinanceDunningRunLine
{
	public long Id { get; init; }
	public long RunId { get; init; }
	public required long OpenItemId { get; init; }
	public required long CustomerId { get; init; }
	public string? CustomerName { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal OutstandingAmount { get; init; }
	public int DaysOverdue { get; init; }
	public int LevelNumber { get; init; }
	public required string LevelCode { get; init; }
}

public sealed record FinanceReceivableAgingSummary(
	long CustomerId,
	string CustomerName,
	CurrencyCode Currency,
	decimal Current,
	decimal Days1To30,
	decimal Days31To60,
	decimal Days61To90,
	decimal DaysOver90,
	decimal UnappliedCredits)
{
	public decimal TotalReceivable => Current + Days1To30 + Days31To60 + Days61To90 + DaysOver90;
	public decimal NetExposure => TotalReceivable - UnappliedCredits;
}

public sealed record FinanceCustomerStatementRow(
	DateOnly Date,
	string Type,
	string Reference,
	string Description,
	CurrencyCode Currency,
	decimal Debit,
	decimal Credit,
	decimal OpenAmount);
