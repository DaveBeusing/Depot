// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ProcurementTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-procurement-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task PartialAndFinalReceiptsUpdateStockLinesAndOrderAtomically()
	{
		var context = await CreateContextAsync();
		var order = await context.Orders.SaveDraftAsync(new PurchaseOrder
		{
			SupplierId = context.SupplierId, OrderDate = DateTime.Today,
			Lines = [new PurchaseOrderLine { ItemId = context.ItemId, Quantity = 10, UnitPrice = 12.50m }]
		});
		Assert.Matches("^PO-[0-9]{6}$", order.OrderNumber);
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);

		var firstReceipt = await context.Receipts.PostAsync(new GoodsReceipt
		{
			PurchaseOrderId = order.Id, InvoiceNumber = "INV-100", InvoiceDate = DateTime.Today, InvoiceDocumentPath = context.InvoicePath,
			Lines = [new GoodsReceiptLine { PurchaseOrderLineId = order.Lines[0].Id, InventoryId = context.InventoryId, Quantity = 4 }]
		});
		Assert.Matches("^GR-[0-9]{6}$", firstReceipt.ReceiptNumber);
		var partial = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.PartiallyReceived, partial.Status);
		Assert.Equal(4, partial.Lines[0].ReceivedQuantity);
		Assert.Equal(4L, await ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements;"));

		await context.Receipts.PostAsync(new GoodsReceipt
		{
			PurchaseOrderId = order.Id, InvoiceNumber = "INV-101", InvoiceDate = DateTime.Today, InvoiceDocumentPath = context.InvoicePath,
			Lines = [new GoodsReceiptLine { PurchaseOrderLineId = order.Lines[0].Id, InventoryId = context.InventoryId, Quantity = 6 }]
		});
		var received = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Received, received.Status);
		Assert.Equal(10, received.Lines[0].ReceivedQuantity);
		Assert.Equal(10L, await ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements;"));
	}

	[Fact]
	public async Task InvalidReceiptRollsBackHeaderLinesStockAndOrderQuantities()
	{
		var context = await CreateContextAsync();
		var order = await context.Orders.SaveDraftAsync(new PurchaseOrder { SupplierId = context.SupplierId, Lines = [new PurchaseOrderLine { ItemId = context.ItemId, Quantity = 2, UnitPrice = 5m }] });
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Receipts.PostAsync(new GoodsReceipt
		{
			PurchaseOrderId = order.Id, InvoiceNumber = "INV-OVER", InvoiceDate = DateTime.Today, InvoiceDocumentPath = context.InvoicePath,
			Lines = [new GoodsReceiptLine { PurchaseOrderLineId = order.Lines[0].Id, InventoryId = context.InventoryId, Quantity = 3 }]
		}));
		Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
		Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM GoodsReceiptLines;"));
		Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM StockMovements;"));
		Assert.Equal(0L, await ScalarAsync("SELECT ReceivedQuantity FROM PurchaseOrderLines;"));
		Assert.Equal((long)PurchaseOrderStatus.Ordered, await ScalarAsync("SELECT Status FROM PurchaseOrders;"));
	}

	[Fact]
	public async Task ConcurrentReceiptsCannotOverReceiveTheSameOrderLine()
	{
		var context = await CreateContextAsync();
		var order = await context.Orders.SaveDraftAsync(new PurchaseOrder { SupplierId = context.SupplierId, Lines = [new PurchaseOrderLine { ItemId = context.ItemId, Quantity = 5, UnitPrice = 8m }] });
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);
		var lineId = order.Lines[0].Id;
		var results = await Task.WhenAll(AttemptAsync("INV-A"), AttemptAsync("INV-B"));
		Assert.Single(results, result => result);
		Assert.Equal(4L, await ScalarAsync("SELECT ReceivedQuantity FROM PurchaseOrderLines;"));
		Assert.Equal(4L, await ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements;"));
		Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));

		async Task<bool> AttemptAsync(string invoice)
		{
			try
			{
				await context.Receipts.PostAsync(new GoodsReceipt { PurchaseOrderId = order.Id, InvoiceNumber = invoice, InvoiceDate = DateTime.Today, InvoiceDocumentPath = context.InvoicePath, Lines = [new GoodsReceiptLine { PurchaseOrderLineId = lineId, InventoryId = context.InventoryId, Quantity = 4 }] });
				return true;
			}
			catch (InvalidOperationException) { return false; }
		}
	}

	[Fact]
	public async Task GoodsReceiptRequiresAnInvoiceBeforeStartingTheTransaction()
	{
		var context = await CreateContextAsync();
		await Assert.ThrowsAsync<ArgumentException>(() => context.Receipts.PostAsync(new GoodsReceipt { PurchaseOrderId = 1, InvoiceNumber = "INV-MISSING-DOCUMENT", Lines = [new GoodsReceiptLine { PurchaseOrderLineId = 1, InventoryId = context.InventoryId, Quantity = 1 }] }));
		Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var data = new DatabaseAccess(factory);
		var supplierId = await data.InsertAsync("INSERT INTO Suppliers (SupplierNumber, AccountNumber, Name) VALUES ('TEST-SUPPLIER', 1000, 'Test Supplier');", CancellationToken.None);
		var itemId = await data.InsertAsync("INSERT INTO Items (PartNumber, Description) VALUES ('PO-ITEM', 'Procurement test item');", CancellationToken.None);
		var purposeId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT Id FROM Purposes WHERE Name = 'Stock';", CancellationToken.None));
		var locationId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT Id FROM StorageLocations LIMIT 1;", CancellationToken.None));
		var inventoryId = await data.InsertAsync("INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive) VALUES ($ItemId, $PurposeId, $StorageLocationId, 1);", CancellationToken.None, new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$PurposeId", purposeId), new DatabaseParameter("$StorageLocationId", locationId));
		var authorization = new AuthorizationService();
		var admin = new UserRepository(data).GetByEmail("admin@depot.local") ?? throw new InvalidOperationException();
		authorization.SignIn(admin);
		var audit = new AuditService(new AuditRepository(data), authorization);
		var orderService = new PurchaseOrderService(new PurchaseOrderRepository(data), new SupplierRepository(data), new ItemRepository(data), audit);
		var receiptService = new GoodsReceiptService(new GoodsReceiptRepository(data), audit);
		var invoicePath = _databasePath + ".invoice.pdf";
		await File.WriteAllTextAsync(invoicePath, "test invoice");
		return new TestContext(orderService, receiptService, supplierId, itemId, inventoryId, invoicePath);
	}

	private async Task<long> ScalarAsync(string sql)
	{
		var data = new DatabaseAccess(new SqliteConnectionFactory(_databasePath));
		return Convert.ToInt64(await data.ExecuteScalarAsync(sql, CancellationToken.None));
	}

	public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_databasePath)) File.Delete(_databasePath); if (File.Exists(_databasePath + ".invoice.pdf")) File.Delete(_databasePath + ".invoice.pdf"); }
	private sealed record TestContext(PurchaseOrderService Orders, GoodsReceiptService Receipts, long SupplierId, long ItemId, long InventoryId, string InvoicePath);
}
