// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinancePayableDocumentKind
{
	Invoice = 1,
	CreditNote = 2
}

public enum FinancePayableDocumentStatus
{
	Draft = 0,
	PendingApproval = 1,
	Approved = 2,
	Rejected = 3,
	Posted = 4,
	Reversed = 5
}

public enum FinancePayableMatchStatus
{
	NotRequired = 0,
	Matched = 1,
	Exception = 2
}

public enum FinancePayableOpenItemKind
{
	Invoice = 1,
	CreditNote = 2,
	Payment = 3
}

public enum FinancePayableDirection
{
	Debit = 1,
	Credit = 2
}

public enum FinancePayableSettlementStatus
{
	Open = 0,
	PartiallySettled = 1,
	Settled = 2,
	Voided = 3
}

public static class FinancePayablePostingAmountKeys
{
	public const string Gross = "Gross";
	public const string Net = "Net";
	public const string Tax = "Tax";
	public const string Payment = "Payment";
}

public static class FinancePayableSourceTypes
{
	public const string SupplierInvoice = nameof(FinanceSupplierDocument) + ".Invoice";
	public const string SupplierCreditNote = nameof(FinanceSupplierDocument) + ".CreditNote";
	public const string SupplierPayment = nameof(FinancePayablePayment);
}

public sealed record FinancePayablesConfiguration
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid LegalEntityId { get; init; }
	public required Guid FiscalCalendarId { get; init; }
	public required long InvoicePostingProfileId { get; init; }
	public required long CreditNotePostingProfileId { get; init; }
	public required long PaymentPostingProfileId { get; init; }
	public bool IsActive { get; init; } = true;
}

