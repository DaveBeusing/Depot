// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SupplierReturnTests
{
    [Fact]
    public async Task DraftCanBeSavedAndCancelled()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (_, receipt) = await CreateReceiptAsync(context, 5);
        var saved = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 2));
        Assert.Equal(SupplierReturnStatus.Draft, saved.Status);
        Assert.StartsWith("SR-", saved.ReturnNumber, StringComparison.Ordinal);
        Assert.Equal(SupplierReturnStatus.Cancelled, (await context.SupplierReturns.CancelAsync(saved.Id, saved.Version)).Status);
    }

    [Fact]
    public async Task PostingCreatesNegativeMovementAndLeavesReceiptHistoryUnchanged()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (order, receipt) = await CreateReceiptAsync(context, 6);
        var saved = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 2));
        var posted = await context.SupplierReturns.PostSupplierReturnAsync(saved.Id, saved.Version);
        Assert.Equal(SupplierReturnStatus.Posted, posted.Status);
        Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference AND MovementType = $Type AND Quantity = -2 AND ReversalOfMovementId IS NULL;", new DatabaseParameter("$Reference", $"Supplier Return {saved.ReturnNumber}"), new DatabaseParameter("$Type", (int)StockMovementType.SupplierReturn)));
        Assert.Equal(6, await context.ScalarAsync("SELECT ReceivedQuantity FROM PurchaseOrderLines WHERE Id = $Id;", new DatabaseParameter("$Id", order.Lines[0].Id)));
        Assert.Equal(6, await context.ScalarAsync("SELECT Quantity FROM GoodsReceiptLines WHERE Id = $Id;", new DatabaseParameter("$Id", receipt.Lines[0].Id)));
        Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'SupplierReturn' AND EntityId = $Id AND Action = 'Updated';", new DatabaseParameter("$Id", saved.Id)));
    }

    [Fact]
	public async Task PreviouslyReturnedQuantityAndAvailableStockAreEnforced()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (_, receipt) = await CreateReceiptAsync(context, 5);
        var first = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 3));
        await context.SupplierReturns.PostSupplierReturnAsync(first.Id, first.Version);
        var available = Assert.Single(await context.SupplierReturns.GetReturnableLinesAsync(receipt.Id));
        Assert.Equal(3, available.AlreadyReturnedQuantity);
        Assert.Equal(2, available.ReturnableQuantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SupplierReturns.SaveDraftAsync(NewReturn(context, receipt, 3, available)));
	}

	[Fact]
	public async Task ReceiptLineCannotBeReturnedThroughAnotherInventoryOrItem()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var (_, receipt) = await CreateReceiptAsync(context, 3);
		var value = await NewReturnAsync(context, receipt, 1);
		value.Lines[0].InventoryId = context.SecondInventoryId;
		value.Lines[0].ItemId = context.SecondItemId;
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.SupplierReturns.SaveDraftAsync(value));
	}

	[Fact]
	public async Task ConcurrentReturnsCannotExceedNetReceivedQuantity()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var (_, receipt) = await CreateReceiptAsync(context, 5);
		var first = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 4));
		var second = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 4));
		var results = await Task.WhenAll(
			CaptureAsync(() => context.SupplierReturns.PostSupplierReturnAsync(first.Id, first.Version)),
			CaptureAsync(() => context.SupplierReturns.PostSupplierReturnAsync(second.Id, second.Version)));
		Assert.Single(results, result => result is null);
		Assert.Single(results, result => result is not null);
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM SupplierReturns WHERE Status = $Posted;", new DatabaseParameter("$Posted", (int)SupplierReturnStatus.Posted)));
	}

    [Fact]
    public async Task PostingRollsBackWhenStockIsInsufficient()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (_, receipt) = await CreateReceiptAsync(context, 4);
        var saved = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 4));
        var issue = await context.MaterialIssues.SaveDraftAsync(new MaterialIssue { IssueDate = DateTime.Today, Recipient = $"Material issue test {Guid.NewGuid():N}", Lines = [new MaterialIssueLine { InventoryId = context.InventoryId, Quantity = 4, ReasonCodeId = await ReasonIdAsync(context, ReasonCodeSystemCodes.GoodsIssue) }] });
        await context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version);
        await Assert.ThrowsAsync<InsufficientStockException>(() => context.SupplierReturns.PostSupplierReturnAsync(saved.Id, saved.Version));
        Assert.Equal(SupplierReturnStatus.Draft, (await context.SupplierReturns.GetByIdAsync(saved.Id))?.Status);
        Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;", new DatabaseParameter("$Reference", $"Supplier Return {saved.ReturnNumber}")));
    }

    [Fact]
    public async Task AuditFailureRollsBackPosting()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (_, receipt) = await CreateReceiptAsync(context, 4);
        var saved = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 2));
        await context.Data.ExecuteAsync("CREATE TRIGGER FailSupplierReturnAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'SupplierReturn' AND NEW.Action = 'Updated' BEGIN SELECT RAISE(ABORT, 'forced supplier return audit failure'); END;", CancellationToken.None);
        await Assert.ThrowsAsync<SqliteException>(() => context.SupplierReturns.PostSupplierReturnAsync(saved.Id, saved.Version));
        Assert.Equal(SupplierReturnStatus.Draft, (await context.SupplierReturns.GetByIdAsync(saved.Id))?.Status);
        Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;", new DatabaseParameter("$Reference", $"Supplier Return {saved.ReturnNumber}")));
    }

    [Fact]
    public async Task PostedReturnIsImmutableAndStaleVersionIsRejected()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (_, receipt) = await CreateReceiptAsync(context, 3);
        var saved = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 1));
        var posted = await context.SupplierReturns.PostSupplierReturnAsync(saved.Id, saved.Version);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.SupplierReturns.PostSupplierReturnAsync(saved.Id, saved.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SupplierReturns.SaveDraftAsync(posted));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SupplierReturns.CancelAsync(posted.Id, posted.Version));
    }

    [Fact]
    public async Task ReversalCreatesCounterMovementAndRestoresReturnableQuantity()
    {
        await using var context = await ProcurementTestContext.CreateSqliteAsync();
        var (_, receipt) = await CreateReceiptAsync(context, 5);
        var saved = await context.SupplierReturns.SaveDraftAsync(await NewReturnAsync(context, receipt, 2));
        var posted = await context.SupplierReturns.PostSupplierReturnAsync(saved.Id, saved.Version);
        var reasonId = await ReasonIdAsync(context, ReasonCodeSystemCodes.InventoryCorrection);
        var reversals = await context.SupplierReturns.ReverseAsync(posted.Id, posted.Version, reasonId, "Supplier accepted cancellation");
        Assert.Single(reversals);
        Assert.Equal(2, reversals[0].Quantity);
        var reloaded = await context.SupplierReturns.GetByIdAsync(posted.Id);
        Assert.True(reloaded?.IsReversed);
        Assert.Equal(5, Assert.Single(await context.SupplierReturns.GetReturnableLinesAsync(receipt.Id)).ReturnableQuantity);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => context.SupplierReturns.ReverseAsync(posted.Id, posted.Version, reasonId, "Duplicate"));
    }

    private static async Task<(PurchaseOrder Order, GoodsReceipt Receipt)> CreateReceiptAsync(ProcurementTestContext context, int quantity)
    {
        var order = await context.Orders.SaveDraftAsync(context.NewOrder(quantity));
        order = await context.ApproveAndOrderAsync(order);
        var receipt = await context.Receipts.PostGoodsReceiptAsync(context.NewReceipt(order, quantity));
        return (order, receipt);
    }

    private static async Task<SupplierReturn> NewReturnAsync(ProcurementTestContext context, GoodsReceipt receipt, int quantity)
    {
        var available = Assert.Single(await context.SupplierReturns.GetReturnableLinesAsync(receipt.Id));
        return NewReturn(context, receipt, quantity, available, await ReasonIdAsync(context, ReasonCodeSystemCodes.Returned));
    }

    private static SupplierReturn NewReturn(ProcurementTestContext context, GoodsReceipt receipt, int quantity, SupplierReturnableLine available, long reasonCodeId = 1) => new()
    {
        SupplierId = context.SupplierId,
        PurchaseOrderId = receipt.PurchaseOrderId,
        GoodsReceiptId = receipt.Id,
        ReturnDate = DateTime.Today,
        SupplierReference = "RMA-TEST",
        Notes = "Supplier return integration test",
        Lines = [new SupplierReturnLine { GoodsReceiptLineId = available.GoodsReceiptLineId, InventoryId = available.InventoryId, ItemId = available.ItemId, Quantity = quantity, ReasonCodeId = reasonCodeId }]
    };

	private static async Task<long> ReasonIdAsync(ProcurementTestContext context, string code) => Convert.ToInt64(await context.Data.ExecuteScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", CancellationToken.None, new DatabaseParameter("$Code", code)));

	private static async Task<Exception?> CaptureAsync(Func<Task<SupplierReturn>> action)
	{
		try { await action(); return null; }
		catch (Exception exception) { return exception; }
	}
}
