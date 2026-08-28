// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceInventoryValuationMethod
{
	Fifo = 1
}

public enum FinanceInventoryAccountingEventKind
{
	GoodsReceipt = 1,
	SalesShipment = 2,
	GoodsReceiptReversal = 3,
	SalesShipmentReversal = 4
}

public sealed record FinanceInventoryAccountingConfiguration
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public required Guid LegalEntityId { get; init; }
	public required Guid FiscalCalendarId { get; init; }
	public required CurrencyCode PurchaseOrderPriceCurrency { get; init; }
	public FinanceInventoryValuationMethod ValuationMethod { get; init; } = FinanceInventoryValuationMethod.Fifo;
	public long GoodsReceiptPostingProfileId { get; init; }
	public long SalesIssuePostingProfileId { get; init; }
	public bool IsActive { get; init; }
}

public sealed record FinanceInventoryValuationLayer
{
	public long Id { get; init; }
	public required Guid AccountingBookId { get; init; }
	public long ItemId { get; init; }
	public long SourceMovementId { get; init; }
	public required DateOnly AcquiredDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public int OriginalQuantity { get; init; }
	public int RemainingQuantity { get; init; }
	public decimal UnitCost { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
}

public sealed record FinanceInventoryValuationConsumption
{
	public long Id { get; init; }
	public long MovementId { get; init; }
	public long LayerId { get; init; }
	public int Quantity { get; init; }
	public decimal UnitCost { get; init; }
	public decimal Amount { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
}

public sealed record FinanceInventoryAccountingEvent
{
	public long Id { get; init; }
	public long MovementId { get; init; }
	public FinanceInventoryAccountingEventKind Kind { get; init; }
	public required Guid AccountingBookId { get; init; }
	public long ItemId { get; init; }
	public int Quantity { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal Amount { get; init; }
	public long JournalEntryId { get; init; }
	public required Guid OperationId { get; init; }
	public long? ReversalOfMovementId { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long? CreatedByUserId { get; init; }
}
