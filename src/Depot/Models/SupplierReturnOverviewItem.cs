// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SupplierReturnOverviewItem
{
	public long Id { get; init; }
	public string ReturnNumber { get; init; } = string.Empty;
	public long SupplierId { get; init; }
	public string SupplierName { get; init; } = string.Empty;
	public DateTime ReturnDate { get; init; }
	public SupplierReturnStatus Status { get; init; }
	public string PurchaseOrderNumber { get; init; } = string.Empty;
	public string GoodsReceiptNumber { get; init; } = string.Empty;
	public string? SupplierReference { get; init; }
	public int LineCount { get; init; }
	public long Version { get; init; }
	public bool IsReversed { get; init; }
	public string StatusDisplayName => IsReversed ? "Posted · Reversed" : Status.ToString();
}
