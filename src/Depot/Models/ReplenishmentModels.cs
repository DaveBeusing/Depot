// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ReplenishmentSuggestionStatus
{
	Open = 1,
	Accepted = 2,
	Dismissed = 3,
	Superseded = 4,
	Blocked = 5
}

public sealed class ReplenishmentPolicy
{
	public long Id { get; set; }
	public long ItemId { get; set; }
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public long WarehouseId { get; set; }
	public string WarehouseName { get; set; } = string.Empty;
	public bool IsActive { get; set; } = true;
	public int ReorderPoint { get; set; }
	public int SafetyStock { get; set; }
	public int TargetStock { get; set; }
	public long? PreferredSupplierId { get; set; }
	public string? PreferredSupplierName { get; set; }
	public long Version { get; set; } = 1;
}

public sealed class ReplenishmentRequirementInput
{
	public required ReplenishmentPolicy Policy { get; init; }
	public long OnHandQuantity { get; init; }
	public long ReservedQuantity { get; init; }
	public long BackorderedQuantity { get; init; }
	public long EligibleInboundQuantity { get; init; }
	public int ActivePolicyCountForItem { get; init; }
	public int SupplierEvidenceCount { get; init; }
	public long? SupplierItemId { get; init; }
	public long? PlanningSupplierId { get; init; }
	public int? SupplierLeadTimeDays { get; init; }
	public decimal? SupplierMinimumOrderQuantity { get; init; }
}

public sealed class ReplenishmentRequirementSnapshot
{
	public long Id { get; set; }
	public long PolicyId { get; set; }
	public string SnapshotKey { get; set; } = string.Empty;
	public DateTime CalculatedAtUtc { get; set; }
	public long OnHandQuantity { get; set; }
	public long ReservedQuantity { get; set; }
	public long BackorderedQuantity { get; set; }
	public long EligibleInboundQuantity { get; set; }
	public long ProjectedAvailableQuantity { get; set; }
	public int ReorderPoint { get; set; }
	public int SafetyStock { get; set; }
	public int TargetStock { get; set; }
	public long RequiredReplenishmentQuantity { get; set; }
	public int SuggestedPurchaseQuantity { get; set; }
	public long? PlanningSupplierId { get; set; }
	public long? SupplierItemId { get; set; }
	public int? SupplierLeadTimeDays { get; set; }
	public decimal? SupplierMinimumOrderQuantity { get; set; }
	public bool IsBlocked { get; set; }
	public string? BlockReason { get; set; }
	public string Explanation { get; set; } = string.Empty;
}

public sealed class ReplenishmentSuggestion
{
	public long Id { get; set; }
	public long PolicyId { get; set; }
	public long SnapshotId { get; set; }
	public ReplenishmentSuggestionStatus Status { get; set; }
	public int SuggestedQuantity { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? ReviewedAtUtc { get; set; }
	public long? ReviewedByUserId { get; set; }
	public long? ConvertedPurchaseRequisitionId { get; set; }
	public long Version { get; set; } = 1;
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public string WarehouseName { get; set; } = string.Empty;
	public string? PlanningSupplierName { get; set; }
	public ReplenishmentRequirementSnapshot Snapshot { get; set; } = new();
}

public sealed record ReplenishmentBatchResult(
	int PoliciesEvaluated,
	int SuggestionsOpen,
	int SuggestionsBlocked,
	int SuggestionsSuperseded,
	TimeSpan Elapsed);

public sealed record ReplenishmentConversionResult(
	long PurchaseRequisitionId,
	IReadOnlyList<long> SuggestionIds);
