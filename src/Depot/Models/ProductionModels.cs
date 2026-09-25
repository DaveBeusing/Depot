// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum BillOfMaterialStatus
{
	Draft = 1,
	Active = 2,
	Retired = 3
}

public enum ProductionOrderStatus
{
	Draft = 1,
	Released = 2,
	InProgress = 3,
	Completed = 4,
	Cancelled = 5,
	Reversed = 6
}

public enum ProductionMovementKind
{
	ComponentIssue = 1,
	ComponentIssueReversal = 2,
	FinishedGoodsReceipt = 3,
	FinishedGoodsReceiptReversal = 4
}

public sealed class BillOfMaterial
{
	public long Id { get; set; }
	public long FinishedItemId { get; set; }
	public string FinishedPartNumber { get; set; } = string.Empty;
	public string FinishedDescription { get; set; } = string.Empty;
	public string Revision { get; set; } = string.Empty;
	public BillOfMaterialStatus Status { get; set; } = BillOfMaterialStatus.Draft;
	public DateTime? EffectiveFromUtc { get; set; }
	public DateTime? EffectiveUntilUtc { get; set; }
	public long Version { get; set; } = 1;
	public DateTime CreatedAtUtc { get; set; }
	public long CreatedByUserId { get; set; }
	public IReadOnlyList<BillOfMaterialLine> Lines { get; set; } = [];
}

public sealed class BillOfMaterialLine
{
	public long Id { get; set; }
	public long BillOfMaterialId { get; set; }
	public long ComponentItemId { get; set; }
	public string ComponentPartNumber { get; set; } = string.Empty;
	public string ComponentDescription { get; set; } = string.Empty;
	public string? UnitOfMeasure { get; set; }
	public decimal Quantity { get; set; }
	public int Sequence { get; set; }
}

public sealed class ProductionOrder
{
	public long Id { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public long FinishedItemId { get; set; }
	public string FinishedPartNumber { get; set; } = string.Empty;
	public string FinishedDescription { get; set; } = string.Empty;
	public long BillOfMaterialId { get; set; }
	public string BillOfMaterialRevision { get; set; } = string.Empty;
	public int PlannedQuantity { get; set; }
	public int CompletedQuantity { get; set; }
	public long WarehouseId { get; set; }
	public string WarehouseName { get; set; } = string.Empty;
	public long FinishedInventoryId { get; set; }
	public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Draft;
	public long? OwnerUserId { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public long CreatedByUserId { get; set; }
	public DateTime? ReleasedAtUtc { get; set; }
	public long? ReleasedByUserId { get; set; }
	public DateTime? CompletedAtUtc { get; set; }
	public long? CompletedByUserId { get; set; }
	public DateTime? ReversedAtUtc { get; set; }
	public long? ReversedByUserId { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<ProductionOrderRequirement> Requirements { get; set; } = [];
}

public sealed class ProductionOrderRequirement
{
	public long Id { get; set; }
	public long ProductionOrderId { get; set; }
	public long SourceBillOfMaterialLineId { get; set; }
	public long ComponentItemId { get; set; }
	public string ComponentPartNumber { get; set; } = string.Empty;
	public string ComponentDescription { get; set; } = string.Empty;
	public string? UnitOfMeasure { get; set; }
	public int RequiredQuantity { get; set; }
	public int IssuedQuantity { get; set; }
	public int Sequence { get; set; }
	public int RemainingQuantity => Math.Max(0, RequiredQuantity - IssuedQuantity);
}

public sealed record ProductionRequirementAvailability(
	long RequirementId,
	long ComponentItemId,
	string ComponentPartNumber,
	int RequiredQuantity,
	int IssuedQuantity,
	long AvailableQuantity,
	long ReservedQuantity,
	long ReservableQuantity,
	long ShortageQuantity);

public sealed class ProductionMaterialMovement
{
	public long Id { get; set; }
	public long ProductionOrderId { get; set; }
	public long? RequirementId { get; set; }
	public long StockMovementId { get; set; }
	public ProductionMovementKind Kind { get; set; }
	public int Quantity { get; set; }
	public string OperationKey { get; set; } = string.Empty;
	public DateTime CreatedAtUtc { get; set; }
	public long CreatedByUserId { get; set; }
}

public sealed class ProductionAssemblyCostEvidence
{
	public long Id { get; set; }
	public long ProductionOrderId { get; set; }
	public long RequirementId { get; set; }
	public long ComponentItemId { get; set; }
	public int Quantity { get; set; }
	public decimal UnitCost { get; set; }
	public decimal ExtendedCost { get; set; }
	public string Currency { get; set; } = string.Empty;
	public string EvidenceVersion { get; set; } = string.Empty;
	public DateTime CalculatedAtUtc { get; set; }
}

public sealed record ProductionCompletionCost(
	decimal TotalComponentCost,
	decimal UnitFinishedCost,
	string Currency,
	IReadOnlyList<ProductionAssemblyCostEvidence> Components);

public sealed record ProductionShortageHandoffResult(
	long ProductionOrderId,
	int ShortagesEvaluated,
	IReadOnlyList<long> ReplenishmentSuggestionIds);
