// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class GoodsReceiptService
{
	private readonly GoodsReceiptRepository _receipts;
	private readonly AuditService _audit;

	public GoodsReceiptService(GoodsReceiptRepository receipts, AuditService audit)
	{
		_receipts = receipts;
		_audit = audit;
	}

	public Task<IReadOnlyList<ReceiptInventoryOption>> GetInventoryOptionsAsync(long itemId, CancellationToken cancellationToken = default) =>
		_receipts.ListInventoryOptionsAsync(itemId, cancellationToken);

	public async Task<GoodsReceipt> PostAsync(GoodsReceipt receipt, CancellationToken cancellationToken = default)
	{
		receipt.InvoiceNumber = receipt.InvoiceNumber.Trim();
		receipt.InvoiceDocumentPath = Normalize(receipt.InvoiceDocumentPath);
		receipt.Notes = Normalize(receipt.Notes);
		if (receipt.PurchaseOrderId <= 0) throw new ArgumentException("A purchase order is required.");
		if (string.IsNullOrWhiteSpace(receipt.InvoiceNumber)) throw new ArgumentException("Invoice number is required before posting the goods receipt.");
		if (receipt.InvoiceNumber.Length > 100) throw new ArgumentException("Invoice number must not exceed 100 characters.");
		if (receipt.InvoiceDate.Date > DateTime.Today) throw new ArgumentException("Invoice date cannot be in the future.");
		if (string.IsNullOrWhiteSpace(receipt.InvoiceDocumentPath)) throw new ArgumentException("An invoice document is required before posting the goods receipt.");
		if (receipt.InvoiceDocumentPath?.Length > 1000) throw new ArgumentException("Invoice document path must not exceed 1000 characters.");
		if (!File.Exists(receipt.InvoiceDocumentPath)) throw new FileNotFoundException("The selected invoice document was not found.", receipt.InvoiceDocumentPath);
		if (receipt.Lines.Count == 0) throw new InvalidOperationException("At least one receipt line is required.");
		if (receipt.Lines.Any(line => line.Quantity <= 0 || line.InventoryId <= 0)) throw new InvalidOperationException("Every receipt line requires a positive quantity and destination inventory.");
		if (receipt.Lines.Select(line => line.PurchaseOrderLineId).Distinct().Count() != receipt.Lines.Count) throw new InvalidOperationException("A purchase order line can only occur once per goods receipt.");

		return await _receipts.PostAsync(receipt, _audit.CreateCreatedEntry(0, receipt), cancellationToken);
	}

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
