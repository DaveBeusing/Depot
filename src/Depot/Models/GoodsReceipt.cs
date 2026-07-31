// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class GoodsReceipt
{
	public long Id { get; set; }
	public long PurchaseOrderId { get; set; }
	public string ReceiptNumber { get; set; } = string.Empty;
	public DateTime ReceiptDate { get; set; } = DateTime.Today;
	public string SupplierDeliveryNoteNumber { get; set; } = string.Empty;
	public long ReceivedByUserId { get; set; }
	public string? Notes { get; set; }
	public DateTime? ReversedAtUtc { get; set; }
	public long? ReversedByUserId { get; set; }
	public string? ReversalReason { get; set; }
	public long Version { get; set; } = 1;
	public bool IsReversed => ReversedAtUtc is not null;
	public IReadOnlyList<GoodsReceiptLine> Lines { get; set; } = [];
}
