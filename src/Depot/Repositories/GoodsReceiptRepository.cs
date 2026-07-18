// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Services;

namespace Depot.Repositories;

public sealed class GoodsReceiptRepository : DatabaseRepository
{
	public GoodsReceiptRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<ReceiptInventoryOption>> ListInventoryOptionsAsync(long itemId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"SELECT inv.Id, inv.ItemId, w.Name, sl.Name, p.Name FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId INNER JOIN Warehouses w ON w.Id = sl.WarehouseId INNER JOIN Purposes p ON p.Id = inv.PurposeId WHERE inv.ItemId = $ItemId AND inv.IsActive = 1 AND sl.IsActive = 1 AND w.IsActive = 1 ORDER BY w.Name, sl.Name, p.Name;",
			reader => new ReceiptInventoryOption { InventoryId = reader.GetInt64(0), ItemId = reader.GetInt64(1), DisplayName = $"{reader.GetString(2)} / {reader.GetString(3)} / {reader.GetString(4)}" },
			cancellationToken, Parameter("$ItemId", itemId));

	public Task<GoodsReceipt> PostAsync(GoodsReceipt receipt, AuditEntry auditEntry, CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			if (await session.ExecuteScalarAsync(Database.PurchaseOrderLockSql, token, Parameter("$PurchaseOrderId", receipt.PurchaseOrderId)) is null)
				throw new InvalidOperationException("The purchase order was not found.");
			var status = Convert.ToInt32(await session.ExecuteScalarAsync("SELECT Status FROM PurchaseOrders WHERE Id = $PurchaseOrderId;", token, Parameter("$PurchaseOrderId", receipt.PurchaseOrderId)), CultureInfo.InvariantCulture);
			if ((PurchaseOrderStatus)status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
				throw new InvalidOperationException("Only ordered or partially received purchase orders can be received.");

			var temporaryNumber = $"PENDING-{Guid.NewGuid():N}";
			receipt.Id = await session.InsertAsync(
				"INSERT INTO GoodsReceipts (ReceiptNumber, PurchaseOrderId, ReceiptDate, InvoiceNumber, InvoiceDate, InvoiceDocumentPath, Notes) VALUES ($ReceiptNumber, $PurchaseOrderId, $ReceiptDate, $InvoiceNumber, $InvoiceDate, $InvoiceDocumentPath, $Notes);",
				token, Parameter("$ReceiptNumber", temporaryNumber), Parameter("$PurchaseOrderId", receipt.PurchaseOrderId), Parameter("$ReceiptDate", Date(receipt.ReceiptDate)), Parameter("$InvoiceNumber", receipt.InvoiceNumber), Parameter("$InvoiceDate", Date(receipt.InvoiceDate)), Parameter("$InvoiceDocumentPath", receipt.InvoiceDocumentPath), Parameter("$Notes", receipt.Notes));
			receipt.ReceiptNumber = $"GR-{receipt.Id:000000}";
			await session.ExecuteAsync("UPDATE GoodsReceipts SET ReceiptNumber = $ReceiptNumber WHERE Id = $Id;", token, Parameter("$ReceiptNumber", receipt.ReceiptNumber), Parameter("$Id", receipt.Id));

			foreach (var line in receipt.Lines)
			{
				var orderLine = await session.QuerySingleOrDefaultAsync(
					"SELECT ItemId, Quantity, ReceivedQuantity, UnitPrice FROM PurchaseOrderLines WHERE Id = $Id AND PurchaseOrderId = $PurchaseOrderId;",
					reader => new ReceiptOrderLine(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetDecimal(3)), token, Parameter("$Id", line.PurchaseOrderLineId), Parameter("$PurchaseOrderId", receipt.PurchaseOrderId))
					?? throw new InvalidOperationException("A purchase order line was not found.");
				if (line.Quantity <= 0 || orderLine.ReceivedQuantity + line.Quantity > orderLine.Quantity)
					throw new InvalidOperationException("Receipt quantity exceeds the open purchase order quantity.");
				if (await session.ExecuteScalarAsync(Database.InventoryLockSql, token, Parameter("$InventoryId", line.InventoryId)) is null)
					throw new InvalidOperationException("The destination inventory was not found.");
				var inventoryItemId = Convert.ToInt64(await session.ExecuteScalarAsync("SELECT ItemId FROM Inventories WHERE Id = $InventoryId AND IsActive = 1;", token, Parameter("$InventoryId", line.InventoryId)), CultureInfo.InvariantCulture);
				if (inventoryItemId != orderLine.ItemId) throw new InvalidOperationException("The destination inventory does not belong to the ordered item.");

				line.GoodsReceiptId = receipt.Id;
				line.Id = await session.InsertAsync("INSERT INTO GoodsReceiptLines (GoodsReceiptId, PurchaseOrderLineId, InventoryId, Quantity) VALUES ($GoodsReceiptId, $PurchaseOrderLineId, $InventoryId, $Quantity);", token, Parameter("$GoodsReceiptId", receipt.Id), Parameter("$PurchaseOrderLineId", line.PurchaseOrderLineId), Parameter("$InventoryId", line.InventoryId), Parameter("$Quantity", line.Quantity));
				var updated = await session.ExecuteAsync("UPDATE PurchaseOrderLines SET ReceivedQuantity = ReceivedQuantity + $Quantity, Version = Version + 1 WHERE Id = $Id AND ReceivedQuantity + $Quantity <= Quantity;", token, Parameter("$Quantity", line.Quantity), Parameter("$Id", line.PurchaseOrderLineId));
				if (updated != 1) throw new ConcurrencyConflictException("purchase order line receipt");
				await session.InsertAsync(
					"INSERT INTO StockMovements (InventoryId, ReasonCodeId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes) VALUES ($InventoryId, (SELECT Id FROM ReasonCodes WHERE Name = $ReasonCode), $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference, $Notes);",
					token, Parameter("$InventoryId", line.InventoryId), Parameter("$ReasonCode", "Goods Receipt"), Parameter("$MovementType", (int)StockMovementType.Purchase), Parameter("$TimestampUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), Parameter("$Quantity", line.Quantity), Parameter("$UnitPrice", orderLine.UnitPrice), Parameter("$Reference", receipt.ReceiptNumber), Parameter("$Notes", $"Invoice {receipt.InvoiceNumber}"));
			}

			var openLines = Convert.ToInt64(await session.ExecuteScalarAsync("SELECT COUNT(*) FROM PurchaseOrderLines WHERE PurchaseOrderId = $PurchaseOrderId AND ReceivedQuantity < Quantity;", token, Parameter("$PurchaseOrderId", receipt.PurchaseOrderId)), CultureInfo.InvariantCulture);
			var newStatus = openLines == 0 ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
			await session.ExecuteAsync("UPDATE PurchaseOrders SET Status = $Status, Version = Version + 1 WHERE Id = $PurchaseOrderId;", token, Parameter("$Status", (int)newStatus), Parameter("$PurchaseOrderId", receipt.PurchaseOrderId));
			auditEntry.EntityId = receipt.Id;
			auditEntry.AfterJson = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			await session.ExecuteAsync(
				"INSERT INTO AuditEntries (TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson) VALUES ($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);",
				token, Parameter("$TimestampUtc", auditEntry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$UserId", auditEntry.UserId), Parameter("$UserEmail", auditEntry.UserEmail), Parameter("$EntityType", auditEntry.EntityType), Parameter("$EntityId", auditEntry.EntityId), Parameter("$Action", auditEntry.Action), Parameter("$BeforeJson", auditEntry.BeforeJson), Parameter("$AfterJson", auditEntry.AfterJson));
			return receipt;
		}, cancellationToken);

	private static string Date(DateTime value) => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private sealed record ReceiptOrderLine(long ItemId, int Quantity, int ReceivedQuantity, decimal UnitPrice);
}
