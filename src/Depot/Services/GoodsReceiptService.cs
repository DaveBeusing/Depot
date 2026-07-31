// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class GoodsReceiptService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly GoodsReceiptRepository _receipts;
	private readonly PurchaseOrderRepository _purchaseOrders;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _stockMovements;
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;

	public GoodsReceiptService(
		IDatabaseTransactionRunner transactions,
		GoodsReceiptRepository receipts,
		PurchaseOrderRepository purchaseOrders,
		InventoryRepository inventories,
		StockMovementRepository stockMovements,
		ReasonCodeRepository reasonCodes,
		AuditRepository auditEntries,
		AuditService audit)
	{
		_transactions = transactions;
		_receipts = receipts;
		_purchaseOrders = purchaseOrders;
		_inventories = inventories;
		_stockMovements = stockMovements;
		_reasonCodes = reasonCodes;
		_auditEntries = auditEntries;
		_audit = audit;
	}

	public Task<IReadOnlyList<ReceiptInventoryOption>> GetInventoryOptionsAsync(
		long itemId,
		CancellationToken cancellationToken = default) =>
		_receipts.ListInventoryOptionsAsync(itemId, cancellationToken);

	public async Task<GoodsReceipt> PostAsync(
		GoodsReceipt receipt,
		CancellationToken cancellationToken = default)
	{
		Validate(receipt);
		receipt.ReceivedByUserId = _audit.CurrentUserId
			?? throw new InvalidOperationException("A signed-in user is required to post a goods receipt.");

		return await _transactions.ExecuteAsync(
			(transaction, token) => PostAsync(transaction, receipt, token),
			cancellationToken);
	}

	private async Task<GoodsReceipt> PostAsync(
		DatabaseTransactionContext transaction,
		GoodsReceipt receipt,
		CancellationToken cancellationToken)
	{
		var order = await _purchaseOrders.GetForReceiptUpdateAsync(
			transaction,
			receipt.PurchaseOrderId,
			cancellationToken)
			?? throw new InvalidOperationException("The purchase order was not found.");
		if (order.Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
		{
			throw new InvalidOperationException(
				"Only ordered or partially received purchase orders can be received.");
		}

		var reasonCode = await _reasonCodes.GetByCodeAsync(
			transaction,
			ReasonCodeSystemCodes.GoodsReceipt,
			cancellationToken);
		if (reasonCode is null || !reasonCode.IsActive)
		{
			throw new InvalidOperationException(
				$"Required system reason code '{ReasonCodeSystemCodes.GoodsReceipt}' is unavailable.");
		}

		var orderLines = order.Lines.ToDictionary(line => line.Id);
		ValidateReceiptLines(receipt, orderLines);

		receipt.ReceiptNumber = $"PENDING-{Guid.NewGuid():N}";
		receipt.Id = await _receipts.CreateAsync(transaction, receipt, cancellationToken);
		receipt.ReceiptNumber = $"GR-{receipt.Id:000000}";
		if (await _receipts.UpdateReceiptNumberAsync(
			transaction,
			receipt.Id,
			receipt.ReceiptNumber,
			cancellationToken) != 1)
		{
			throw new ConcurrencyConflictException("goods receipt number");
		}

		foreach (var line in receipt.Lines)
		{
			var orderLine = orderLines[line.PurchaseOrderLineId];
			var inventory = await _inventories.GetForUpdateAsync(
				transaction,
				line.InventoryId,
				cancellationToken)
				?? throw new InvalidOperationException("The destination inventory was not found.");
			if (!inventory.IsActive)
			{
				throw new InvalidOperationException("The destination inventory is inactive.");
			}
			if (inventory.ItemId != orderLine.ItemId)
			{
				throw new InvalidOperationException(
					"The destination inventory does not belong to the ordered item.");
			}

			line.GoodsReceiptId = receipt.Id;
			line.Id = await _receipts.CreateLineAsync(transaction, line, cancellationToken);
			var receivedQuantity = orderLine.ReceivedQuantity + line.Quantity;
			if (!await _purchaseOrders.UpdateReceivedQuantityAsync(
				transaction,
				orderLine.Id,
				orderLine.Version,
				receivedQuantity,
				cancellationToken))
			{
				throw new ConcurrencyConflictException("purchase order line receipt");
			}

			orderLine.ReceivedQuantity = receivedQuantity;
			orderLine.Version++;
			var movement = new StockMovement
			{
				InventoryId = line.InventoryId,
				ReasonCodeId = reasonCode.Id,
				MovementType = StockMovementType.Purchase,
				TimestampUtc = DateTime.UtcNow,
				Quantity = line.Quantity,
				UnitPrice = orderLine.UnitPrice,
				Reference = receipt.ReceiptNumber,
				Notes = $"Delivery note {receipt.SupplierDeliveryNoteNumber}"
			};
			movement.Id = await _stockMovements.CreateAsync(
				transaction,
				movement,
				cancellationToken);
		}

		var newStatus = orderLines.Values.All(line => line.ReceivedQuantity >= line.Quantity)
			? PurchaseOrderStatus.Received
			: PurchaseOrderStatus.PartiallyReceived;
		if (!await _purchaseOrders.UpdateStatusAsync(
			transaction,
			order.Id,
			order.Version,
			newStatus,
			cancellationToken))
		{
			throw new ConcurrencyConflictException("purchase order receipt status");
		}

		await _auditEntries.CreateAsync(
			transaction,
			_audit.CreateCreatedEntry(receipt.Id, receipt),
			cancellationToken);
		return receipt;
	}

	private static void Validate(GoodsReceipt receipt)
	{
		receipt.SupplierDeliveryNoteNumber = receipt.SupplierDeliveryNoteNumber.Trim();
		receipt.Notes = Normalize(receipt.Notes);
		if (receipt.PurchaseOrderId <= 0) throw new ArgumentException("A purchase order is required.");
		if (string.IsNullOrWhiteSpace(receipt.SupplierDeliveryNoteNumber)) throw new ArgumentException("Supplier delivery note number is required before posting the goods receipt.");
		if (receipt.SupplierDeliveryNoteNumber.Length > 100) throw new ArgumentException("Supplier delivery note number must not exceed 100 characters.");
		if (receipt.ReceiptDate.Date > DateTime.Today) throw new ArgumentException("Receipt date cannot be in the future.");
		if (receipt.Notes?.Length > 4000) throw new ArgumentException("Notes must not exceed 4000 characters.");
		if (receipt.Lines.Count == 0) throw new InvalidOperationException("At least one receipt line is required.");
		if (receipt.Lines.Any(line => line.Quantity <= 0 || line.InventoryId <= 0)) throw new InvalidOperationException("Every receipt line requires a positive quantity and destination inventory.");
		if (receipt.Lines.Select(line => line.PurchaseOrderLineId).Distinct().Count() != receipt.Lines.Count) throw new InvalidOperationException("A purchase order line can only occur once per goods receipt.");
	}

	private static void ValidateReceiptLines(
		GoodsReceipt receipt,
		IReadOnlyDictionary<long, PurchaseOrderLine> orderLines)
	{
		foreach (var receiptLine in receipt.Lines)
		{
			if (!orderLines.TryGetValue(receiptLine.PurchaseOrderLineId, out var orderLine))
			{
				throw new InvalidOperationException("A purchase order line was not found.");
			}
			if (orderLine.ReceivedQuantity + receiptLine.Quantity > orderLine.Quantity)
			{
				throw new InvalidOperationException(
					"Receipt quantity exceeds the open purchase order quantity.");
			}
		}
	}

	private static string? Normalize(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
