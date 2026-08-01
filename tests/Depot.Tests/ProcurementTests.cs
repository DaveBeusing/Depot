// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ProcurementTests
{
	[Fact]
	public async Task OrderedPurchaseOrderCanBeClosedWithRequiredMetadataAndAtomicAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.ApproveAndOrderAsync(await context.Orders.SaveDraftAsync(context.NewOrder(10)));

		var closed = await context.Orders.CloseAsync(order.Id, order.Version, "Supplier discontinued the remaining delivery");
		var stored = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();

		Assert.Equal(PurchaseOrderStatus.Closed, closed.Status);
		Assert.Equal(context.Authorization.CurrentUser?.Id, closed.ClosedByUserId);
		Assert.NotNull(closed.ClosedAtUtc);
		Assert.Equal("Supplier discontinued the remaining delivery", closed.CloseReason);
		Assert.Equal(0, Assert.Single(stored.Lines).ReceivedQuantity);
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId = $Id AND Action = 'Updated' AND AfterJson LIKE '%supplier discontinued%';",
			new DatabaseParameter("$Id", order.Id)));
	}

	[Fact]
	public async Task PartiallyReceivedPurchaseOrderCanBeClosedWithoutChangingOpenQuantityAndRejectsFurtherReceipts()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.ApproveAndOrderAsync(await context.Orders.SaveDraftAsync(context.NewOrder(10)));
		await context.Receipts.PostAsync(context.NewReceipt(order, 4));
		var partial = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();

		var closed = await context.Orders.CloseAsync(partial.Id, partial.Version, "Accept the partial delivery as final");

		Assert.Equal(PurchaseOrderStatus.Closed, closed.Status);
		Assert.Equal(4, Assert.Single(closed.Lines).ReceivedQuantity);
		Assert.Equal(6, closed.Lines[0].OpenQuantity);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Receipts.PostAsync(context.NewReceipt(closed, 1)));
		var stored = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Closed, stored.Status);
		Assert.Equal(4, Assert.Single(stored.Lines).ReceivedQuantity);
	}

	[Fact]
	public async Task ClosingRequiresReasonAndAllowedStatusAndHonorsConcurrency()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		await Assert.ThrowsAsync<ArgumentException>(() => context.Orders.CloseAsync(draft.Id, draft.Version, "  "));
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.CloseAsync(draft.Id, draft.Version, "Not ordered"));

		var ordered = await context.ApproveAndOrderAsync(draft);
		await context.Orders.CloseAsync(ordered.Id, ordered.Version, "No longer required");
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.Orders.CloseAsync(ordered.Id, ordered.Version, "Stale close"));
	}

	[Fact]
	public async Task ClosingRollsBackWhenAuditWriteFails()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.ApproveAndOrderAsync(await context.Orders.SaveDraftAsync(context.NewOrder()));
		await context.Data.ExecuteAsync(
			"CREATE TRIGGER FailCloseAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'PurchaseOrder' BEGIN SELECT RAISE(ABORT, 'forced close audit failure'); END;",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => context.Orders.CloseAsync(order.Id, order.Version, "Must roll back"));

		var stored = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Ordered, stored.Status);
		Assert.Null(stored.ClosedByUserId);
		Assert.Null(stored.ClosedAtUtc);
		Assert.Null(stored.CloseReason);
	}

	[Fact]
	public async Task PurchaseOrderWithPostedReceiptCannotBeCancelled()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.ApproveAndOrderAsync(await context.Orders.SaveDraftAsync(context.NewOrder(10)));
		await context.Receipts.PostAsync(context.NewReceipt(order, 4));
		var partial = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.CancelAsync(partial.Id, partial.Version));
		Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await context.Orders.GetByIdAsync(order.Id))?.Status);
	}

	[Fact]
	public async Task GoodsReceiptReversalRestoresStockReceivedQuantityAndOrderStatusAtomically()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(5));
		order = await context.ApproveAndOrderAsync(order);
		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 5));
		var reasonCodeId = await context.ScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", new DatabaseParameter("$Code", ReasonCodeSystemCodes.Returned));

		var reversed = await context.Receipts.ReverseAsync(receipt.Id, receipt.Version, reasonCodeId, "Supplier delivery rejected");
		var storedOrder = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();

		Assert.True(reversed.IsReversed);
		Assert.Equal(PurchaseOrderStatus.Ordered, storedOrder.Status);
		Assert.Equal(0, storedOrder.Lines[0].ReceivedQuantity);
		Assert.Equal(0, await context.ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;", new DatabaseParameter("$InventoryId", context.InventoryId)));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements reversal INNER JOIN StockMovements original ON original.Id = reversal.ReversalOfMovementId WHERE original.Reference = $Reference AND reversal.Quantity = -original.Quantity;", new DatabaseParameter("$Reference", receipt.ReceiptNumber)));
		await Assert.ThrowsAnyAsync<InvalidOperationException>(() => context.Receipts.ReverseAsync(receipt.Id, receipt.Version, reasonCodeId, "Duplicate"));
	}

	[Fact]
	public async Task GoodsReceiptReversalRollsBackWhenAuditFails()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(3));
		order = await context.ApproveAndOrderAsync(order);
		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 3));
		var reasonCodeId = await context.ScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", new DatabaseParameter("$Code", ReasonCodeSystemCodes.Returned));
		await context.Data.ExecuteAsync(
			"CREATE TRIGGER FailReceiptReversalAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'GoodsReceipt' AND NEW.Action = 'Updated' BEGIN SELECT RAISE(ABORT, 'forced receipt reversal audit failure'); END;",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => context.Receipts.ReverseAsync(receipt.Id, receipt.Version, reasonCodeId, "Must roll back"));

		var storedOrder = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Received, storedOrder.Status);
		Assert.Equal(3, storedOrder.Lines[0].ReceivedQuantity);
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId IS NOT NULL;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts WHERE ReversedAtUtc IS NOT NULL;"));
	}

	[Fact]
	public async Task GoodsReceiptReversalIsRejectedWhenReceivedStockWasAlreadyConsumed()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(5));
		order = await context.ApproveAndOrderAsync(order);
		var receipt = await context.Receipts.PostAsync(context.NewReceipt(order, 5));
		await context.Data.InsertAsync(
			"INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, Reference) VALUES ($InventoryId, $MovementType, $TimestampUtc, -5, 'CONSUMED');",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", context.InventoryId),
			new DatabaseParameter("$MovementType", (int)StockMovementType.Withdrawal),
			new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O")));
		var reasonCodeId = await context.ScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", new DatabaseParameter("$Code", ReasonCodeSystemCodes.Returned));

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Receipts.ReverseAsync(receipt.Id, receipt.Version, reasonCodeId, "Stock no longer available"));

		var storedOrder = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(5, storedOrder.Lines[0].ReceivedQuantity);
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId IS NOT NULL;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts WHERE ReversedAtUtc IS NOT NULL;"));
	}
	[Fact]
	public async Task NewPurchaseOrderIsSavedAsDraft()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();

		var saved = await context.Orders.SaveDraftAsync(context.NewOrder());

		Assert.True(saved.Id > 0);
		Assert.Matches("^PO-[0-9]{6}$", saved.OrderNumber);
		Assert.Equal(PurchaseOrderStatus.Draft, saved.Status);
		Assert.Single(saved.Lines);
		Assert.Equal(context.Authorization.CurrentUser?.Id, saved.CreatedByUserId);
	}

	[Fact]
	public async Task ApprovalWorkflowCapturesDecisionAndPreventsSelfApproval()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());

		var pending = await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);

		Assert.Equal(PurchaseOrderStatus.PendingApproval, pending.Status);
		Assert.Equal(context.Authorization.CurrentUser?.Id, pending.SubmittedByUserId);
		Assert.NotNull(pending.SubmittedAtUtc);
		pending.Notes = "Pending orders are immutable";
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.SaveDraftAsync(pending));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Orders.ApproveAsync(pending.Id, pending.Version, "Self approval"));

		context.SignInApprover();
		var approved = await context.Orders.ApproveAsync(pending.Id, pending.Version, "Budget checked");

		Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
		Assert.Equal(context.ApproverUserId, approved.ApprovalDecisionByUserId);
		Assert.NotNull(approved.ApprovalDecisionAtUtc);
		Assert.Equal("Budget checked", approved.ApprovalComment);
		context.SignInAdministrator();
		var ordered = await context.Orders.MarkOrderedAsync(approved.Id, approved.Version);
		Assert.Equal(PurchaseOrderStatus.Ordered, ordered.Status);
	}

	[Fact]
	public async Task ApprovalRequiresExplicitPermissionAndHonorsConcurrency()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		var pending = await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);
		context.Authorization.SignIn(new User
		{
			Id = context.ApproverUserId,
			Email = "unprivileged@depot.test",
			DisplayName = "Unprivileged user",
			IsActive = true
		});

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
			context.Orders.ApproveAsync(pending.Id, pending.Version));

		context.SignInApprover();
		var approved = await context.Orders.ApproveAsync(pending.Id, pending.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			context.Orders.RejectAsync(pending.Id, pending.Version, "Stale rejection"));
		Assert.Equal(PurchaseOrderStatus.Approved,
			(await context.Orders.GetByIdAsync(approved.Id))?.Status);
	}

	[Fact]
	public async Task RejectedOrderCanBeReopenedOrCancelled()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		var pending = await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);
		context.SignInApprover();
		var rejected = await context.Orders.RejectAsync(pending.Id, pending.Version, "Please revise quantities");
		context.SignInAdministrator();

		var reopened = await context.Orders.ReopenRejectedAsync(rejected.Id, rejected.Version);

		Assert.Equal(PurchaseOrderStatus.Draft, reopened.Status);
		Assert.Null(reopened.SubmittedByUserId);
		Assert.Null(reopened.ApprovalDecisionByUserId);
		Assert.Null(reopened.ApprovalComment);

		pending = await context.Orders.SubmitForApprovalAsync(reopened.Id, reopened.Version);
		context.SignInApprover();
		rejected = await context.Orders.RejectAsync(pending.Id, pending.Version);
		context.SignInAdministrator();
		var cancelled = await context.Orders.CancelAsync(rejected.Id, rejected.Version);
		Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
	}

	[Fact]
	public async Task ApprovalAuditFailureRollsBackDecision()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		var pending = await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);
		context.SignInApprover();
		await context.Data.ExecuteAsync(
			"CREATE TRIGGER FailApprovalAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'PurchaseOrder' BEGIN SELECT RAISE(ABORT, 'forced approval audit failure'); END;",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() =>
			context.Orders.ApproveAsync(pending.Id, pending.Version, "Must roll back"));

		var unchanged = await context.Orders.GetByIdAsync(pending.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.PendingApproval, unchanged.Status);
		Assert.Equal(pending.Version, unchanged.Version);
		Assert.Null(unchanged.ApprovalDecisionByUserId);
	}

	[Fact]
	public async Task PurchaseOrderCreateEditOrderAndCancelCommitCorrectAuditStates()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());
		order.Notes = "Updated draft";
		order = await context.Orders.SaveDraftAsync(order);
		order = await context.ApproveAndOrderAsync(order);
		order = await context.Orders.CancelAsync(order.Id, order.Version);

		var auditStates = await context.Data.QueryAsync(
			"SELECT BeforeJson, AfterJson FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId = $Id ORDER BY Id;",
			reader => new AuditState(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1)),
			CancellationToken.None,
			new DatabaseParameter("$Id", order.Id));

		Assert.Equal(6, auditStates.Count);
		Assert.Null(auditStates[0].BeforeJson);
		Assert.Equal(order.Id, JsonInt64(auditStates[0].AfterJson, "id"));
		Assert.Null(JsonString(auditStates[0].AfterJson, "notes"));
		Assert.Null(JsonString(RequiredJson(auditStates[1].BeforeJson), "notes"));
		Assert.Equal("Updated draft", JsonString(auditStates[1].AfterJson, "notes"));
		Assert.Equal((int)PurchaseOrderStatus.Draft, JsonInt32(RequiredJson(auditStates[2].BeforeJson), "status"));
		Assert.Equal((int)PurchaseOrderStatus.PendingApproval, JsonInt32(auditStates[2].AfterJson, "status"));
		Assert.Equal((int)PurchaseOrderStatus.PendingApproval, JsonInt32(RequiredJson(auditStates[3].BeforeJson), "status"));
		Assert.Equal((int)PurchaseOrderStatus.Approved, JsonInt32(auditStates[3].AfterJson, "status"));
		Assert.Equal((int)PurchaseOrderStatus.Approved, JsonInt32(RequiredJson(auditStates[4].BeforeJson), "status"));
		Assert.Equal((int)PurchaseOrderStatus.Ordered, JsonInt32(auditStates[4].AfterJson, "status"));
		Assert.Equal((int)PurchaseOrderStatus.Ordered, JsonInt32(RequiredJson(auditStates[5].BeforeJson), "status"));
		Assert.Equal((int)PurchaseOrderStatus.Cancelled, JsonInt32(auditStates[5].AfterJson, "status"));
	}

	[Fact]
	public async Task AuditFailureRollsBackPurchaseOrderCreationAndLines()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		UseInvalidAuditUser(context);

		await Assert.ThrowsAsync<SqliteException>(() => context.Orders.SaveDraftAsync(context.NewOrder()));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM PurchaseOrders;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM PurchaseOrderLines;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder';"));
	}

	[Fact]
	public async Task AuditFailureRollsBackPurchaseOrderEditAndStatusChanges()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var administrator = context.Authorization.CurrentUser ?? throw new InvalidOperationException();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());
		var originalVersion = order.Version;
		order.Notes = "Must be rolled back";
		UseInvalidAuditUser(context);

		await Assert.ThrowsAsync<SqliteException>(() => context.Orders.SaveDraftAsync(order));

		var unchanged = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Null(unchanged.Notes);
		Assert.Equal(originalVersion, unchanged.Version);
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder';"));

		await Assert.ThrowsAsync<SqliteException>(() => context.Orders.SubmitForApprovalAsync(unchanged.Id, unchanged.Version));
		unchanged = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Draft, unchanged.Status);
		Assert.Equal(originalVersion, unchanged.Version);

		context.Authorization.SignIn(administrator);
		var ordered = await context.ApproveAndOrderAsync(unchanged);
		var orderedVersion = ordered.Version;
		UseInvalidAuditUser(context);
		await Assert.ThrowsAsync<SqliteException>(() => context.Orders.CancelAsync(ordered.Id, ordered.Version));

		var stillOrdered = await context.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Ordered, stillOrdered.Status);
		Assert.Equal(orderedVersion, stillOrdered.Version);
		Assert.Equal(4, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder';"));
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
		order = await context.ApproveAndOrderAsync(order);
		order.Notes = "Editing is no longer allowed";

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Orders.SaveDraftAsync(order));
	}

	[Fact]
	public async Task PurchaseOrderRequiresApprovalBeforeItCanBeMarkedOrdered()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			context.Orders.MarkOrderedAsync(order.Id, order.Version));

		var ordered = await context.ApproveAndOrderAsync(order);

		Assert.Equal(PurchaseOrderStatus.Ordered, ordered.Status);
		Assert.Equal(order.Version + 3, ordered.Version);
	}

	[Fact]
	public async Task InvalidPurchaseOrderStatusTransitionIsRejected()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await context.Orders.SaveDraftAsync(context.NewOrder());
		order = await context.ApproveAndOrderAsync(order);

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
		var auditCount = await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder';");

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.Orders.SaveDraftAsync(stale));
		Assert.Equal(auditCount, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder';"));
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
		Assert.Equal(context.Authorization.CurrentUser?.Id, receipt.ReceivedByUserId);
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM GoodsReceipts WHERE Id = $Id AND SupplierDeliveryNoteNumber = $DeliveryNote AND ReceivedByUserId = $UserId AND InvoiceNumber IS NULL AND InvoiceDate IS NULL AND InvoiceDocumentPath IS NULL;",
			new DatabaseParameter("$Id", receipt.Id),
			new DatabaseParameter("$DeliveryNote", receipt.SupplierDeliveryNoteNumber),
			new DatabaseParameter("$UserId", receipt.ReceivedByUserId)));
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
		order = await context.ApproveAndOrderAsync(order);
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
		var auditJson = await context.Data.QuerySingleOrDefaultAsync(
			"SELECT AfterJson FROM AuditEntries WHERE EntityType = 'GoodsReceipt' AND EntityId = $Id;",
			reader => reader.GetString(0),
			CancellationToken.None,
			new DatabaseParameter("$Id", receipt.Id)) ?? throw new InvalidOperationException();
		using var auditDocument = JsonDocument.Parse(auditJson);
		var after = auditDocument.RootElement;
		Assert.Equal(receipt.SupplierDeliveryNoteNumber, after.GetProperty("supplierDeliveryNoteNumber").GetString());
		Assert.Equal(receipt.ReceivedByUserId, after.GetProperty("receivedByUserId").GetInt64());
		Assert.False(after.TryGetProperty("invoiceNumber", out _));
	}

	[Fact]
	public async Task GoodsReceiptAuditFailureRollsBackEntirePosting()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 3);
		await context.Data.ExecuteAsync(
			"""
			CREATE TRIGGER FailGoodsReceiptAudit
			BEFORE INSERT ON AuditEntries
			WHEN NEW.EntityType = 'GoodsReceipt'
			BEGIN
				SELECT RAISE(ABORT, 'forced audit failure');
			END;
			""",
			CancellationToken.None);

		await Assert.ThrowsAnyAsync<Exception>(() =>
			context.Receipts.PostAsync(context.NewReceipt(order, 2)));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceiptLines;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COALESCE(SUM(ReceivedQuantity), 0) FROM PurchaseOrderLines;"));
		Assert.Equal((long)PurchaseOrderStatus.Ordered, await context.ScalarAsync("SELECT Status FROM PurchaseOrders;"));
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
				receipt.SupplierDeliveryNoteNumber = $"DN-CONCURRENT-{suffix}";
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
	public async Task GoodsReceiptRequiresSupplierDeliveryNoteBeforeStartingTransaction()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);
		var receipt = context.NewReceipt(order, 1);
		receipt.SupplierDeliveryNoteNumber = string.Empty;

		await Assert.ThrowsAsync<ArgumentException>(() => context.Receipts.PostAsync(receipt));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
	}

	[Fact]
	public async Task GoodsReceiptDateCannotBeInTheFuture()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var order = await CreateOrderedOrderAsync(context, 2);
		var receipt = context.NewReceipt(order, 1);
		receipt.ReceiptDate = DateTime.Today.AddDays(1);

		await Assert.ThrowsAsync<ArgumentException>(() => context.Receipts.PostAsync(receipt));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM GoodsReceipts;"));
	}

	[Fact]
	public async Task TwentyLineOrderAndReceiptPreserveProcurementBehavior()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var purposeId = await context.ScalarAsync(
			"SELECT PurposeId FROM Inventories WHERE Id = $Id;",
			new DatabaseParameter("$Id", context.InventoryId));
		var storageLocationId = await context.ScalarAsync(
			"SELECT StorageLocationId FROM Inventories WHERE Id = $Id;",
			new DatabaseParameter("$Id", context.InventoryId));
		var inventoryByItem = new Dictionary<long, long>();
		var lines = new List<PurchaseOrderLine>();
		for (var index = 1; index <= 20; index++)
		{
			var itemId = await context.Data.InsertAsync(
				"INSERT INTO Items (PartNumber, Description, IsActive) VALUES ($PartNumber, $Description, 1);",
				CancellationToken.None,
				new DatabaseParameter("$PartNumber", $"BATCH-{Guid.NewGuid():N}-{index:00}"),
				new DatabaseParameter("$Description", $"Batch item {index:00}"));
			var inventoryId = await context.Data.InsertAsync(
				"INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive) VALUES ($ItemId, $PurposeId, $StorageLocationId, 1);",
				CancellationToken.None,
				new DatabaseParameter("$ItemId", itemId),
				new DatabaseParameter("$PurposeId", purposeId),
				new DatabaseParameter("$StorageLocationId", storageLocationId));
			inventoryByItem.Add(itemId, inventoryId);
			lines.Add(new PurchaseOrderLine { ItemId = itemId, Quantity = 2, UnitPrice = index });
		}

		var draft = context.NewOrder();
		draft.Lines = lines;
		var order = await context.Orders.SaveDraftAsync(draft);

		Assert.Equal(20, order.Lines.Count);
		Assert.All(order.Lines, line =>
		{
			Assert.NotEqual(0, line.Id);
			Assert.NotEmpty(line.ItemPartNumber);
			Assert.NotEmpty(line.ItemDescription);
		});
		order = await context.ApproveAndOrderAsync(order);
		var receipt = new GoodsReceipt
		{
			PurchaseOrderId = order.Id,
			SupplierDeliveryNoteNumber = $"DN-BATCH-{Guid.NewGuid():N}",
			ReceiptDate = DateTime.Today,
			Lines = order.Lines.Select(line => new GoodsReceiptLine
			{
				PurchaseOrderLineId = line.Id,
				InventoryId = inventoryByItem[line.ItemId],
				Quantity = 2
			}).ToArray()
		};

		var posted = await context.Receipts.PostAsync(receipt);

		Assert.Equal(20, posted.Lines.Count);
		Assert.Equal(20, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;",
			new DatabaseParameter("$Reference", posted.ReceiptNumber)));
		Assert.Equal((long)PurchaseOrderStatus.Received, await context.ScalarAsync(
			"SELECT Status FROM PurchaseOrders WHERE Id = $Id;",
			new DatabaseParameter("$Id", order.Id)));
	}

	private static async Task<PurchaseOrder> CreateOrderedOrderAsync(
		ProcurementTestContext context,
		int quantity)
	{
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(quantity));
		return await context.ApproveAndOrderAsync(order);
	}

	private static void UseInvalidAuditUser(ProcurementTestContext context) =>
		context.Authorization.SignIn(new User
		{
			Id = long.MaxValue,
			Email = "missing-audit-user@depot.test",
			DisplayName = "Missing audit user",
			Role = UserRole.Administrator,
			IsAdministrator = true,
			IsActive = true
		});

	private static long JsonInt64(string json, string propertyName)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.GetProperty(propertyName).GetInt64();
	}

	private static int JsonInt32(string json, string propertyName)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.GetProperty(propertyName).GetInt32();
	}

	private static string? JsonString(string json, string propertyName)
	{
		using var document = JsonDocument.Parse(json);
		var property = document.RootElement.GetProperty(propertyName);
		return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
	}

	private static string RequiredJson(string? json) =>
		json ?? throw new InvalidOperationException("The expected audit before-state is missing.");

	private sealed record AuditState(string? BeforeJson, string AfterJson);
}
