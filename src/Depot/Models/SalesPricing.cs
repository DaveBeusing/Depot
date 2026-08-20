// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SalesPriceList
{
	public long Id { get; set; }
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Currency { get; set; } = "EUR";
	public DateTime? ValidFrom { get; set; }
	public DateTime? ValidTo { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
	public IReadOnlyList<SalesPriceListItem> Items { get; set; } = [];
}

public sealed class SalesPriceListItem
{
	public long Id { get; set; }
	public long SalesPriceListId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public decimal UnitPrice { get; set; }
	public decimal DiscountPercent { get; set; }
	public long Version { get; set; } = 1;
}

public sealed class CustomerPriceListAssignment
{
	public long CustomerId { get; set; }
	public long SalesPriceListId { get; set; }
	public string PriceListName { get; set; } = string.Empty;
}

public sealed record SalesPriceResult(decimal UnitPrice, decimal DiscountPercent, string Source);
