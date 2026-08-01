// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SupplierReturn
{
	public long Id { get; set; }
	public string ReturnNumber { get; set; } = string.Empty;
	public long SupplierId { get; set; }
	public string SupplierName { get; set; } = string.Empty;
	public DateTime ReturnDate { get; set; } = DateTime.Today;
	public SupplierReturnStatus Status { get; set; } = SupplierReturnStatus.Draft;
	public long PurchaseOrderId { get; set; }
	public string PurchaseOrderNumber { get; set; } = string.Empty;
	public long GoodsReceiptId { get; set; }
	public string GoodsReceiptNumber { get; set; } = string.Empty;
	public string? SupplierReference { get; set; }
	public string? Notes { get; set; }
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public long? ReversedByUserId { get; set; }
	public DateTime? ReversedAtUtc { get; set; }
	public string? ReversalReason { get; set; }
	public bool IsReversed => ReversedAtUtc is not null;
	public string StatusDisplayName => IsReversed ? "Posted · Reversed" : Status.ToString();
	public long Version { get; set; } = 1;
	public IReadOnlyList<SupplierReturnLine> Lines { get; set; } = [];
}
