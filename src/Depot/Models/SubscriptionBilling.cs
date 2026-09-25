// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SubscriptionContractStatus { Draft = 1, Active = 2, Paused = 3, Cancelled = 4, Expired = 5 }
public enum SubscriptionBillingCadence { Monthly = 1, Quarterly = 2, Annual = 3 }
public enum SubscriptionPricePolicy { FixedContractPrice = 1, RepriceAtBilling = 2 }
public enum SubscriptionBillingInstanceStatus { Due = 1, Generated = 2, Blocked = 3 }

public sealed record SubscriptionContract
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public int TermsRevision { get; init; } = 1;
	public string ContractNumber { get; init; } = string.Empty;
	public long CustomerId { get; init; }
	public string CustomerName { get; init; } = string.Empty;
	public Guid LegalEntityId { get; init; }
	public string Currency { get; init; } = "EUR";
	public DateOnly StartDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
	public DateOnly? EndDate { get; init; }
	public SubscriptionBillingCadence Cadence { get; init; } = SubscriptionBillingCadence.Monthly;
	public DateOnly NextBillingDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
	public SubscriptionContractStatus Status { get; init; } = SubscriptionContractStatus.Draft;
	public DateOnly StatusEffectiveDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
	public long OwnerUserId { get; init; }
	public string? Notes { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public DateTime UpdatedAtUtc { get; init; }
	public long UpdatedByUserId { get; init; }
	public IReadOnlyList<SubscriptionContractLine> Lines { get; init; } = [];
}

public sealed record SubscriptionContractLine
{
	public long Id { get; init; }
	public long ContractId { get; init; }
	public int TermsRevision { get; init; } = 1;
	public int LineNumber { get; init; }
	public long ItemId { get; init; }
	public string PartNumber { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public int Quantity { get; init; } = 1;
	public decimal UnitPrice { get; init; }
	public decimal DiscountPercent { get; init; }
	public decimal TaxRate { get; init; } = 19m;
	public SubscriptionPricePolicy PricePolicy { get; init; } = SubscriptionPricePolicy.FixedContractPrice;
	public long? PriceSourceListId { get; init; }
	public string? PriceSourceName { get; init; }
	public SalesPriceListScope? PriceSourceScope { get; init; }
	public string? PriceSourceCurrency { get; init; }
}

public sealed record SubscriptionBillingInstance
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public long ContractId { get; init; }
	public string ContractNumber { get; init; } = string.Empty;
	public int TermsRevision { get; init; }
	public DateOnly PeriodStart { get; init; }
	public DateOnly PeriodEnd { get; init; }
	public DateOnly BillingDate { get; init; }
	public SubscriptionBillingInstanceStatus Status { get; init; } = SubscriptionBillingInstanceStatus.Due;
	public long? SalesInvoiceId { get; init; }
	public string? SalesInvoiceNumber { get; init; }
	public string? BlockReason { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public DateTime? GeneratedAtUtc { get; init; }
	public long? GeneratedByUserId { get; init; }
	public IReadOnlyList<SubscriptionBillingPriceEvidence> PriceEvidence { get; init; } = [];
}

public sealed record SubscriptionBillingPriceEvidence
{
	public long Id { get; init; }
	public long BillingInstanceId { get; init; }
	public long ContractLineId { get; init; }
	public int LineNumber { get; init; }
	public decimal UnitPrice { get; init; }
	public decimal DiscountPercent { get; init; }
	public long? PriceSourceListId { get; init; }
	public string? PriceSourceName { get; init; }
	public SalesPriceListScope? PriceSourceScope { get; init; }
	public string? PriceSourceCurrency { get; init; }
	public DateTime ResolvedAtUtc { get; init; }
}

public sealed record SubscriptionLifecycleEvent
{
	public long Id { get; init; }
	public long ContractId { get; init; }
	public string Action { get; init; } = string.Empty;
	public DateOnly EffectiveDate { get; init; }
	public SubscriptionContractStatus PreviousStatus { get; init; }
	public SubscriptionContractStatus NewStatus { get; init; }
	public string? Comment { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
}

public sealed record SubscriptionContractRevision(long ContractId,int TermsRevision,DateOnly EffectiveFrom,string TermsSnapshotJson,DateTime CreatedAtUtc,long CreatedByUserId);
