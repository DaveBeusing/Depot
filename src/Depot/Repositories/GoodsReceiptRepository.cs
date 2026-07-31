// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class GoodsReceiptRepository : DatabaseRepository
{
	private const string ReceiptColumns = "Id, PurchaseOrderId, ReceiptNumber, ReceiptDate, SupplierDeliveryNoteNumber, ReceivedByUserId, Notes, ReversedAtUtc, ReversedByUserId, ReversalReason, Version";
	private const string LineColumns = "Id, GoodsReceiptId, PurchaseOrderLineId, InventoryId, Quantity";
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

	public async Task<IReadOnlyList<GoodsReceipt>> ListByPurchaseOrderAsync(long purchaseOrderId, CancellationToken cancellationToken)
	{
		var receipts = await Database.QueryAsync(
			$"SELECT {ReceiptColumns} FROM GoodsReceipts WHERE PurchaseOrderId = $PurchaseOrderId ORDER BY ReceiptDate DESC, Id DESC;",
			ReadReceipt,
			cancellationToken,
			Parameter("$PurchaseOrderId", purchaseOrderId));
		foreach (var receipt in receipts)
		{
			receipt.Lines = await Database.QueryAsync(
				$"SELECT {LineColumns} FROM GoodsReceiptLines WHERE GoodsReceiptId = $GoodsReceiptId ORDER BY Id;",
				ReadLine,
				cancellationToken,
				Parameter("$GoodsReceiptId", receipt.Id));
		}
		return receipts;
	}

	public async Task<GoodsReceipt?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var receipt = await transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {ReceiptColumns} FROM GoodsReceipts WHERE Id = $Id;",
			ReadReceipt,
			cancellationToken,
			Parameter("$Id", id));
		if (receipt is null) return null;
		receipt.Lines = await transaction.Session.QueryAsync(
			$"SELECT {LineColumns} FROM GoodsReceiptLines WHERE GoodsReceiptId = $GoodsReceiptId ORDER BY Id;",
			ReadLine,
			cancellationToken,
			Parameter("$GoodsReceiptId", id));
		return receipt;
	}

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

	public async Task<bool> MarkReversedAsync(DatabaseTransactionContext transaction, long id, long version, DateTime reversedAtUtc, long reversedByUserId, string reversalReason, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE GoodsReceipts SET ReversedAtUtc = $ReversedAtUtc, ReversedByUserId = $ReversedByUserId, ReversalReason = $ReversalReason, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND ReversedAtUtc IS NULL;",
			cancellationToken,
			Parameter("$ReversedAtUtc", DateTimeValue(reversedAtUtc)),
			Parameter("$ReversedByUserId", reversedByUserId),
			Parameter("$ReversalReason", reversalReason),
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	private static GoodsReceipt ReadReceipt(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		PurchaseOrderId = reader.GetInt64(1),
		ReceiptNumber = reader.GetString(2),
		ReceiptDate = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
		SupplierDeliveryNoteNumber = reader.GetString(4),
		ReceivedByUserId = reader.GetInt64(5),
		Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
		ReversedAtUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
		ReversedByUserId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
		ReversalReason = reader.IsDBNull(9) ? null : reader.GetString(9),
		Version = reader.GetInt64(10)
	};

	private static GoodsReceiptLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		GoodsReceiptId = reader.GetInt64(1),
		PurchaseOrderLineId = reader.GetInt64(2),
		InventoryId = reader.GetInt64(3),
		Quantity = reader.GetInt32(4)
	};

	private static string Date(DateTime value) =>
		value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static string DateTimeValue(DateTime value) =>
		value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
