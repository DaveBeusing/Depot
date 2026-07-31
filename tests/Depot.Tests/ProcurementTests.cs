// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ProcurementTests
{
	[Fact]
	public async Task NewPurchaseOrderIsSavedAsDraft()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();

		var saved = await context.Orders.SaveDraftAsync(context.NewOrder());

		Assert.True(saved.Id > 0);
		Assert.Matches("^PO-[0-9]{6}$", saved.OrderNumber);
		Assert.Equal(PurchaseOrderStatus.Draft, saved.Status);
		Assert.Single(saved.Lines);
	}

	[Fact]
	public async Task PurchaseOrderRequiresAnActiveSupplier()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Orders.SaveDraftAsync(context.NewOrder(supplierId: context.InactiveSupplierId)));

		Assert.Contains("inactive", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM PurchaseOrders;"));
	}

	[Fact]
	public async Task PurchaseOrderRequiresAtLeastOneLine()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = context.NewOrder();
		order.Lines = [];

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.SaveDraftAsync(order));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM PurchaseOrders;"));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task PurchaseOrderLineQuantityMustBePositive(int quantity)
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			context.Orders.SaveDraftAsync(context.NewOrder(quantity: quantity)));
	}

	[Fact]
	public async Task PurchaseOrderLinePriceMustNotBeNegative()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			context.Orders.SaveDraftAsync(context.NewOrder(unitPrice: -0.01m)));
	}

	[Fact]
	public async Task InactiveItemsCannotBeOrdered()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Orders.SaveDraftAsync(context.NewOrder(itemId: context.InactiveItemId)));

		Assert.Contains("inactive", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ItemCanOnlyOccurOncePerPurchaseOrder()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = context.NewOrder();
		order.Lines =
		[
			new PurchaseOrderLine { ItemId = context.ItemId, Quantity = 1, UnitPrice = 1m },
			new PurchaseOrderLine { ItemId = context.ItemId, Quantity = 2, UnitPrice = 2m }
		];

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.SaveDraftAsync(order));
	}

	[Fact]
	public async Task ExpectedDeliveryDateCannotPrecedeOrderDate()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = context.NewOrder();
		order.ExpectedDeliveryDate = order.OrderDate.AddDays(-1);

		await Assert.ThrowsAsync<ArgumentException>(() => context.Orders.SaveDraftAsync(order));
	}

	[Fact]
	public async Task OnlyDraftPurchaseOrdersCanBeEdited()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);
		order.Notes = "Editing is no longer allowed";

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.SaveDraftAsync(order));
	}

	[Fact]
	public async Task DraftPurchaseOrderCanBeMarkedOrdered()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());

		var ordered = await context.Orders.MarkOrderedAsync(order.Id, order.Version);

		Assert.Equal(PurchaseOrderStatus.Ordered, ordered.Status);
		Assert.Equal(order.Version + 1, ordered.Version);
	}

	[Fact]
	public async Task InvalidPurchaseOrderStatusTransitionIsRejected()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			context.Orders.MarkOrderedAsync(order.Id, order.Version));
	}

	[Fact]
	public async Task OptimisticConcurrencyConflictIsDetectedWhenEditingPurchaseOrder()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var saved = await context.Orders.SaveDraftAsync(context.NewOrder());
		var current = await context.Orders.GetByIdAsync(saved.Id) ?? throw new InvalidOperationException();
		var stale = await context.Orders.GetByIdAsync(saved.Id) ?? throw new InvalidOperationException();
		current.Notes = "First editor";
		stale.Notes = "Second editor";

		await context.Orders.SaveDraftAsync(current);

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.Orders.SaveDraftAsync(stale));
	}

	[Fact]
	public async Task GoodsReceiptCanBePostedForOrderedPurchaseOrder()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 3);

		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 1));

		Assert.True(receipt.Id > 0);
		Assert.Matches("^GR-[0-9]{6}$", receipt.ReceiptNumber);
		Assert.Equal(order.Id, receipt.PurchaseOrderId);
	}

	[Fact]
	public async Task PartialGoodsReceiptUpdatesOrderStatus()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 10);

		await context.Receipts.PostAsync(context.NewReceipt(order, 4));

		var updated = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.PartiallyReceived, updated.Status);
		Assert.Equal(4, updated.Lines[0].ReceivedQuantity);
	}

	[Fact]
	public async Task CompleteGoodsReceiptUpdatesOrderStatus()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 10);

		await context.Receipts.PostAsync(context.NewReceipt(order, 4));
		await context.Receipts.PostAsync(context.NewReceipt(order, 6));

		var updated = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Received, updated.Status);
		Assert.Equal(10, updated.Lines[0].ReceivedQuantity);
	}

	[Fact]
	public async Task GoodsReceiptCannotExceedOpenQuantity()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Receipts.PostAsync(context.NewReceipt(order, 3)));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
	}

	[Fact]
	public async Task GoodsReceiptRejectsInventoryForAnotherItem()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Receipts.PostAsync(context.NewReceipt(order, 1, context.SecondInventoryId)));

		Assert.Contains("does not belong", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task GoodsReceiptRejectsInactiveOrMissingInventory(bool useInactiveInventory)
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);
		var inventoryId = useInactiveInventory ? context.InactiveInventoryId : long.MaxValue;

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Receipts.PostAsync(context.NewReceipt(order, 1, inventoryId)));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
	}

	[Fact]
	public async Task GoodsReceiptCreatesStockMovement()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 5);

		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 3));

		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE InventoryId = $InventoryId AND Quantity = 3 AND Reference = $Reference;",
			new DatabaseParameter("$InventoryId", context.InventoryId),
			new DatabaseParameter("$Reference", receipt.ReceiptNumber)));
	}

	[Fact]
	public async Task GoodsReceiptUsesTechnicalReasonCodeAfterDisplayNameChanges()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);
		await context.Data.ExecuteAsync(
			"UPDATE ReasonCodes SET Name = 'Localized inbound delivery' WHERE Code = $Code;",
			CancellationToken.None,
			new DatabaseParameter("$Code", ReasonCodeSystemCodes.GoodsReceipt));

		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 1));

		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements sm INNER JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId WHERE sm.Reference = $Reference AND rc.Code = $Code;",
			new DatabaseParameter("$Reference", receipt.ReceiptNumber),
			new DatabaseParameter("$Code", ReasonCodeSystemCodes.GoodsReceipt)));
	}

	[Fact]
	public async Task GoodsReceiptUpdatesReceivedQuantity()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 5);

		await context.Receipts.PostAsync(context.NewReceipt(order, 2));

		Assert.Equal(2, await context.ScalarAsync(
			"SELECT ReceivedQuantity FROM PurchaseOrderLines WHERE Id = $Id;",
			new DatabaseParameter("$Id", order.Lines[0].Id)));
	}

	[Fact]
	public async Task ReceiptFailureRollsBackAllBusinessAndAuditChanges()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = context.NewOrder();
		draft.Lines =
		[
			new PurchaseOrderLine { ItemId = context.ItemId, Quantity = 2, UnitPrice = 5m },
			new PurchaseOrderLine { ItemId = context.SecondItemId, Quantity = 2, UnitPrice = 6m }
		];
		var order = await context.Orders.SaveDraftAsync(draft);
		order = await context.Orders.MarkOrderedAsync(order.Id, order.Version);
		var receipt = context.NewReceipt(order, 1);
		receipt.Lines =
		[
			new GoodsReceiptLine { PurchaseOrderLineId = order.Lines[0].Id, InventoryId = context.InventoryId, Quantity = 1 },
			new GoodsReceiptLine { PurchaseOrderLineId = order.Lines[1].Id, InventoryId = context.InventoryId, Quantity = 1 }
		];
		var auditCountBefore = await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries;");

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Receipts.PostAsync(receipt));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceiptLines;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COALESCE(SUM(ReceivedQuantity), 0) FROM PurchaseOrderLines;"));
		Assert.Equal(auditCountBefore, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries;"));
		Assert.Equal((long)PurchaseOrderStatus.Ordered, await context.ScalarAsync("SELECT Status FROM PurchaseOrders;"));
	}

	[Fact]
	public async Task GoodsReceiptAuditAndBusinessPostingAreConsistent()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 3);

		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 2));

		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'GoodsReceipt' AND EntityId = $Id AND Action = 'Created' AND AfterJson IS NOT NULL;",
			new DatabaseParameter("$Id", receipt.Id)));
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM GoodsReceipts gr INNER JOIN GoodsReceiptLines grl ON grl.GoodsReceiptId = gr.Id INNER JOIN StockMovements sm ON sm.Reference = gr.ReceiptNumber WHERE gr.Id = $Id;",
			new DatabaseParameter("$Id", receipt.Id)));
	}

	[Fact]
	public async Task ConcurrentGoodsReceiptsCannotExceedOpenQuantity()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 5);

		var results = await Task.WhenAll(AttemptAsync("A"), AttemptAsync("B"));

		Assert.Single(results, result => result);
		Assert.Equal(4, await context.ScalarAsync("SELECT ReceivedQuantity FROM PurchaseOrderLines;"));
		Assert.Equal(4, await context.ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements;"));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));

		async Task<bool> AttemptAsync(string suffix)
		{
			try
			{
				var receipt = context.NewReceipt(order, 4);
				receipt.InvoiceNumber = $"INV-CONCURRENT-{suffix}";
				await context.Receipts.PostAsync(receipt);
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}
	}

	[Fact]
	public async Task GoodsReceiptRequiresInvoiceDocumentBeforeStartingTransaction()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);
		var receipt = context.NewReceipt(order, 1);
		receipt.InvoiceDocumentPath = null;

		await Assert.ThrowsAsync<ArgumentException>(() => context.Receipts.PostAsync(receipt));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
	}

	private static async Task<PurchaseOrder> CreateOrderedOrderAsync(
		ProcurementTestContext context,
		int quantity)
	{
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(quantity));
		return await context.Orders.MarkOrderedAsync(order.Id, order.Version);
	}
}
