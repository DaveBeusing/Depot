// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SupplierItem
{
	public long Id { get; set; }
	public long SupplierId { get; set; }
	public long ItemId { get; set; }
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public string SupplierPartNumber { get; set; } = string.Empty;
	public decimal PurchasePrice { get; set; }
	public int LeadTimeDays { get; set; }
	public decimal MinimumOrderQuantity { get; set; }
	public bool IsPreferredSupplier { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
}
