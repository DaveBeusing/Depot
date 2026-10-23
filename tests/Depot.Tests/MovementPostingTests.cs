// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection(SqliteSchemaInitializationCollection.Name)]
public sealed class MovementPostingTests
{
	[Theory]
	[InlineData(0, false)]
	[InlineData(0, true)]
	[InlineData(1, false)]
	[InlineData(1, true)]
	public async Task FullCompositionRejectsInsufficientStockWithoutChangingMovementTrackingOrAudit(int initialQuantity, bool correction)
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var movements = MovementPostingTestSupport.CreateMovements(context);
		if (initialQuantity > 0)
			await movements.AddPurchaseAsync(context.InventoryId, initialQuantity, 5m, null, null, null, CancellationToken.None);
		var auditCount = await MovementPostingTestSupport.StockMovementAuditCountAsync(context);

		if (correction)
			await Assert.ThrowsAsync<InsufficientStockException>(() => movements.AddCorrectionAsync(context.InventoryId, -2, null, null, null, CancellationToken.None));
		else
			await Assert.ThrowsAsync<InsufficientStockException>(() => movements.AddWithdrawalAsync(context.InventoryId, 2, null, null, null, CancellationToken.None));

		await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, initialQuantity, initialQuantity > 0 ? 1 : 0);
		Assert.Equal(auditCount, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));
		await MovementPostingTestSupport.AssertTrackingStateAsync(context, 0, 0, 0);
	}

	[Fact]
	public async Task FullCompositionAllowsCorrectionToExactlyZero()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var movements = MovementPostingTestSupport.CreateMovements(context);
		await movements.AddPurchaseAsync(context.InventoryId, 2, 5m, null, null, null, CancellationToken.None);

		var correction = await movements.AddCorrectionAsync(context.InventoryId, -2, null, "COUNT", null, CancellationToken.None);

		Assert.Equal(StockMovementType.Correction, correction.MovementType);
		Assert.Equal(-2, correction.Quantity);
		await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, 0, 2);
	}

	[Fact]
	public async Task FullCompositionRejectsInactiveInventoryBeforePosting()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var movements = MovementPostingTestSupport.CreateMovements(context);
		var auditCount = await MovementPostingTestSupport.StockMovementAuditCountAsync(context);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => movements.AddPurchaseAsync(context.InactiveInventoryId, 1, 5m, null, null, null, CancellationToken.None));

		Assert.Contains("inactive", exception.Message);
		await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InactiveInventoryId, 0, 0);
		Assert.Equal(auditCount, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));
	}

	[Fact]
	public async Task FullCompositionSerializesConcurrentWithdrawalsOfLastUnit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var movements = MovementPostingTestSupport.CreateMovements(context);
		await movements.AddPurchaseAsync(context.InventoryId, 1, 5m, null, null, null, CancellationToken.None);

		await MovementPostingTestSupport.WithdrawLastUnitConcurrentlyAsync(context);
	}

	[Fact]
	public async Task AuditFailureRollsBackMovementNewTrackingUnitAndAllocation()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await MovementPostingTestSupport.EnableLotTrackingAsync(context);
		var movements = MovementPostingTestSupport.CreateMovements(context);
		var auditCount = await MovementPostingTestSupport.StockMovementAuditCountAsync(context);
		await context.Data.ExecuteAsync(
			"CREATE TRIGGER FailStockMovementAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'StockMovement' BEGIN SELECT RAISE(ABORT, 'forced stock movement audit failure'); END;",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => movements.AddPurchaseAsync(
			context.InventoryId, 2, 5m, null, null, null,
			[new TrackingAllocationInput { Code = "LOT-A", Quantity = 2 }]));

		await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, 0, 0);
		await MovementPostingTestSupport.AssertTrackingStateAsync(context, 0, 0, 0);
		Assert.Equal(auditCount, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));
	}

	[Fact]
	public async Task TrackingFailureRollsBackEarlierAllocationAndPreservesStockForRetry()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await MovementPostingTestSupport.EnableLotTrackingAsync(context);
		var movements = MovementPostingTestSupport.CreateMovements(context);
		await movements.AddPurchaseAsync(context.InventoryId, 2, 5m, null, null, null,
			[new TrackingAllocationInput { Code = "LOT-A", Quantity = 2 }]);
		var auditCount = await MovementPostingTestSupport.StockMovementAuditCountAsync(context);

		// LOT-A is allocated first; resolving the later, missing lot must roll it back.
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => movements.AddWithdrawalAsync(
			context.InventoryId, 2, null, null, null,
			[
				new TrackingAllocationInput { Code = "LOT-A", Quantity = 1 },
				new TrackingAllocationInput { Code = "LOT-Z-MISSING", Quantity = 1 }
			]));

		Assert.Contains("LOT-Z-MISSING", exception.Message);
		await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, 2, 1);
		await MovementPostingTestSupport.AssertTrackingStateAsync(context, 1, 1, 2);
		Assert.Equal(auditCount, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));

		await movements.AddWithdrawalAsync(context.InventoryId, 2, null, null, null,
			[new TrackingAllocationInput { Code = "LOT-A", Quantity = 2 }]);

		await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, 0, 2);
		await MovementPostingTestSupport.AssertTrackingStateAsync(context, 1, 2, 0);
		Assert.Equal(auditCount + 1, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));
	}
}

