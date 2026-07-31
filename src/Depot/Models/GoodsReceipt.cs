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
	public IReadOnlyList<GoodsReceiptLine> Lines { get; set; } = [];
}
