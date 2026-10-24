// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class ServicePartIntegrityProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqliteServicePartsContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-service-parts-{Guid.NewGuid():N}.db");
		try { await VerifyAsync(new SqliteConnectionFactory(path)); }
		finally
		{
			SqliteConnection.ClearAllPools();
			foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
				if (File.Exists(path + suffix)) File.Delete(path + suffix);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerServicePartsContract() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbServicePartsContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlServicePartsContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		await using var fixture = await StockWorkflowProviderFixture.CreateAsync(factory);
		Assert.Equal((long)ServiceManagementSchemaMigration.CurrentVersion,
			await fixture.ScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='ServiceManagement';"));
		await VerifyAtomicRollbackAndReplayAsync(fixture);
		await VerifyReturnBoundsAsync(fixture);
		await VerifyConcurrentReturnsAsync(fixture);
		await VerifyConcurrentReplayAsync(fixture);
		await VerifyFinalizationOverlapAsync(fixture, complete: true, partFirst: false);
		await VerifyFinalizationOverlapAsync(fixture, complete: true, partFirst: true);
		await VerifyFinalizationOverlapAsync(fixture, complete: false, partFirst: false);
		await VerifyFinalizationOverlapAsync(fixture, complete: false, partFirst: true);
		await VerifyTrackedReturnsAsync(fixture);
	}

	private static async Task VerifyAtomicRollbackAndReplayAsync(StockWorkflowProviderFixture fixture)
	{
		var stock = await fixture.CreateStockAsync(6, ItemTrackingMode.Lot, [Allocation("LOT-ROLLBACK", 6)]);
		var order = await fixture.CreateServiceOrderAsync(stock);
		var operationId = Guid.NewGuid();
		var before = await SnapshotAsync(fixture, stock, order.Id);
		foreach (var failure in new Exception[]
		{
			new InvalidOperationException("Injected service evidence persistence failure"),
			new OperationCanceledException("Injected cancellation before service commit")
		})
		{
			fixture.Transactions.FailNextCommit(failure);
			Assert.Same(failure, await Record.ExceptionAsync(() => fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId,
				2, null, 25m, true, 19m, [Allocation("LOT-ROLLBACK", 2)], operationId)));
			Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		}

		var consumption = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 2, null, 25m, true, 19m,
			[Allocation("LOT-ROLLBACK", 2)], operationId);
		Assert.Equal(operationId, consumption.OperationId);
		Assert.Equal(4L, await fixture.StockAsync(stock));
		var committed = await SnapshotAsync(fixture, stock, order.Id);
		Assert.Equal(before.OrderVersion + 1, committed.OrderVersion);
		Assert.Equal(1, committed.EvidenceCount);
		Assert.Equal(1L, committed.Operations);
		Assert.Equal(before.Stock.Audits + 3, committed.Stock.Audits);
		Assert.Equal(before.Stock.TrackingRows + 1, committed.Stock.TrackingRows);
		Assert.Equal(before.Stock.Journals, committed.Stock.Journals);

		var returnId = Guid.NewGuid();
		var returnFailure = new InvalidOperationException("Injected service return persistence failure");
		fixture.Transactions.FailNextCommit(returnFailure);
		Assert.Same(returnFailure, await Record.ExceptionAsync(() => fixture.Service.ReturnPartAsync(order.Id, consumption.Id,
			1, null, [Allocation("LOT-ROLLBACK", 1)], returnId)));
		Assert.Equal(committed, await SnapshotAsync(fixture, stock, order.Id));
		var returned = await fixture.Service.ReturnPartAsync(order.Id, consumption.Id, 1, null, [Allocation("LOT-ROLLBACK", 1)], returnId);
		var current = await fixture.Service.GetOrderAsync(order.Id) ?? throw new InvalidOperationException("Service order missing.");
		await fixture.Service.CompleteServiceOrderAsync(order.Id, current.Version, "Provider completion with retained parts evidence");
		var completed = await SnapshotAsync(fixture, stock, order.Id);
		Assert.Equal(ServiceOrderStatus.Completed, completed.Status);
		Assert.Equal(consumption.Id, (await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 2, null, 25m, true, 19m,
			[Allocation("LOT-ROLLBACK", 2)], operationId)).Id);
		Assert.Equal(returned.Id, (await fixture.Service.ReturnPartAsync(order.Id, consumption.Id, 1, null,
			[Allocation("LOT-ROLLBACK", 1)], returnId)).Id);
		Assert.Equal(completed, await SnapshotAsync(fixture, stock, order.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId,
			3, null, 25m, true, 19m, [Allocation("LOT-ROLLBACK", 3)], operationId));
		Assert.Equal(completed, await SnapshotAsync(fixture, stock, order.Id));
	}

	private static async Task VerifyReturnBoundsAsync(StockWorkflowProviderFixture fixture)
	{
		var stock = await fixture.CreateStockAsync(10);
		var order = await fixture.CreateServiceOrderAsync(stock);
		var otherOrder = await fixture.CreateServiceOrderAsync(stock);
		var before = await SnapshotAsync(fixture, stock, order.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, long.MaxValue, 100, null));
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		var consumed = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 4, null, 27m, true, 7m);
		before = await SnapshotAsync(fixture, stock, order.Id);
		var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Movements.ReverseWithdrawalAsync(
			consumed.StockMovementId, fixture.ReasonCodeId, "Service-owned movement", CancellationToken.None));
		Assert.Contains("owning workflow", rejected.Message, StringComparison.Ordinal);
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		var otherBefore = await SnapshotAsync(fixture, stock, otherOrder.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(otherOrder.Id, consumed.Id, 1, null));
		Assert.Equal(otherBefore, await SnapshotAsync(fixture, stock, otherOrder.Id));

		var partial = await fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null);
		Assert.Equal(consumed.Id, partial.OriginalConsumptionId);
		Assert.Equal(consumed.InventoryId, partial.InventoryId);
		Assert.Equal(consumed.UnitPrice, partial.UnitPrice);
		Assert.Equal(consumed.TaxRate, partial.TaxRate);
		Assert.False(partial.Billable);
		Assert.Equal(7L, await fixture.StockAsync(stock));
		before = await SnapshotAsync(fixture, stock, order.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 4, null));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, partial.Id, 1, null));
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		await fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 3, null);
		Assert.Equal(10L, await fixture.StockAsync(stock));
		before = await SnapshotAsync(fixture, stock, order.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null));
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));

		// Retain a historical return without an operation link, as a schema-one database would.
		await fixture.Data.ExecuteAsync("DELETE FROM ServicePartOperations WHERE ServicePartEvidenceId=$Id;",
			CancellationToken.None, new DatabaseParameter("$Id", partial.Id));
		before = await SnapshotAsync(fixture, stock, order.Id);
		var historical = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null));
		Assert.Contains("Historical returns", historical.Message, StringComparison.Ordinal);
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
	}

	private static async Task VerifyConcurrentReturnsAsync(StockWorkflowProviderFixture fixture)
	{
		var stock = await fixture.CreateStockAsync(8);
		var order = await fixture.CreateServiceOrderAsync(stock);
		var consumed = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 4, null, 10m, true, 19m);
		var before = await SnapshotAsync(fixture, stock, order.Id);
		var losingKey = Guid.NewGuid();
		var failure = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 3, null),
			() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 3, null, operationId: losingKey));
		Assert.IsType<InvalidOperationException>(failure);
		var after = await SnapshotAsync(fixture, stock, order.Id);
		Assert.Equal(7L, after.Stock.Stock);
		Assert.Equal(3, after.ReturnedQuantity);
		Assert.Equal(before.EvidenceCount + 1, after.EvidenceCount);
		Assert.Equal(before.Operations + 1, after.Operations);
		Assert.Equal(before.OrderVersion + 1, after.OrderVersion);
		Assert.Equal(before.Stock.Movements + 1, after.Stock.Movements);
		Assert.Equal(before.Stock.Audits + 3, after.Stock.Audits);
		Assert.Equal(before.Stock.Journals, after.Stock.Journals);
		Assert.Equal(before.Stock.TrackingRows, after.Stock.TrackingRows);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 3, null, operationId: losingKey));
		Assert.Equal(after, await SnapshotAsync(fixture, stock, order.Id));
		await fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null);
		Assert.Equal(8L, await fixture.StockAsync(stock));
		Assert.Equal(4, (await SnapshotAsync(fixture, stock, order.Id)).ReturnedQuantity);
	}

	private static async Task VerifyConcurrentReplayAsync(StockWorkflowProviderFixture fixture)
	{
		var stock = await fixture.CreateStockAsync(8);
		var order = await fixture.CreateServiceOrderAsync(stock);
		var before = await SnapshotAsync(fixture, stock, order.Id);
		var operationId = Guid.NewGuid();
		ServicePartEvidence? first = null;
		ServicePartEvidence? second = null;
		var failure = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			async () => { first = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 2, null, 10m, true, 19m, operationId: operationId); },
			async () => { second = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 2, null, 10m, true, 19m, operationId: operationId); });
		Assert.Null(failure);
		var firstEvidence = first ?? throw new InvalidOperationException("First consumption did not return its evidence.");
		var secondEvidence = second ?? throw new InvalidOperationException("Replayed consumption did not return its evidence.");
		Assert.Equal(firstEvidence.Id, secondEvidence.Id);
		Assert.Equal(firstEvidence.StockMovementId, secondEvidence.StockMovementId);
		var after = await SnapshotAsync(fixture, stock, order.Id);
		Assert.Equal(6L, after.Stock.Stock);
		Assert.Equal(1, after.EvidenceCount);
		Assert.Equal(1L, after.Operations);
		Assert.Equal(before.OrderVersion + 1, after.OrderVersion);
		Assert.Equal(before.Stock.Movements + 1, after.Stock.Movements);
		Assert.Equal(before.Stock.Audits + 3, after.Stock.Audits);
	}

	private static async Task VerifyFinalizationOverlapAsync(StockWorkflowProviderFixture fixture, bool complete, bool partFirst)
	{
		var stock = await fixture.CreateStockAsync(4);
		var order = await fixture.CreateServiceOrderAsync(stock);
		await fixture.Service.AddWorkLineAsync(order.Id, "Retained completion evidence", 1m, 10m, true, 19m);
		var before = await SnapshotAsync(fixture, stock, order.Id);
		Func<Task> part = () => fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 1, null, 5m, true, 19m);
		Func<Task> finalize = complete
			? () => fixture.Service.CompleteServiceOrderAsync(order.Id, before.OrderVersion, "Concurrent completion")
			: () => fixture.Service.CancelServiceOrderAsync(order.Id, before.OrderVersion, "Concurrent cancellation");
		var failure = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			partFirst ? part : finalize, partFirst ? finalize : part);
		if (partFirst) Assert.IsType<ConcurrencyConflictException>(failure);
		else Assert.IsType<InvalidOperationException>(failure);
		var after = await SnapshotAsync(fixture, stock, order.Id);
		Assert.Equal(partFirst ? 3L : 4L, after.Stock.Stock);
		Assert.Equal(partFirst ? 1 : 0, after.EvidenceCount);
		Assert.Equal(partFirst ? 1L : 0L, after.Operations);
		Assert.Equal(before.OrderVersion + 1, after.OrderVersion);
		Assert.Equal(partFirst ? ServiceOrderStatus.InProgress : complete ? ServiceOrderStatus.Completed : ServiceOrderStatus.Cancelled, after.Status);
		Assert.Equal(before.Stock.Movements + (partFirst ? 1 : 0), after.Stock.Movements);
		Assert.Equal(before.Stock.Audits + (partFirst ? 3 : complete ? 2 : 1), after.Stock.Audits);
		Assert.Equal(before.Stock.Journals, after.Stock.Journals);
		Assert.Equal(before.Stock.TrackingRows, after.Stock.TrackingRows);
		Assert.Equal(before.Stock.Reserved, after.Stock.Reserved);
		if (partFirst)
		{
			Assert.Equal(before.CaseVersion, after.CaseVersion);
			Assert.Equal(before.CaseStatus, after.CaseStatus);
			Assert.Equal(before.CaseHistory, after.CaseHistory);
		}
	}

	private static async Task VerifyTrackedReturnsAsync(StockWorkflowProviderFixture fixture)
	{
		var expiry = DateTime.Today.AddYears(2);
		var stock = await fixture.CreateStockAsync(5, ItemTrackingMode.Lot, [Allocation("LOT-A", 3, expiry), Allocation("LOT-B", 2, expiry)]);
		var order = await fixture.CreateServiceOrderAsync(stock);
		var operationId = Guid.NewGuid();
		var consumed = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 3, null, 10m, true, 19m,
			[Allocation("lot-b", 1, expiry), Allocation(" lot-a ", 2, expiry)], operationId);
		var before = await SnapshotAsync(fixture, stock, order.Id);
		var replay = await fixture.Service.ConsumePartAsync(order.Id, stock.InventoryId, 3, null, 10m, true, 19m,
			[Allocation("LOT-A", 2, expiry), Allocation("LOT-B", 1, expiry)], operationId);
		Assert.Equal(consumed.Id, replay.Id);
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null, [Allocation("LOT-NEW", 1)]));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null, [Allocation("LOT-A", 1, expiry.AddDays(1))]));
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		var partial = await fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 1, null, [Allocation("LOT-A", 1)]);
		Assert.Equal(await TrackingUnitAsync(fixture, consumed.StockMovementId, "LOT-A"), await TrackingUnitAsync(fixture, partial.StockMovementId, "LOT-A"));
		before = await SnapshotAsync(fixture, stock, order.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 2, null, [Allocation("LOT-A", 2)]));
		Assert.Equal(before, await SnapshotAsync(fixture, stock, order.Id));
		await fixture.Service.ReturnPartAsync(order.Id, consumed.Id, 2, null, [Allocation("LOT-A", 1), Allocation("LOT-B", 1)]);
		Assert.Equal(5L, await fixture.StockAsync(stock));
		Assert.Equal(3L, await TrackingBalanceAsync(fixture, stock, "LOT-A"));
		Assert.Equal(2L, await TrackingBalanceAsync(fixture, stock, "LOT-B"));

		var serialStock = await fixture.CreateStockAsync(1, ItemTrackingMode.SerialNumber, [Allocation("SERIAL-A", 1)]);
		var serialOrder = await fixture.CreateServiceOrderAsync(serialStock);
		var serialConsumed = await fixture.Service.ConsumePartAsync(serialOrder.Id, serialStock.InventoryId, 1, null, 10m, true, 19m, [Allocation("SERIAL-A", 1)]);
		before = await SnapshotAsync(fixture, serialStock, serialOrder.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReturnPartAsync(serialOrder.Id, serialConsumed.Id, 1, null, [Allocation("SERIAL-NEW", 1)]));
		Assert.Equal(before, await SnapshotAsync(fixture, serialStock, serialOrder.Id));
		var serialReturned = await fixture.Service.ReturnPartAsync(serialOrder.Id, serialConsumed.Id, 1, null, [Allocation("SERIAL-A", 1)]);
		Assert.Equal(await TrackingUnitAsync(fixture, serialConsumed.StockMovementId, "SERIAL-A"), await TrackingUnitAsync(fixture, serialReturned.StockMovementId, "SERIAL-A"));
		Assert.Equal(1L, await fixture.StockAsync(serialStock));
		Assert.Equal(1L, await TrackingBalanceAsync(fixture, serialStock, "SERIAL-A"));
	}

	private static TrackingAllocationInput Allocation(string code, int quantity, DateTime? expiry = null) => new() { Code = code, Quantity = quantity, ExpiryDate = expiry };

	private static Task<long> TrackingUnitAsync(StockWorkflowProviderFixture fixture, long movementId, string code) => fixture.ScalarAsync(
		"SELECT t.TrackingUnitId FROM StockMovementTracking t INNER JOIN ItemTrackingUnits u ON u.Id=t.TrackingUnitId WHERE t.StockMovementId=$Id AND u.Code=$Code;",
		new DatabaseParameter("$Id", movementId), new DatabaseParameter("$Code", code));

	private static Task<long> TrackingBalanceAsync(StockWorkflowProviderFixture fixture, ProviderStock stock, string code) => fixture.ScalarAsync(
		"SELECT COALESCE(SUM(t.Quantity),0) FROM StockMovementTracking t INNER JOIN StockMovements m ON m.Id=t.StockMovementId INNER JOIN ItemTrackingUnits u ON u.Id=t.TrackingUnitId WHERE m.InventoryId=$Id AND u.Code=$Code;",
		new DatabaseParameter("$Id", stock.InventoryId), new DatabaseParameter("$Code", code));

	private static async Task<ServiceSnapshot> SnapshotAsync(StockWorkflowProviderFixture fixture, ProviderStock stock, long orderId)
	{
		var order = await fixture.Service.GetOrderAsync(orderId) ?? throw new InvalidOperationException("Service order missing.");
		var serviceCase = await fixture.Service.GetCaseAsync(order.ServiceCaseId) ?? throw new InvalidOperationException("Service case missing.");
		var parts = await fixture.Service.ListPartEvidenceAsync(orderId);
		return new ServiceSnapshot(await fixture.SnapshotAsync(stock), order.Version, order.Status, parts.Count,
			parts.Where(value => value.MovementKind == ServicePartMovementKind.Consumption).Sum(value => value.Quantity),
			parts.Where(value => value.MovementKind == ServicePartMovementKind.Return).Sum(value => value.Quantity),
			await fixture.ScalarAsync("SELECT COUNT(*) FROM ServicePartOperations o INNER JOIN ServicePartEvidence e ON e.Id=o.ServicePartEvidenceId WHERE e.ServiceOrderId=$Id;", new DatabaseParameter("$Id", orderId)),
			serviceCase.Version, serviceCase.Status, (await fixture.Service.ListCaseHistoryAsync(serviceCase.Id)).Count);
	}

	private sealed record ServiceSnapshot(ProviderStockSnapshot Stock, long OrderVersion, ServiceOrderStatus Status,
		int EvidenceCount, int ConsumedQuantity, int ReturnedQuantity, long Operations,
		long CaseVersion, ServiceCaseStatus CaseStatus, int CaseHistory);
}
