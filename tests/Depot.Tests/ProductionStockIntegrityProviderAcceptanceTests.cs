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
public sealed class ProductionStockIntegrityProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqliteProductionStockContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-production-integrity-{Guid.NewGuid():N}.db");
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
	public Task SqlServerProductionStockContract() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbProductionStockContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlProductionStockContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		await using var fixture = await StockWorkflowProviderFixture.CreateAsync(factory);
		await VerifyOwnedCorrectionAsync(fixture);
		await VerifyReservedQuantityAsync(fixture);
		await VerifyCompetingReservationAsync(fixture, reserveFirst: true);
		await VerifyCompetingReservationAsync(fixture, reserveFirst: false);
		await VerifyStandaloneWithdrawalAsync(fixture);
	}

	private static async Task VerifyOwnedCorrectionAsync(StockWorkflowProviderFixture fixture)
	{
		var component = await fixture.CreateStockAsync(4);
		var plan = await fixture.CreateReleasedProductionAsync(component, 4);
		var requirementId = Assert.Single(plan.Order.Requirements).Id;
		await fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, component.InventoryId, 4);
		var originalId = await fixture.ScalarAsync("SELECT StockMovementId FROM ProductionMaterialMovements WHERE ProductionOrderId=$Id AND Kind=$Kind;",
			new DatabaseParameter("$Id", plan.Order.Id), new DatabaseParameter("$Kind", (int)ProductionMovementKind.ComponentIssue));
		var before = await SnapshotAsync(fixture, plan);

		var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Movements.ReverseWithdrawalAsync(
			originalId, fixture.ReasonCodeId, "Standalone reversal must retain production ownership", CancellationToken.None));
		Assert.Contains("owning workflow", rejected.Message, StringComparison.Ordinal);
		Assert.Equal(before, await SnapshotAsync(fixture, plan));

		var correctionId = Guid.NewGuid();
		var failure = new InvalidOperationException("Injected failure after component correction persistence");
		fixture.Transactions.FailNextCommit(failure);
		Assert.Same(failure, await Record.ExceptionAsync(() => fixture.Production.ReverseComponentIssueAsync(
			plan.Order.Id, originalId, fixture.ReasonCodeId, "Correct component issue", correctionId)));
		Assert.Equal(before, await SnapshotAsync(fixture, plan));

		var corrected = await fixture.Production.ReverseComponentIssueAsync(plan.Order.Id, originalId, fixture.ReasonCodeId, "Correct component issue", correctionId);
		Assert.Equal(0, Assert.Single(corrected.Requirements).IssuedQuantity);
		Assert.Equal(4L, await fixture.StockAsync(component));
		var afterCorrection = await SnapshotAsync(fixture, plan);
		await fixture.Production.ReverseComponentIssueAsync(plan.Order.Id, originalId, fixture.ReasonCodeId, "Correct component issue", correctionId);
		Assert.Equal(afterCorrection, await SnapshotAsync(fixture, plan));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Production.ReverseComponentIssueAsync(
			plan.Order.Id, originalId, fixture.ReasonCodeId, "Changed correction payload", correctionId));
		Assert.Equal(afterCorrection, await SnapshotAsync(fixture, plan));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Production.CompleteAsync(plan.Order.Id));
		Assert.Equal(afterCorrection, await SnapshotAsync(fixture, plan));

		await fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, component.InventoryId, 4);
		var completed = await fixture.Production.CompleteAsync(plan.Order.Id);
		Assert.Equal(ProductionOrderStatus.Completed, completed.Order.Status);
		Assert.Equal(20m, completed.Cost.TotalComponentCost);
		Assert.Equal(0L, await fixture.StockAsync(component));
		Assert.Equal(1L, await fixture.StockAsync(plan.Finished));

		var reversalId = Guid.NewGuid();
		var reversed = await fixture.Production.ReverseAsync(plan.Order.Id, fixture.ReasonCodeId, "Reverse corrected assembly", reversalId);
		Assert.Equal(ProductionOrderStatus.Reversed, reversed.Status);
		Assert.Equal(0, Assert.Single(reversed.Requirements).IssuedQuantity);
		Assert.Equal(4L, await fixture.StockAsync(component));
		Assert.Equal(0L, await fixture.StockAsync(plan.Finished));
		Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId=$Id;", new DatabaseParameter("$Id", originalId)));
		var afterReversal = await SnapshotAsync(fixture, plan);
		await fixture.Production.ReverseAsync(plan.Order.Id, fixture.ReasonCodeId, "Reverse corrected assembly", reversalId);
		Assert.Equal(afterReversal, await SnapshotAsync(fixture, plan));
	}

	private static async Task VerifyReservedQuantityAsync(StockWorkflowProviderFixture fixture)
	{
		var stock = await fixture.CreateStockAsync(10);
		var plan = await fixture.CreateReleasedProductionAsync(stock, 5);
		var order = await fixture.CreateApprovedSalesOrderAsync(stock, 10);
		order = await fixture.SalesOrders.SetReservationsAsync(order.Id, order.Version,
			[new SalesReservationRequest(Assert.Single(order.Lines).Id, stock.InventoryId, 10)]);
		order = await fixture.SalesOrders.ReleaseAsync(order.Id, order.Version);
		var requirementId = Assert.Single(plan.Order.Requirements).Id;
		var availability = Assert.Single(await fixture.Production.GetAvailabilityAsync(plan.Order.Id));
		Assert.Equal(0L, availability.ReservableQuantity);
		Assert.Equal(10L, availability.ReservedQuantity);
		Assert.Equal(5L, availability.ShortageQuantity);
		var before = await SnapshotAsync(fixture, plan);
		await Assert.ThrowsAsync<InsufficientStockException>(() => fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, stock.InventoryId, 5));
		Assert.Equal(before, await SnapshotAsync(fixture, plan));

		order = await fixture.SalesOrders.SetReservationsAsync(order.Id, order.Version,
			[new SalesReservationRequest(Assert.Single(order.Lines).Id, stock.InventoryId, 6)]);
		before = await SnapshotAsync(fixture, plan);
		await Assert.ThrowsAsync<InsufficientStockException>(() => fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, stock.InventoryId, 5));
		Assert.Equal(before, await SnapshotAsync(fixture, plan));
		var issued = await fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, stock.InventoryId, 4);
		Assert.Equal(4, Assert.Single(issued.Requirements).IssuedQuantity);
		Assert.Equal(6L, await fixture.StockAsync(stock));
		Assert.Equal(6L, await fixture.ReservationsAsync(stock));
		before = await SnapshotAsync(fixture, plan);
		await Assert.ThrowsAsync<InsufficientStockException>(() => fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, stock.InventoryId, 1));
		Assert.Equal(before, await SnapshotAsync(fixture, plan));
		var retained = await fixture.SalesOrders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Reserved order missing.");
		Assert.Equal(order.Version, retained.Version);
		Assert.Equal(6, Assert.Single(retained.Lines).ReservedQuantity);
		Assert.Equal(0, Assert.Single(retained.Lines).ShippedQuantity);
	}

	private static async Task VerifyCompetingReservationAsync(StockWorkflowProviderFixture fixture, bool reserveFirst)
	{
		var stock = await fixture.CreateStockAsync(10);
		var plan = await fixture.CreateReleasedProductionAsync(stock, 5);
		var order = await fixture.CreateApprovedSalesOrderAsync(stock, 7);
		var orderLine = Assert.Single(order.Lines);
		var requirementId = Assert.Single(plan.Order.Requirements).Id;
		var before = await SnapshotAsync(fixture, plan);
		Func<Task> reserve = () => fixture.SalesOrders.SetReservationsAsync(order.Id, order.Version,
			[new SalesReservationRequest(orderLine.Id, stock.InventoryId, 7)]);
		Func<Task> issue = () => fixture.Production.IssueComponentAsync(plan.Order.Id, requirementId, stock.InventoryId, 5);
		var loser = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			reserveFirst ? reserve : issue, reserveFirst ? issue : reserve);
		Assert.IsType<InsufficientStockException>(loser);

		var after = await SnapshotAsync(fixture, plan);
		Assert.Equal(reserveFirst ? 10L : 5L, after.Component.Stock);
		Assert.Equal(reserveFirst ? 7L : 0L, after.Component.Reserved);
		Assert.True(after.Component.Stock >= after.Component.Reserved);
		Assert.Equal(reserveFirst ? 0 : 5, after.IssuedQuantity);
		Assert.Equal(before.MaterialEvidence + (reserveFirst ? 0 : 1), after.MaterialEvidence);
		Assert.Equal(before.Component.Movements + (reserveFirst ? 0 : 1), after.Component.Movements);
		Assert.Equal(before.Component.Audits + (reserveFirst ? 1 : 2), after.Component.Audits);
		Assert.Equal(before.Component.Journals, after.Component.Journals);
		Assert.Equal(before.Component.TrackingRows, after.Component.TrackingRows);
		Assert.Equal(before.CostEvidence, after.CostEvidence);
		Assert.Equal(0L, after.Finished.Stock);
		var retained = await fixture.SalesOrders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Competing order missing.");
		var retainedLine = Assert.Single(retained.Lines);
		Assert.Equal(reserveFirst ? 7 : 0, retainedLine.ReservedQuantity);
		Assert.Equal(0, retainedLine.ShippedQuantity);
		if (!reserveFirst)
		{
			Assert.Equal(order.Version, retained.Version);
			Assert.Equal(orderLine.Version, retainedLine.Version);
			Assert.Empty(await fixture.SalesOrders.GetReservationsAsync(order.Id));
		}
		else
		{
			Assert.Equal(plan.Order.Version, after.OrderVersion);
			Assert.Equal(ProductionOrderStatus.Released, after.Status);
		}
	}

	private static async Task VerifyStandaloneWithdrawalAsync(StockWorkflowProviderFixture fixture)
	{
		var stock = await fixture.CreateStockAsync(3);
		var withdrawal = await fixture.Movements.AddWithdrawalAsync(stock.InventoryId, 1, null, "Unowned withdrawal", "Provider control", CancellationToken.None);
		await fixture.Movements.ReverseWithdrawalAsync(withdrawal.MovementId, fixture.ReasonCodeId, "Allowed standalone correction", CancellationToken.None);
		Assert.Equal(3L, await fixture.StockAsync(stock));
		Assert.Equal(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId=$Id;", new DatabaseParameter("$Id", withdrawal.MovementId)));
	}

	private static async Task<ProductionSnapshot> SnapshotAsync(StockWorkflowProviderFixture fixture, ProviderProductionPlan plan)
	{
		var order = await fixture.Production.GetOrderAsync(plan.Order.Id) ?? throw new InvalidOperationException("Production order missing.");
		return new ProductionSnapshot(await fixture.SnapshotAsync(plan.Component), await fixture.SnapshotAsync(plan.Finished),
			order.Version, order.Status, Assert.Single(order.Requirements).IssuedQuantity, order.CompletedQuantity,
			await fixture.ScalarAsync("SELECT COUNT(*) FROM ProductionMaterialMovements WHERE ProductionOrderId=$Id;", new DatabaseParameter("$Id", order.Id)),
			await fixture.ScalarAsync("SELECT COUNT(*) FROM ProductionAssemblyCostEvidence WHERE ProductionOrderId=$Id;", new DatabaseParameter("$Id", order.Id)));
	}

	private sealed record ProductionSnapshot(ProviderStockSnapshot Component, ProviderStockSnapshot Finished,
		long OrderVersion, ProductionOrderStatus Status, int IssuedQuantity, int CompletedQuantity, long MaterialEvidence, long CostEvidence);
}
