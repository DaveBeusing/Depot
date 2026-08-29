// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ItemCostBaseSource { PreferredSupplierPurchasePrice = 0 }
public enum ItemCostCalculationType { Absolute = 0, Percentage = 1 }
public enum ItemCostCalculationBase { BaseCost = 0, RunningTotal = 1 }

public sealed class ItemCostProfile
{
	public long Id { get; set; }
	public long ItemId { get; set; }
	public ItemCostBaseSource BaseCostSource { get; set; } = ItemCostBaseSource.PreferredSupplierPurchasePrice;
	public string Currency { get; set; } = string.Empty;
	public long Version { get; set; } = 1;
}

public sealed class ItemCostComponent
{
	public long Id { get; set; }
	public long ItemId { get; set; }
	public string Name { get; set; } = string.Empty;
	public ItemCostCalculationType CalculationType { get; set; }
	public decimal Value { get; set; }
	public ItemCostCalculationBase CalculationBase { get; set; } = ItemCostCalculationBase.BaseCost;
	public int Sequence { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime? ValidFrom { get; set; }
	public DateTime? ValidUntil { get; set; }
	public long Version { get; set; } = 1;
}

public sealed record ItemCostBaseValue(long SupplierItemId, decimal Amount, long Version);
public sealed record ItemCostComponentResult(long ComponentId,string Name,ItemCostCalculationType CalculationType,ItemCostCalculationBase CalculationBase,decimal ConfiguredValue,decimal CalculationBasisAmount,decimal AppliedAmount,decimal RunningTotal,int Sequence,long Version);

public sealed class ItemCostCalculationResult
{
	public long ItemId { get; init; }
	public bool IsSuccess { get; init; }
	public string? Error { get; init; }
	public decimal BaseCost { get; init; }
	public decimal CalculatedCost { get; init; }
	public string Currency { get; init; } = string.Empty;
	public DateTime CalculationDate { get; init; }
	public ItemCostBaseSource BaseCostSource { get; init; }
	public string EvidenceVersion { get; init; } = string.Empty;
	public IReadOnlyList<ItemCostComponentResult> Components { get; init; } = [];
}

public sealed record ItemCostCandidate(long ItemId,string PartNumber,string Description,long? CategoryId,long? ManufacturerId);
public enum BulkPriceFilterType { AllActiveItems = 0, Category = 1, Manufacturer = 2, SelectedItems = 3 }
public enum BulkPriceApplyMode { ReplaceCalculatedPrices = 0, OnlyIncreasePrices = 1, OnlyCreateMissingPrices = 2 }
public enum BulkPricePreviewAction { Create = 0, Update = 1, Skip = 2, Error = 3 }

public sealed class PriceListGenerationRequest
{
	public long? ExistingPriceListId { get; init; }
	public SalesPriceList? NewPriceList { get; init; }
	public BulkPriceFilterType FilterType { get; init; } = BulkPriceFilterType.AllActiveItems;
	public long? FilterId { get; init; }
	public IReadOnlyList<long> SelectedItemIds { get; init; } = [];
	public decimal MarkupPercentage { get; init; }
	public BulkPriceApplyMode ApplyMode { get; init; } = BulkPriceApplyMode.ReplaceCalculatedPrices;
	public DateTime EffectiveDate { get; init; } = DateTime.Today;
}

public sealed class BulkPricePreviewRow
{
	public long ItemId { get; init; }
	public string PartNumber { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public bool IsCalculable { get; init; }
	public string? Error { get; init; }
	public decimal? CalculatedCost { get; init; }
	public decimal? CurrentPrice { get; init; }
	public decimal? CalculatedNewPrice { get; init; }
	public decimal? AbsoluteChange { get; init; }
	public decimal? PercentageChange { get; init; }
	public decimal MarkupPercentage { get; init; }
	public BulkPricePreviewAction Action { get; init; }
	public long? ExistingPriceItemId { get; init; }
	public long? ExistingPriceItemVersion { get; init; }
	public string CostEvidenceVersion { get; init; } = string.Empty;
	public IReadOnlyList<ItemCostComponentResult> CostComponents { get; init; } = [];
}

public sealed class PriceListGenerationPreview
{
	public required PriceListGenerationRequest Request { get; init; }
	public long? TargetPriceListId { get; init; }
	public long TargetPriceListVersion { get; init; }
	public required string TargetPriceListName { get; init; }
	public required string Currency { get; init; }
	public IReadOnlyList<BulkPricePreviewRow> Rows { get; init; } = [];
	public int CreateCount => Rows.Count(row => row.Action == BulkPricePreviewAction.Create);
	public int UpdateCount => Rows.Count(row => row.Action == BulkPricePreviewAction.Update);
	public int SkipCount => Rows.Count(row => row.Action == BulkPricePreviewAction.Skip);
	public int ErrorCount => Rows.Count(row => row.Action == BulkPricePreviewAction.Error);
}

public sealed record PriceListGenerationApplyResult(long PriceListId,int Created,int Updated,int Skipped,int Failed);
public sealed class PriceListGenerationAuditRecord
{
	public long PriceListId { get; init; }
	public string PriceListName { get; init; } = string.Empty;
	public string PricingMethod { get; init; } = "PercentageMarkup";
	public decimal MarkupPercentage { get; init; }
	public BulkPriceApplyMode ApplyMode { get; init; }
	public int Created { get; init; }
	public int Updated { get; init; }
	public int Skipped { get; init; }
	public int Failed { get; init; }
}
