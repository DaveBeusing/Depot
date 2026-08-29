// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public static class FinanceInventoryAccountingEvents
{
	public const string SourceType = "InventoryAccounting";
	public const string GoodsReceipt = "GoodsReceipt";
	public const string SalesShipment = "SalesShipment";
	public const string InventoryAdjustment = "InventoryAdjustment";
	public const string PurchaseVariance = "PurchaseVariance";
	public const string LandedCost = "LandedCost";
}

public static class FinanceInventoryAccountingAmountKeys
{
	public const string Cost = "Cost";
	public const string AdjustmentDebit = "AdjustmentDebit";
	public const string AdjustmentCredit = "AdjustmentCredit";
	public const string VarianceDebit = "VarianceDebit";
	public const string VarianceCredit = "VarianceCredit";
}

public enum FinanceLandedCostAllocationMethod
{
	Quantity = 1,
	ExistingValue = 2
}

public sealed record FinanceInventoryAccountingPolicy
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public required Guid InventoryControlAccountId { get; init; }
	public long InventoryAdjustmentPostingProfileId { get; init; }
	public long PurchaseVariancePostingProfileId { get; init; }
	public long LandedCostPostingProfileId { get; init; }
	public bool IsActive { get; init; }
}

public sealed record FinanceInventoryPurchaseVariance
{
	public long Id { get; init; }
	public long SupplierDocumentId { get; init; }
	public required Guid OperationId { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal ExpectedNetAmount { get; init; }
	public decimal ActualNetAmount { get; init; }
	public decimal SignedVarianceAmount { get; init; }
	public long JournalEntryId { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public long? ReversalJournalEntryId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
}

public sealed record FinanceInventoryLandedCostRequest
{
	public required Guid OperationId { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal Amount { get; init; }
	public FinanceLandedCostAllocationMethod AllocationMethod { get; init; } = FinanceLandedCostAllocationMethod.ExistingValue;
	public IReadOnlyList<long> LayerIds { get; init; } = [];
	public string? Reference { get; init; }
	public Guid? ExchangeRateId { get; init; }
}

public sealed record FinanceInventoryLandedCostOperation
{
	public long Id { get; init; }
	public required Guid OperationId { get; init; }
	public required string RequestHash { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal Amount { get; init; }
	public FinanceLandedCostAllocationMethod AllocationMethod { get; init; }
	public string? Reference { get; init; }
	public long JournalEntryId { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public long? ReversalJournalEntryId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
	public IReadOnlyList<FinanceInventoryLandedCostAllocation> Allocations { get; init; } = [];
}

public sealed record FinanceInventoryLandedCostAllocation
{
	public long Id { get; init; }
	public long OperationId { get; init; }
	public long LayerId { get; init; }
	public decimal Amount { get; init; }
	public decimal UnitCostIncrease { get; init; }
}

public sealed record FinanceInventoryReconciliationRequest
{
	public required Guid OperationId { get; init; }
	public required DateOnly AsOfDate { get; init; }
}

public sealed record FinanceInventoryReconciliationRun
{
	public long Id { get; init; }
	public required Guid OperationId { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required Guid InventoryControlAccountId { get; init; }
	public required DateOnly AsOfDate { get; init; }
	public required CurrencyCode ReportingCurrency { get; init; }
	public decimal ValuationAmount { get; init; }
	public decimal GeneralLedgerAmount { get; init; }
	public decimal Difference { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public IReadOnlyList<FinanceInventoryReconciliationLine> Lines { get; init; } = [];
}

public sealed record FinanceInventoryReconciliationLine
{
	public long ItemId { get; init; }
	public int Quantity { get; init; }
	public decimal ReportingValue { get; init; }
}

public sealed record FinanceInventoryValuationSummary(long ItemId, int Quantity, CurrencyCode Currency, decimal TransactionValue);
