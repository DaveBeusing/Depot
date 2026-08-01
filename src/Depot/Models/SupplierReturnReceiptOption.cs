// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SupplierReturnReceiptOption
{
	public long GoodsReceiptId { get; init; }
	public string ReceiptNumber { get; init; } = string.Empty;
	public DateTime ReceiptDate { get; init; }
	public long PurchaseOrderId { get; init; }
	public string PurchaseOrderNumber { get; init; } = string.Empty;
	public long SupplierId { get; init; }
	public string SupplierName { get; init; } = string.Empty;
	public string DisplayName => $"{ReceiptNumber} · {PurchaseOrderNumber} · {SupplierName}";
}