internal static class MovementPostingTestSupport
{
	internal static MovementService CreateMovements(ProcurementTestContext context)
	{
		var data = new DatabaseAccess(context.ConnectionFactory);
		var transactions = new DatabaseTransactionRunner(data);
		var auditEntries = new AuditRepository(data);
		var audit = new AuditService(auditEntries, context.Authorization);
		var items = new ItemRepository(data);
		var inventories = new InventoryRepository(data);
		var reasonCodes = new ReasonCodeRepository(data);
		var movements = new StockMovementRepository(data);
		var traceability = new ItemTraceabilityService(new ItemTraceabilityRepository(data), audit);
		var reversals = new StockMovementReversalService(transactions, inventories, movements, reasonCodes, auditEntries, audit, traceability);
		return new MovementService(items, inventories, reasonCodes, movements, audit, reversals, transactions, auditEntries, traceability);
	}

	internal static async Task WithdrawLastUnitConcurrentlyAsync(ProcurementTestContext context)
	{
		var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstService = CreateMovements(context);
		var secondService = CreateMovements(context);
		var first = Task.Run(() => AttemptAsync(firstService, firstReady));
		var second = Task.Run(() => AttemptAsync(secondService, secondReady));
		await Task.WhenAll(firstReady.Task, secondReady.Task);
		start.SetResult(true);
		var results = await Task.WhenAll(first, second);

		Assert.Single(results, result => result);
		await AssertInventoryStateAsync(context, context.InventoryId, 0, 2);

		async Task<bool> AttemptAsync(MovementService movements, TaskCompletionSource<bool> ready)
		{
			ready.SetResult(true);
			await start.Task;
			try
			{
				await movements.AddWithdrawalAsync(context.InventoryId, 1, null, "LAST-UNIT", null, CancellationToken.None);
				return true;
			}
			catch (InsufficientStockException)
			{
				return false;
			}
		}
	}

	internal static async Task AssertInventoryStateAsync(ProcurementTestContext context, long inventoryId, long quantity, long movementCount)
	{
		Assert.Equal(quantity, await context.ScalarAsync(
			"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", inventoryId)));
		Assert.Equal(movementCount, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", inventoryId)));
		Assert.Equal(movementCount, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockMovement' AND Action = 'Created' AND EntityId IN (SELECT Id FROM StockMovements WHERE InventoryId = $InventoryId);",
			new DatabaseParameter("$InventoryId", inventoryId)));
	}

	internal static Task<long> StockMovementAuditCountAsync(ProcurementTestContext context) =>
		context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockMovement';");

	internal static Task EnableLotTrackingAsync(ProcurementTestContext context) =>
		context.Data.ExecuteAsync("UPDATE Items SET TrackingMode = $TrackingMode WHERE Id = $ItemId;", CancellationToken.None,
			new DatabaseParameter("$TrackingMode", (int)ItemTrackingMode.Lot),
			new DatabaseParameter("$ItemId", context.ItemId));

	internal static async Task AssertTrackingStateAsync(ProcurementTestContext context, long unitCount, long allocationCount, long quantity)
	{
		Assert.Equal(unitCount, await context.ScalarAsync("SELECT COUNT(*) FROM ItemTrackingUnits WHERE ItemId = $ItemId;",
			new DatabaseParameter("$ItemId", context.ItemId)));
		Assert.Equal(allocationCount, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovementTracking mt INNER JOIN StockMovements sm ON sm.Id = mt.StockMovementId WHERE sm.InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", context.InventoryId)));
		Assert.Equal(quantity, await context.ScalarAsync(
			"SELECT COALESCE(SUM(mt.Quantity), 0) FROM StockMovementTracking mt INNER JOIN StockMovements sm ON sm.Id = mt.StockMovementId WHERE sm.InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", context.InventoryId)));
	}
}
