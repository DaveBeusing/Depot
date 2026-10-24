// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class SalesSplitShipmentProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqlitePreservesSplitShipmentQuantities()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-split-shipment-{Guid.NewGuid():N}.db");
		try
		{
			await VerifyAsync(new SqliteConnectionFactory(path));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
				if (File.Exists(path + suffix)) File.Delete(path + suffix);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerPreservesSplitShipmentQuantities() => VerifyAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbPreservesSplitShipmentQuantities() => VerifyAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlPreservesSplitShipmentQuantities() => VerifyAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory innerFactory)
	{
		DatabaseProvisioningService.Initialize(innerFactory);
		var factory = new ObservedProviderConnectionFactory(innerFactory);
		ProviderTransactionGate? gate = null;
		var fixture = await SalesProviderAcceptanceTests.SalesFixture.CreateAsync(factory,
			data => gate = new ProviderTransactionGate(new DatabaseTransactionRunner(data)));
		var transactions = gate ?? throw new InvalidOperationException("The fixture did not configure its transaction runner.");

		await VerifyGroupedChangesAndRollbackAsync(fixture, transactions);
		await VerifyPendingDraftsAsync(fixture);
		await VerifyConcurrentShipmentsAndReversalsAsync(fixture, factory, transactions);
		await VerifyOpposingChangesAsync(fixture, factory, transactions, reversalFirst: false);
		await VerifyOpposingChangesAsync(fixture, factory, transactions, reversalFirst: true);
	}

	private static async Task VerifyGroupedChangesAndRollbackAsync(SalesProviderAcceptanceTests.SalesFixture fixture, ProviderTransactionGate gate)
	{
		var scenario = await CreateScenarioAsync(fixture, mixedOrderLines: true);
		var shipment = await CreatePackedShipmentAsync(fixture, scenario, 4, 6);
		var before = await SnapshotAsync(fixture, scenario);
		var postingFailure = new InvalidOperationException("Injected split posting failure before commit.");
		gate.FailNextCommit(postingFailure);
		Assert.Same(postingFailure, await Record.ExceptionAsync(() => fixture.Shipments.PostAsync(shipment.Id, shipment.Version)));
		Assert.Equal(before, await SnapshotAsync(fixture, scenario));

		shipment = await fixture.Shipments.PostAsync(shipment.Id, shipment.Version);
		var shipped = await AssertQuantitiesAsync(fixture, scenario, 0, 10, SalesOrderStatus.Shipped, 13, 14);
		Assert.Equal(0, shipped.Lines[1].ReservedQuantity);
		Assert.Equal(3, shipped.Lines[1].ShippedQuantity);
		Assert.All(shipped.Lines, line => Assert.Equal(scenario.Order.Lines.Single(source => source.Id == line.Id).Version + 1, line.Version));
		Assert.All(await fixture.Orders.GetReservationsAsync(shipped.Id), reservation => Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status));

		before = await SnapshotAsync(fixture, scenario);
		var reversalFailure = new InvalidOperationException("Injected split reversal failure before commit.");
		gate.FailNextCommit(reversalFailure);
		Assert.Same(reversalFailure, await Record.ExceptionAsync(() => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Retry split correction")));
		Assert.Equal(before, await SnapshotAsync(fixture, scenario));
		await fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Split correction");
		var restored = await AssertQuantitiesAsync(fixture, scenario, 10, 0, SalesOrderStatus.Released, 20, 20);
		Assert.Equal(3, restored.Lines[1].ReservedQuantity);
		Assert.Equal(0, restored.Lines[1].ShippedQuantity);
		Assert.All(restored.Lines, line => Assert.Equal(scenario.Order.Lines.Single(source => source.Id == line.Id).Version + 2, line.Version));
		var active = (await fixture.Orders.GetReservationsAsync(restored.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(3, active.Length);
		foreach (var original in scenario.Reservations)
			Assert.Equal(original.Quantity, Assert.Single(active, reservation => reservation.SalesOrderLineId == original.SalesOrderLineId && reservation.InventoryId == original.InventoryId).Quantity);
		await fixture.Orders.CancelAsync(restored.Id, restored.Version, "Provider scenario complete");
	}

	private static async Task VerifyPendingDraftsAsync(SalesProviderAcceptanceTests.SalesFixture fixture)
	{
		var scenario = await CreateScenarioAsync(fixture);
		var first = await CreatePackedShipmentAsync(fixture, scenario, 2, 3);
		var stale = await CreatePackedShipmentAsync(fixture, scenario, 4, 6);
		var pending = await CreatePackedShipmentAsync(fixture, scenario, 2, 3);
		first = await fixture.Shipments.PostAsync(first.Id, first.Version);
		await AssertQuantitiesAsync(fixture, scenario, 5, 5, SalesOrderStatus.PartiallyShipped, 18, 17);
		var before = await SnapshotAsync(fixture, scenario);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostAsync(stale.Id, stale.Version));
		Assert.Equal(before, await SnapshotAsync(fixture, scenario));

		await fixture.Shipments.ReverseAsync(first.Id, first.Version, "Restore partial split reservations");
		var active = (await fixture.Orders.GetReservationsAsync(scenario.Order.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(2, active.Length);
		foreach (var original in scenario.Reservations)
			Assert.Equal(original.Quantity, Assert.Single(active, reservation => reservation.Id == original.Id).Quantity);
		pending = await fixture.Shipments.PostAsync(pending.Id, pending.Version);
		await AssertQuantitiesAsync(fixture, scenario, 5, 5, SalesOrderStatus.PartiallyShipped, 18, 17);
		await fixture.Shipments.ReverseAsync(pending.Id, pending.Version, "Provider scenario complete");
		var restored = await AssertQuantitiesAsync(fixture, scenario, 10, 0, SalesOrderStatus.Released, 20, 20);
		await fixture.Orders.CancelAsync(restored.Id, restored.Version, "Provider scenario complete");
	}

	private static async Task VerifyConcurrentShipmentsAndReversalsAsync(SalesProviderAcceptanceTests.SalesFixture fixture, ObservedProviderConnectionFactory factory, ProviderTransactionGate gate)
	{
		var scenario = await CreateScenarioAsync(fixture);
		var first = await CreatePackedShipmentAsync(fixture, scenario, 2, 3);
		var second = await CreatePackedShipmentAsync(fixture, scenario, 2, 3);
		Assert.Null(await ProviderWriteOverlap.RunAsync(factory, gate,
			() => fixture.Shipments.PostAsync(first.Id, first.Version),
			() => fixture.Shipments.PostAsync(second.Id, second.Version)));
		var shipped = await AssertQuantitiesAsync(fixture, scenario, 0, 10, SalesOrderStatus.Shipped, 16, 14);
		Assert.Equal(scenario.Order.Lines[0].Version + 2, shipped.Lines[0].Version);
		Assert.Empty((await fixture.Orders.GetReservationsAsync(shipped.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active));
		first = await fixture.Shipments.GetByIdAsync(first.Id) ?? throw new InvalidOperationException("First shipment missing.");
		second = await fixture.Shipments.GetByIdAsync(second.Id) ?? throw new InvalidOperationException("Second shipment missing.");
		Assert.Null(await ProviderWriteOverlap.RunAsync(factory, gate,
			() => fixture.Shipments.ReverseAsync(first.Id, first.Version, "First concurrent split reversal"),
			() => fixture.Shipments.ReverseAsync(second.Id, second.Version, "Second concurrent split reversal")));
		var restored = await AssertQuantitiesAsync(fixture, scenario, 10, 0, SalesOrderStatus.Released, 20, 20);
		Assert.Equal(scenario.Order.Lines[0].Version + 4, restored.Lines[0].Version);
		await AssertActiveReservationsAsync(fixture, scenario, 4, 6);
		await fixture.Orders.CancelAsync(restored.Id, restored.Version, "Provider scenario complete");
	}

	private static async Task VerifyOpposingChangesAsync(SalesProviderAcceptanceTests.SalesFixture fixture, ObservedProviderConnectionFactory factory, ProviderTransactionGate gate, bool reversalFirst)
	{
		var scenario = await CreateScenarioAsync(fixture);
		var first = await CreatePackedShipmentAsync(fixture, scenario, 2, 3);
		first = await fixture.Shipments.PostAsync(first.Id, first.Version);
		var second = await CreatePackedShipmentAsync(fixture, scenario, 2, 3);
		Func<Task> post = () => fixture.Shipments.PostAsync(second.Id, second.Version);
		Func<Task> reverse = () => fixture.Shipments.ReverseAsync(first.Id, first.Version, "Concurrent split correction");
		Assert.Null(await ProviderWriteOverlap.RunAsync(factory, gate, reversalFirst ? reverse : post, reversalFirst ? post : reverse));
		await AssertQuantitiesAsync(fixture, scenario, 5, 5, SalesOrderStatus.PartiallyShipped, 18, 17);
		await AssertActiveReservationsAsync(fixture, scenario, 2, 3);
		second = await fixture.Shipments.GetByIdAsync(second.Id) ?? throw new InvalidOperationException("Second shipment missing.");
		await fixture.Shipments.ReverseAsync(second.Id, second.Version, "Provider scenario complete");
		var restored = await AssertQuantitiesAsync(fixture, scenario, 10, 0, SalesOrderStatus.Released, 20, 20);
		await fixture.Orders.CancelAsync(restored.Id, restored.Version, "Provider scenario complete");
	}

	private static async Task<SalesOrder> AssertQuantitiesAsync(SalesProviderAcceptanceTests.SalesFixture fixture, SplitScenario scenario, int reserved, int shipped, SalesOrderStatus status, long firstStock, long secondStock)
	{
		var order = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(status, order.Status);
		Assert.Equal(reserved, order.Lines[0].ReservedQuantity);
		Assert.Equal(shipped, order.Lines[0].ShippedQuantity);
		Assert.Equal(0, order.Lines[0].InvoicedQuantity);
		Assert.Equal(0, order.Lines[0].BackorderedQuantity);
		Assert.Equal(firstStock, await fixture.CurrentStockAsync());
		Assert.Equal(secondStock, Convert.ToInt64(await fixture.Database.ExecuteScalarAsync("SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$Id;", CancellationToken.None, new DatabaseParameter("$Id", scenario.SecondInventoryId))));
		return order;
	}

	private static async Task AssertActiveReservationsAsync(SalesProviderAcceptanceTests.SalesFixture fixture, SplitScenario scenario, int first, int second)
	{
		var active = (await fixture.Orders.GetReservationsAsync(scenario.Order.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(2, active.Length);
		Assert.Equal(first, Assert.Single(active, reservation => reservation.InventoryId == fixture.InventoryId).Quantity);
		Assert.Equal(second, Assert.Single(active, reservation => reservation.InventoryId == scenario.SecondInventoryId).Quantity);
	}

	private static async Task<Shipment> CreatePackedShipmentAsync(SalesProviderAcceptanceTests.SalesFixture fixture, SplitScenario scenario, int first, int second)
	{
		var shipment = await fixture.Shipments.CreateAsync(scenario.Order.Id, scenario.Reservations.Select(reservation => new ShipmentLineRequest(reservation.Id,
			reservation.SalesOrderLineId != scenario.Order.Lines[0].Id ? reservation.Quantity : reservation.InventoryId == fixture.InventoryId ? first : second)).ToArray());
		var picking = await fixture.Packing.SetStatusAsync(shipment.Id, shipment.Version, ShipmentPackingStatus.Picking);
		return await fixture.Packing.SetStatusAsync(picking.Id, picking.Version, ShipmentPackingStatus.Packed);
	}

	private static async Task<SplitScenario> CreateScenarioAsync(SalesProviderAcceptanceTests.SalesFixture fixture, bool mixedOrderLines = false)
	{
		var data = fixture.Database;
		var warehouseId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT sl.WarehouseId FROM StorageLocations sl INNER JOIN Inventories inv ON inv.StorageLocationId=sl.Id WHERE inv.Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", fixture.InventoryId)));
		var purposeId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT PurposeId FROM Inventories WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", fixture.InventoryId)));
		var locationId = await data.InsertAsync("INSERT INTO StorageLocations (WarehouseId,Name,Description,IsActive) VALUES ($WarehouseId,$Name,'Provider split shipment bin',1);", CancellationToken.None,
			new DatabaseParameter("$WarehouseId", warehouseId), new DatabaseParameter("$Name", $"Split-{Guid.NewGuid():N}"));
		var secondInventoryId = await data.InsertAsync("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($ItemId,$PurposeId,$LocationId,1);", CancellationToken.None,
			new DatabaseParameter("$ItemId", fixture.ItemId), new DatabaseParameter("$PurposeId", purposeId), new DatabaseParameter("$LocationId", locationId));
		await data.InsertAsync("INSERT INTO StockMovements (InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice,Reference) VALUES ($InventoryId,$Type,$Timestamp,20,0,'Provider split opening stock');", CancellationToken.None,
			new DatabaseParameter("$InventoryId", secondInventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$Timestamp", DateTime.UtcNow.ToString("O")));
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = $"Provider split {Guid.NewGuid():N}", Currency = "EUR" });
		int[] quantities = mixedOrderLines ? [10, 3] : [10];
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = quantities.Select(quantity => new SalesOrderLine { ItemId = fixture.ItemId, Quantity = quantity, UnitPrice = 10m, TaxRate = 19m }).ToArray() });
		var submitted = await fixture.Orders.SubmitAsync(draft.Id, draft.Version);
		var approved = await fixture.Orders.ApproveAsync(submitted.Id, submitted.Version);
		var requests = new List<SalesReservationRequest> { new(approved.Lines[0].Id, fixture.InventoryId, 4), new(approved.Lines[0].Id, secondInventoryId, 6) };
		if (mixedOrderLines) requests.Add(new(approved.Lines[1].Id, fixture.InventoryId, 3));
		var reserved = await fixture.Orders.SetReservationsAsync(approved.Id, approved.Version, requests);
		var released = await fixture.Orders.ReleaseAsync(reserved.Id, reserved.Version);
		var reservations = (await fixture.Orders.GetReservationsAsync(released.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		return new SplitScenario(released, secondInventoryId, reservations);
	}

	private static async Task<string> SnapshotAsync(SalesProviderAcceptanceTests.SalesFixture fixture, SplitScenario scenario)
	{
		var tables = new Dictionary<string, IReadOnlyList<object?[]>>();
		var queries = new Dictionary<string, string>
		{
			["SalesOrders"] = "SELECT * FROM SalesOrders WHERE Id=$OrderId ORDER BY Id;",
			["SalesOrderLines"] = "SELECT * FROM SalesOrderLines WHERE SalesOrderId=$OrderId ORDER BY Id;",
			["Shipments"] = "SELECT * FROM Shipments WHERE SalesOrderId=$OrderId ORDER BY Id;",
			["ShipmentLines"] = "SELECT * FROM ShipmentLines WHERE ShipmentId IN (SELECT Id FROM Shipments WHERE SalesOrderId=$OrderId) ORDER BY Id;",
			["InventoryReservations"] = "SELECT * FROM InventoryReservations WHERE SalesOrderLineId IN (SELECT Id FROM SalesOrderLines WHERE SalesOrderId=$OrderId) ORDER BY Id;",
			["StockMovements"] = "SELECT * FROM StockMovements WHERE InventoryId IN ($FirstInventoryId,$SecondInventoryId) ORDER BY Id;",
			["Inventories"] = "SELECT * FROM Inventories WHERE Id IN ($FirstInventoryId,$SecondInventoryId) ORDER BY Id;",
			["EvidenceCounts"] = "SELECT (SELECT COUNT(*) FROM AuditEntries),(SELECT COUNT(*) FROM FinanceJournalEntries),(SELECT COUNT(*) FROM StockMovementTracking);"
		};
		foreach (var (name, sql) in queries)
		{
			tables[name] = await fixture.Database.QueryAsync(sql, reader => Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray(), CancellationToken.None,
				new DatabaseParameter("$OrderId", scenario.Order.Id), new DatabaseParameter("$FirstInventoryId", fixture.InventoryId), new DatabaseParameter("$SecondInventoryId", scenario.SecondInventoryId));
		}
		return System.Text.Json.JsonSerializer.Serialize(tables);
	}

	private sealed record SplitScenario(SalesOrder Order, long SecondInventoryId, IReadOnlyList<InventoryReservation> Reservations);
}