public sealed record FinanceSupplierDocument
{
	public long Id { get; init; }
	public long Version { get; init; }
	public FinancePayableDocumentKind Kind { get; init; }
	public required long SupplierId { get; init; }
	public string? SupplierName { get; init; }
	public required string SupplierDocumentNumber { get; init; }
	public string? InternalReference { get; init; }
	public required DateOnly DocumentDate { get; init; }
	public required DateOnly DueDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public FinancePayableDocumentStatus Status { get; init; }
	public decimal NetAmount { get; init; }
	public decimal TaxAmount { get; init; }
	public decimal GrossAmount { get; init; }
	public long CreatedByUserId { get; init; }
	public required DateTime CreatedAtUtc { get; init; }
	public long? SubmittedByUserId { get; init; }
	public DateTime? SubmittedAtUtc { get; init; }
	public long? ApprovalDecisionByUserId { get; init; }
	public DateTime? ApprovalDecisionAtUtc { get; init; }
	public string? ApprovalComment { get; init; }
	public bool MatchExceptionApproved { get; init; }
	public string? MatchExceptionReason { get; init; }
	public long? PostedByUserId { get; init; }
	public DateTime? PostedAtUtc { get; init; }
	public Guid? PostingOperationId { get; init; }
	public long? OpenItemId { get; init; }
	public long? JournalEntryId { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public long? ReversalJournalEntryId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
	public IReadOnlyList<FinanceSupplierDocumentLine> Lines { get; init; } = [];

	public bool HasMatchExceptions => Lines.Any(line => line.MatchStatus == FinancePayableMatchStatus.Exception);
}

public sealed record FinanceSupplierDocumentLine
{
	public long Id { get; init; }
	public long DocumentId { get; init; }
	public int LineNumber { get; init; }
	public long? PurchaseOrderLineId { get; init; }
	public long? GoodsReceiptLineId { get; init; }
	public required string Description { get; init; }
	public decimal Quantity { get; init; }
	public decimal UnitPrice { get; init; }
	public decimal NetAmount { get; init; }
	public decimal TaxAmount { get; init; }
	public decimal GrossAmount { get; init; }
	public FinancePayableMatchStatus MatchStatus { get; init; }
	public decimal? OrderedUnitPrice { get; init; }
	public decimal? ReceivedQuantity { get; init; }
	public decimal? PreviouslyInvoicedQuantity { get; init; }
	public decimal QuantityVariance { get; init; }
	public decimal PriceVariance { get; init; }
}

public sealed record FinanceSupplierDocumentDraft
{
	public long Id { get; init; }
	public long Version { get; init; }
	public FinancePayableDocumentKind Kind { get; init; }
	public required long SupplierId { get; init; }
	public required string SupplierDocumentNumber { get; init; }
	public string? InternalReference { get; init; }
	public required DateOnly DocumentDate { get; init; }
	public required DateOnly DueDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public IReadOnlyList<FinanceSupplierDocumentLineDraft> Lines { get; init; } = [];
}

public sealed record FinanceSupplierDocumentLineDraft
{
	public long? PurchaseOrderLineId { get; init; }
	public long? GoodsReceiptLineId { get; init; }
	public required string Description { get; init; }
	public decimal Quantity { get; init; }
	public decimal UnitPrice { get; init; }
	public decimal TaxAmount { get; init; }
	public decimal NetAmount => Quantity * UnitPrice;
	public decimal GrossAmount => NetAmount + TaxAmount;
}

public sealed record FinanceSupplierApprovalRequest
{
	public required long ExpectedVersion { get; init; }
	public required bool Approve { get; init; }
	public string? Comment { get; init; }
	public bool ApproveMatchException { get; init; }
	public string MatchExceptionReason { get; init; } = string.Empty;
}

public sealed record FinanceSupplierPostingRequest
{
	public required Guid OperationId { get; init; }
	public required long ExpectedVersion { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
}

public sealed record FinancePayableOpenItem
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid LegalEntityId { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required long SupplierId { get; init; }
	public string? SupplierName { get; init; }
	public FinancePayableOpenItemKind Kind { get; init; }
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

	public FinancePayableDirection Direction => Kind == FinancePayableOpenItemKind.Invoice ? FinancePayableDirection.Credit : FinancePayableDirection.Debit;
	public decimal SignedRemainingAmount => Direction == FinancePayableDirection.Credit ? RemainingAmount : -RemainingAmount;
	public FinancePayableSettlementStatus SettlementStatus => IsVoided
		? FinancePayableSettlementStatus.Voided
		: RemainingAmount == 0m
			? FinancePayableSettlementStatus.Settled
			: RemainingAmount < OriginalAmount
				? FinancePayableSettlementStatus.PartiallySettled
				: FinancePayableSettlementStatus.Open;
}

public sealed record FinancePayableAllocation
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

public sealed record FinancePayableAllocationRequest(long CreditOpenItemId, decimal Amount);

public sealed record FinancePayablePaymentRequest
{
	public required Guid OperationId { get; init; }
	public required long SupplierId { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required DateOnly PaymentDate { get; init; }
	public decimal Amount { get; init; }
	public string? Reference { get; init; }
	public required string Description { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
	public IReadOnlyList<FinancePayableAllocationRequest> Allocations { get; init; } = [];
}

public sealed record FinancePayablePayment
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid OperationId { get; init; }
	public required string RequestHash { get; init; }
	public required long SupplierId { get; init; }
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

public sealed record FinancePayableReversalRequest
{
	public required Guid OperationId { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required string Reason { get; init; }
}

public sealed record FinancePayableAgingSummary(
	long SupplierId,
	string SupplierName,
	CurrencyCode Currency,
	decimal Current,
	decimal Days1To30,
	decimal Days31To60,
	decimal Days61To90,
	decimal DaysOver90,
	decimal UnappliedDebits)
{
	public decimal TotalPayable => Current + Days1To30 + Days31To60 + Days61To90 + DaysOver90;
	public decimal NetExposure => TotalPayable - UnappliedDebits;
}

public sealed record FinanceSupplierStatementRow(
	DateOnly Date,
	string Type,
	string Reference,
	string Description,
	CurrencyCode Currency,
	decimal Debit,
	decimal Credit,
	decimal RemainingAmount);
