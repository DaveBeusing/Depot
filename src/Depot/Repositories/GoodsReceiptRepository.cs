// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class GoodsReceiptRepository : DatabaseRepository
{
	public GoodsReceiptRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<IReadOnlyList<ReceiptInventoryOption>> ListInventoryOptionsAsync(
		long itemId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"SELECT inv.Id, inv.ItemId, w.Name, sl.Name, p.Name FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId INNER JOIN Warehouses w ON w.Id = sl.WarehouseId INNER JOIN Purposes p ON p.Id = inv.PurposeId WHERE inv.ItemId = $ItemId AND inv.IsActive = 1 AND sl.IsActive = 1 AND w.IsActive = 1 ORDER BY w.Name, sl.Name, p.Name;",
			reader => new ReceiptInventoryOption
			{
				InventoryId = reader.GetInt64(0),
				ItemId = reader.GetInt64(1),
				DisplayName = $"{reader.GetString(2)} / {reader.GetString(3)} / {reader.GetString(4)}"
			},
			cancellationToken,
			Parameter("$ItemId", itemId));

	public Task<long> CreateAsync(
		DatabaseTransactionContext transaction,
		GoodsReceipt receipt,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO GoodsReceipts (ReceiptNumber, PurchaseOrderId, ReceiptDate, SupplierDeliveryNoteNumber, ReceivedByUserId, Notes) VALUES ($ReceiptNumber, $PurchaseOrderId, $ReceiptDate, $SupplierDeliveryNoteNumber, $ReceivedByUserId, $Notes);",
			cancellationToken,
			Parameter("$ReceiptNumber", receipt.ReceiptNumber),
			Parameter("$PurchaseOrderId", receipt.PurchaseOrderId),
			Parameter("$ReceiptDate", Date(receipt.ReceiptDate)),
			Parameter("$SupplierDeliveryNoteNumber", receipt.SupplierDeliveryNoteNumber),
			Parameter("$ReceivedByUserId", receipt.ReceivedByUserId),
			Parameter("$Notes", receipt.Notes));

	public Task<int> UpdateReceiptNumberAsync(
		DatabaseTransactionContext transaction,
		long id,
		string receiptNumber,
		CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE GoodsReceipts SET ReceiptNumber = $ReceiptNumber WHERE Id = $Id;",
			cancellationToken,
			Parameter("$ReceiptNumber", receiptNumber),
			Parameter("$Id", id));

	public Task<long> CreateLineAsync(
		DatabaseTransactionContext transaction,
		GoodsReceiptLine line,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO GoodsReceiptLines (GoodsReceiptId, PurchaseOrderLineId, InventoryId, Quantity) VALUES ($GoodsReceiptId, $PurchaseOrderLineId, $InventoryId, $Quantity);",
			cancellationToken,
			Parameter("$GoodsReceiptId", line.GoodsReceiptId),
			Parameter("$PurchaseOrderLineId", line.PurchaseOrderLineId),
			Parameter("$InventoryId", line.InventoryId),
			Parameter("$Quantity", line.Quantity));

	private static string Date(DateTime value) =>
		value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
