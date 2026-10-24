// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SalesWorkflowIntegrationTests : IAsyncLifetime
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-sales-flow-{Guid.NewGuid():N}.db");
	private SalesFixture? _fixture;

	[Fact]
	public async Task OrderToCashHappyPathCompletesOrderAndReducesStockOnlyOnShipment()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(InvoiceCustomer("Sales Integration Customer", "Billing Street 1", "Shipping Street 2"));
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, BillingAddress = "Billing Street 1", ShippingAddress = "Shipping Street 2", OrderDate = DateTime.Today, RequestedDeliveryDate = DateTime.Today.AddDays(7), Lines = [new SalesOrderLine { ItemId = fixture.ItemId, Quantity = 10, UnitPrice = 25m, TaxRate = 19m }] });
		Assert.Equal("Billing Street 1", draft.BillingAddress);
		Assert.Equal("Shipping Street 2", draft.ShippingAddress);
		var submitted = await fixture.Orders.SubmitAsync(draft.Id, draft.Version);
		var approved = await fixture.Orders.ApproveAsync(submitted.Id, submitted.Version, "Approved by integration test");
		Assert.Equal(20, await fixture.CurrentStockAsync());
		var reserved = await fixture.Orders.SetReservationsAsync(approved.Id, approved.Version, [new SalesReservationRequest(approved.Lines[0].Id, fixture.InventoryId, 10)]);
		Assert.Equal(20, await fixture.CurrentStockAsync());
		var released = await fixture.Orders.ReleaseAsync(reserved.Id, reserved.Version);
		var reservation = Assert.Single(await fixture.Orders.GetReservationsAsync(released.Id));
		var shipment = await fixture.Shipments.CreateAsync(released.Id, [new ShipmentLineRequest(reservation.Id, 10)], "DHL", "TRACK-001", "Integration shipment");
		Assert.Equal("Shipping Street 2", shipment.ShippingAddress);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostAsync(shipment.Id, shipment.Version));
		var postedShipment = await fixture.PackAndPostAsync(shipment);
		Assert.Equal(10, await fixture.CurrentStockAsync());
		var invoicedOrder = await fixture.Orders.GetByIdAsync(released.Id) ?? throw new InvalidOperationException();
		Assert.Equal(SalesOrderStatus.Shipped, invoicedOrder.Status);
		Assert.Equal(10, invoicedOrder.Lines[0].ShippedQuantity);
		var invoice = await fixture.Invoices.CreateFromShipmentAsync(postedShipment.Id);
		Assert.Equal("Billing Street 1", invoice.BillingAddress);
		var postedInvoice = await fixture.Invoices.PostAsync(invoice.Id, invoice.Version);
		var completed = await fixture.Orders.GetByIdAsync(released.Id) ?? throw new InvalidOperationException();
		Assert.Equal(SalesInvoiceStatus.Posted, postedInvoice.Status);
		Assert.Equal(SalesOrderStatus.Completed, completed.Status);
		Assert.Equal(10, completed.Lines[0].InvoicedQuantity);
		Assert.Equal(250m, postedInvoice.NetAmount);
	}

	[Fact]
	public async Task PartialFulfillmentLeavesBackorderAndCanBeCompletedLater()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Backorder Customer", Currency = "EUR" });
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = [new SalesOrderLine { ItemId = fixture.ItemId, Quantity = 15, UnitPrice = 10m, TaxRate = 19m }] });
		var submitted = await fixture.Orders.SubmitAsync(draft.Id, draft.Version);
		var approved = await fixture.Orders.ApproveAsync(submitted.Id, submitted.Version);
		var reserved = await fixture.Orders.SetReservationsAsync(approved.Id, approved.Version, [new SalesReservationRequest(approved.Lines[0].Id, fixture.InventoryId, 8)]);
		var released = await fixture.Orders.ReleaseAsync(reserved.Id, reserved.Version);
		var firstReservation = Assert.Single(await fixture.Orders.GetReservationsAsync(released.Id), value => value.Status == InventoryReservationStatus.Active);
		var firstShipment = await fixture.Shipments.CreateAsync(released.Id, [new ShipmentLineRequest(firstReservation.Id, 8)]);
		await fixture.PackAndPostAsync(firstShipment);
		var partiallyShipped = await fixture.Orders.GetByIdAsync(released.Id) ?? throw new InvalidOperationException();
		Assert.Equal(SalesOrderStatus.PartiallyShipped, partiallyShipped.Status);
		Assert.Equal(7, partiallyShipped.Lines[0].BackorderedQuantity);
		var reReserved = await fixture.Orders.SetReservationsAsync(partiallyShipped.Id, partiallyShipped.Version, [new SalesReservationRequest(partiallyShipped.Lines[0].Id, fixture.InventoryId, 7)]);
		var secondReservation = Assert.Single(await fixture.Orders.GetReservationsAsync(reReserved.Id), value => value.Status == InventoryReservationStatus.Active);
		var secondShipment = await fixture.Shipments.CreateAsync(released.Id, [new ShipmentLineRequest(secondReservation.Id, 7)]);
		await fixture.PackAndPostAsync(secondShipment);
		var shipped = await fixture.Orders.GetByIdAsync(released.Id) ?? throw new InvalidOperationException();
		Assert.Equal(SalesOrderStatus.Shipped, shipped.Status);
		Assert.Equal(0, shipped.Lines[0].BackorderedQuantity);
	}

	[Fact]
	public async Task ShipmentReversalRestoresStockAndReservation()
	{
		var fixture = Fixture;
		var posted = await fixture.CreatePostedShipmentAsync(5);
		Assert.Equal(15, await fixture.CurrentStockAsync());
		var reversed = await fixture.Shipments.ReverseAsync(posted.Id, posted.Version, "Posting error");
		var order = await fixture.Orders.GetByIdAsync(posted.SalesOrderId) ?? throw new InvalidOperationException();
		Assert.NotNull(reversed.ReversedAtUtc);
		Assert.Equal(20, await fixture.CurrentStockAsync());
		Assert.Equal(SalesOrderStatus.Released, order.Status);
		Assert.Equal(5, order.Lines[0].ReservedQuantity);
		Assert.Equal(0, order.Lines[0].ShippedQuantity);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SplitShipmentAccumulatesEveryReservationAndRestoresEveryOrderLine(bool mixedOrderLines)
	{
		var fixture = Fixture;
		var scenario = await fixture.CreateSplitOrderAsync(mixedOrderLines);
		var shipment = await fixture.CreateSplitShipmentAsync(scenario, 4, 6);
		var posted = await fixture.PackAndPostAsync(shipment);
		var shipped = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");

		Assert.Equal(SalesOrderStatus.Shipped, shipped.Status);
		foreach (var line in shipped.Lines)
		{
			Assert.Equal(line.Quantity, line.ShippedQuantity);
			Assert.Equal(0, line.ReservedQuantity);
			Assert.Equal(0, line.BackorderedQuantity);
			Assert.Equal(0, line.InvoicedQuantity);
			Assert.Equal(scenario.Order.Lines.Single(source => source.Id == line.Id).Version + 1, line.Version);
		}
		Assert.Equal(mixedOrderLines ? 13L : 16L, await fixture.CurrentStockAsync());
		Assert.Equal(14L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
		Assert.All(await fixture.Orders.GetReservationsAsync(shipped.Id), reservation => Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status));

		var reversed = await fixture.Shipments.ReverseAsync(posted.Id, posted.Version, "Reverse split shipment");
		var restored = await fixture.Orders.GetByIdAsync(shipped.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(ShipmentStatus.Cancelled, reversed.Status);
		Assert.Equal(SalesOrderStatus.Released, restored.Status);
		Assert.Equal(20L, await fixture.CurrentStockAsync());
		Assert.Equal(20L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
		foreach (var line in restored.Lines)
		{
			Assert.Equal(line.Quantity, line.ReservedQuantity);
			Assert.Equal(0, line.ShippedQuantity);
			Assert.Equal(0, line.InvoicedQuantity);
			Assert.Equal(scenario.Order.Lines.Single(source => source.Id == line.Id).Version + 2, line.Version);
		}
		var active = (await fixture.Orders.GetReservationsAsync(restored.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(scenario.Reservations.Count, active.Length);
		foreach (var original in scenario.Reservations)
			Assert.Equal(original.Quantity, Assert.Single(active, reservation => reservation.SalesOrderLineId == original.SalesOrderLineId && reservation.InventoryId == original.InventoryId).Quantity);
	}

	[Fact]
	public async Task PartialSplitShipmentReversalMergesActiveReservationsAndPreservesPendingDraft()
	{
		var fixture = Fixture;
		var scenario = await fixture.CreateSplitOrderAsync();
		var first = await fixture.CreateSplitShipmentAsync(scenario, 2, 3);
		var pending = await fixture.PackAsync(await fixture.CreateSplitShipmentAsync(scenario, 2, 3));
		var posted = await fixture.PackAndPostAsync(first);
		var partlyShipped = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(5, partlyShipped.Lines[0].ReservedQuantity);
		Assert.Equal(5, partlyShipped.Lines[0].ShippedQuantity);

		await fixture.Shipments.ReverseAsync(posted.Id, posted.Version, "Restore the partial split");
		var active = (await fixture.Orders.GetReservationsAsync(scenario.Order.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(2, active.Length);
		foreach (var original in scenario.Reservations)
			Assert.Equal(original.Quantity, Assert.Single(active, reservation => reservation.Id == original.Id).Quantity);

		await fixture.Shipments.PostAsync(pending.Id, pending.Version);
		var after = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(SalesOrderStatus.PartiallyShipped, after.Status);
		Assert.Equal(5, after.Lines[0].ReservedQuantity);
		Assert.Equal(5, after.Lines[0].ShippedQuantity);
		Assert.Equal(18L, await fixture.CurrentStockAsync());
		Assert.Equal(17L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
	}

	[Fact]
	public async Task PreexistingSplitShipmentDraftCannotExceedTheRemainingReservedQuantity()
	{
		var fixture = Fixture;
		var scenario = await fixture.CreateSplitOrderAsync();
		var first = await fixture.CreateSplitShipmentAsync(scenario, 2, 3);
		var stale = await fixture.PackAsync(await fixture.CreateSplitShipmentAsync(scenario, 4, 6));
		await fixture.PackAndPostAsync(first);
		var before = await fixture.PersistenceSnapshotAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostAsync(stale.Id, stale.Version));

		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());
		var retained = await fixture.Shipments.GetByIdAsync(stale.Id) ?? throw new InvalidOperationException("Shipment missing.");
		Assert.Equal(ShipmentStatus.Draft, retained.Status);
		Assert.Equal(stale.Version, retained.Version);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task FailedSplitShipmentAuditRollsBackEveryMovementReservationAndCounter(bool reversing)
	{
		var fixture = Fixture;
		var scenario = await fixture.CreateSplitOrderAsync(mixedOrderLines: true);
		var shipment = await fixture.PackAsync(await fixture.CreateSplitShipmentAsync(scenario, 4, 6));
		if (reversing) shipment = await fixture.Shipments.PostAsync(shipment.Id, shipment.Version);
		await fixture.SetRejectShipmentAuditAsync(true);
		var before = await fixture.PersistenceSnapshotAsync();
		Func<Task<Shipment>> operation = reversing
			? () => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Rollback split reversal")
			: () => fixture.Shipments.PostAsync(shipment.Id, shipment.Version);

		await Assert.ThrowsAsync<SqliteException>(operation);
		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());

		await fixture.SetRejectShipmentAuditAsync(false);
		await operation();
		var after = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(reversing ? SalesOrderStatus.Released : SalesOrderStatus.Shipped, after.Status);
		foreach (var line in after.Lines)
		{
			Assert.Equal(reversing ? line.Quantity : 0, line.ReservedQuantity);
			Assert.Equal(reversing ? 0 : line.Quantity, line.ShippedQuantity);
			Assert.Equal(scenario.Order.Lines.Single(source => source.Id == line.Id).Version + (reversing ? 2 : 1), line.Version);
		}
		Assert.Equal(reversing ? 20L : 13L, await fixture.CurrentStockAsync());
		Assert.Equal(reversing ? 20L : 14L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
	}

	[Fact]
	public async Task ConcurrentSplitShipmentsAndReversalsPreserveCumulativeOrderQuantities()
	{
		var fixture = Fixture;
		var scenario = await fixture.CreateSplitOrderAsync();
		var first = await fixture.PackAsync(await fixture.CreateSplitShipmentAsync(scenario, 2, 3));
		var second = await fixture.PackAsync(await fixture.CreateSplitShipmentAsync(scenario, 2, 3));

		Assert.Null(await fixture.RunCompetingWritesAsync(
			() => fixture.Shipments.PostAsync(first.Id, first.Version),
			() => fixture.Shipments.PostAsync(second.Id, second.Version)));

		var shipped = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(SalesOrderStatus.Shipped, shipped.Status);
		Assert.Equal(0, shipped.Lines[0].ReservedQuantity);
		Assert.Equal(10, shipped.Lines[0].ShippedQuantity);
		Assert.Equal(scenario.Order.Lines[0].Version + 2, shipped.Lines[0].Version);
		Assert.Equal(16L, await fixture.CurrentStockAsync());
		Assert.Equal(14L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
		first = await fixture.Shipments.GetByIdAsync(first.Id) ?? throw new InvalidOperationException("First shipment missing.");
		second = await fixture.Shipments.GetByIdAsync(second.Id) ?? throw new InvalidOperationException("Second shipment missing.");

		Assert.Null(await fixture.RunCompetingWritesAsync(
			() => fixture.Shipments.ReverseAsync(first.Id, first.Version, "First split reversal"),
			() => fixture.Shipments.ReverseAsync(second.Id, second.Version, "Second split reversal")));

		var restored = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(SalesOrderStatus.Released, restored.Status);
		Assert.Equal(10, restored.Lines[0].ReservedQuantity);
		Assert.Equal(0, restored.Lines[0].ShippedQuantity);
		Assert.Equal(scenario.Order.Lines[0].Version + 4, restored.Lines[0].Version);
		Assert.Equal(20L, await fixture.CurrentStockAsync());
		Assert.Equal(20L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
		var active = (await fixture.Orders.GetReservationsAsync(restored.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(2, active.Length);
		Assert.Equal(4, Assert.Single(active, reservation => reservation.InventoryId == fixture.InventoryId).Quantity);
		Assert.Equal(6, Assert.Single(active, reservation => reservation.InventoryId == scenario.SecondInventoryId).Quantity);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentSplitPostingAndReversalKeepTheOtherShipmentContribution(bool reversalFirst)
	{
		var fixture = Fixture;
		var scenario = await fixture.CreateSplitOrderAsync();
		var first = await fixture.PackAndPostAsync(await fixture.CreateSplitShipmentAsync(scenario, 2, 3));
		var second = await fixture.PackAsync(await fixture.CreateSplitShipmentAsync(scenario, 2, 3));
		Func<Task> post = () => fixture.Shipments.PostAsync(second.Id, second.Version);
		Func<Task> reverse = () => fixture.Shipments.ReverseAsync(first.Id, first.Version, "Overlapping split correction");

		Assert.Null(await fixture.RunCompetingWritesAsync(reversalFirst ? reverse : post, reversalFirst ? post : reverse));

		var after = await fixture.Orders.GetByIdAsync(scenario.Order.Id) ?? throw new InvalidOperationException("Sales order missing.");
		Assert.Equal(SalesOrderStatus.PartiallyShipped, after.Status);
		Assert.Equal(5, after.Lines[0].ReservedQuantity);
		Assert.Equal(5, after.Lines[0].ShippedQuantity);
		Assert.Equal(18L, await fixture.CurrentStockAsync());
		Assert.Equal(17L, await fixture.CurrentStockAsync(scenario.SecondInventoryId));
		var active = (await fixture.Orders.GetReservationsAsync(after.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
		Assert.Equal(2, active.Length);
		Assert.Equal(2, Assert.Single(active, reservation => reservation.InventoryId == fixture.InventoryId).Quantity);
		Assert.Equal(3, Assert.Single(active, reservation => reservation.InventoryId == scenario.SecondInventoryId).Quantity);
	}

	[Fact]
	public async Task CustomerReturnCannotExceedShippedQuantity()
	{
		var fixture = Fixture;
		var posted = await fixture.CreatePostedShipmentAsync(4);
		var customerReturn = await fixture.Shipments.CreateCustomerReturnAsync(posted.Id, "Customer return");
		await fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.CreateCustomerReturnAsync(posted.Id, "Duplicate return"));
	}

	[Fact]
	public async Task SecondPreexistingReturnDraftCannotCreditTheSameShipmentAgain()
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var first = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "First return");
		var second = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Competing return");
		await fixture.Shipments.PostCustomerReturnAsync(first.Id, first.Version);
		var before = await fixture.PersistenceSnapshotAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostCustomerReturnAsync(second.Id, second.Version));

		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());
		Assert.Equal(20, await fixture.CurrentStockAsync());
		var unchangedReturn = await fixture.Shipments.GetCustomerReturnByIdAsync(second.Id) ?? throw new InvalidOperationException("Customer return was not found.");
		var unchangedShipment = await fixture.Shipments.GetByIdAsync(shipment.Id) ?? throw new InvalidOperationException("Shipment was not found.");
		Assert.Equal(CustomerReturnStatus.Draft, unchangedReturn.Status);
		Assert.Equal(shipment.Version, unchangedShipment.Version);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(5)]
	public async Task PostedPartialOrFullReturnPreventsShipmentReversal(int returnQuantity)
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var customerReturn = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Returned goods");
		await fixture.SetReturnQuantityAsync(customerReturn.Lines[0].Id, returnQuantity);
		await fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version);
		var before = await fixture.PersistenceSnapshotAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Duplicate stock credit"));

		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());
		Assert.Equal(15 + returnQuantity, await fixture.CurrentStockAsync());
		var unchangedShipment = await fixture.Shipments.GetByIdAsync(shipment.Id) ?? throw new InvalidOperationException("Shipment was not found.");
		Assert.Equal(ShipmentStatus.Posted, unchangedShipment.Status);
		Assert.DoesNotContain(await fixture.Orders.GetReservationsAsync(shipment.SalesOrderId), reservation => reservation.Status == InventoryReservationStatus.Active);
		if (returnQuantity < 5)
		{
			var remaining = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Remaining goods");
			Assert.Equal(5 - returnQuantity, Assert.Single(remaining.Lines).Quantity);
			await fixture.Shipments.PostCustomerReturnAsync(remaining.Id, remaining.Version);
			Assert.Equal(20, await fixture.CurrentStockAsync());
		}
	}

	[Fact]
	public async Task ReturnDraftCannotPostAfterItsShipmentWasReversed()
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var customerReturn = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Prepared return");
		await fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Shipment posted in error");
		var before = await fixture.PersistenceSnapshotAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version));

		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());
		Assert.Equal(20, await fixture.CurrentStockAsync());
		Assert.Equal(5, Assert.Single(await fixture.Orders.GetReservationsAsync(shipment.SalesOrderId), value => value.Status == InventoryReservationStatus.Active).Quantity);
	}

	[Fact]
	public async Task ReturnQuantitiesAreValidatedPerShipmentLineBeforeAnyStockIsCredited()
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(2, 3);
		var customerReturn = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Mixed-line return");
		Assert.Equal(2, customerReturn.Lines.Count);
		await fixture.SetReturnQuantityAsync(customerReturn.Lines[1].Id, 4);
		var before = await fixture.PersistenceSnapshotAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version));

		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());
		Assert.Equal(15, await fixture.CurrentStockAsync());
		await fixture.SetReturnQuantityAsync(customerReturn.Lines[1].Id, 3);
		await fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version);
		Assert.Equal(20, await fixture.CurrentStockAsync());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentReturnPostsCreditShipmentOnlyOnce(bool sameDraft)
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var first = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "First return");
		var second = sameDraft ? first : await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Competing return");

		var rejected = await fixture.RunCompetingWritesAsync(
			() => fixture.Shipments.PostCustomerReturnAsync(first.Id, first.Version),
			() => fixture.Shipments.PostCustomerReturnAsync(second.Id, second.Version));

		Assert.IsAssignableFrom<InvalidOperationException>(rejected);
		Assert.Equal(20, await fixture.CurrentStockAsync());
		var postedReturn = await fixture.Shipments.GetCustomerReturnByIdAsync(first.Id) ?? throw new InvalidOperationException("First customer return was not found.");
		var otherReturn = await fixture.Shipments.GetCustomerReturnByIdAsync(second.Id) ?? throw new InvalidOperationException("Second customer return was not found.");
		Assert.Equal(CustomerReturnStatus.Posted, postedReturn.Status);
		Assert.Equal(sameDraft ? CustomerReturnStatus.Posted : CustomerReturnStatus.Draft, otherReturn.Status);
		Assert.Equal(1L, await fixture.CountReturnMovementsAsync());
		Assert.DoesNotContain(await fixture.Orders.GetReservationsAsync(shipment.SalesOrderId), value => value.Status == InventoryReservationStatus.Active);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentReturnAndReversalHaveOnlyOneStockCredit(bool reversalFirst)
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var customerReturn = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Contending return");
		Func<Task> postReturn = () => fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version);
		Func<Task> reverseShipment = () => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Contending reversal");

		var rejected = await fixture.RunCompetingWritesAsync(reversalFirst ? reverseShipment : postReturn, reversalFirst ? postReturn : reverseShipment);

		Assert.IsAssignableFrom<InvalidOperationException>(rejected);
		Assert.Equal(20, await fixture.CurrentStockAsync());
		var afterShipment = await fixture.Shipments.GetByIdAsync(shipment.Id) ?? throw new InvalidOperationException("Shipment was not found.");
		var afterReturn = await fixture.Shipments.GetCustomerReturnByIdAsync(customerReturn.Id) ?? throw new InvalidOperationException("Customer return was not found.");
		Assert.Equal(reversalFirst ? ShipmentStatus.Cancelled : ShipmentStatus.Posted, afterShipment.Status);
		Assert.Equal(reversalFirst ? CustomerReturnStatus.Draft : CustomerReturnStatus.Posted, afterReturn.Status);
		var order = await fixture.Orders.GetByIdAsync(shipment.SalesOrderId) ?? throw new InvalidOperationException("Sales order was not found.");
		Assert.Equal(reversalFirst ? 5 : 0, order.Lines[0].ReservedQuantity);
		Assert.Equal(reversalFirst ? 0 : 5, order.Lines[0].ShippedQuantity);
		Assert.Equal(reversalFirst ? 0L : 1L, await fixture.CountReturnMovementsAsync());
	}

	[Fact]
	public async Task FailedReturnAuditRollsBackStockAndLeavesReturnableQuantityAvailable()
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(5);
		var customerReturn = await fixture.Shipments.CreateCustomerReturnAsync(shipment.Id, "Audit failure recovery");
		await fixture.SetRejectReturnAuditAsync(true);
		var before = await fixture.PersistenceSnapshotAsync();

		await Assert.ThrowsAsync<SqliteException>(() => fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version));

		Assert.Equal(before, await fixture.PersistenceSnapshotAsync());
		Assert.Equal(15, await fixture.CurrentStockAsync());
		await fixture.SetRejectReturnAuditAsync(false);
		await fixture.Shipments.PostCustomerReturnAsync(customerReturn.Id, customerReturn.Version);
		Assert.Equal(20, await fixture.CurrentStockAsync());
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shipments.ReverseAsync(shipment.Id, shipment.Version, "Already returned"));
	}

	[Fact]
	public async Task PostedInvoiceCanBeCorrectedWithCreditNote()
	{
		var fixture = Fixture;
		var shipment = await fixture.CreatePostedShipmentAsync(3);
		var invoice = await fixture.Invoices.CreateFromShipmentAsync(shipment.Id);
		var posted = await fixture.Invoices.PostAsync(invoice.Id, invoice.Version);
		var credit = await fixture.Invoices.CreateCreditNoteAsync(posted.Id, "Commercial correction");
		var postedCredit = await fixture.Invoices.PostCreditNoteAsync(credit.Id, credit.Version);
		Assert.Equal(SalesCreditNoteStatus.Posted, postedCredit.Status);
		Assert.Equal(posted.GrossAmount, postedCredit.GrossAmount);
	}

	public async Task InitializeAsync()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		_fixture = await SalesFixture.CreateAsync(factory);
	}

	public Task DisposeAsync()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		return Task.CompletedTask;
	}

	private SalesFixture Fixture => _fixture ?? throw new InvalidOperationException("Sales fixture is not initialized.");

	private static Customer InvoiceCustomer(string name, string billingAddress = "Billing Street 1", string? shippingAddress = null) => new()
	{
		Name = name,
		BillingAddress = billingAddress,
		ShippingAddress = shippingAddress,
		Currency = "EUR",
		PaymentTermsDays = 30,
		TaxId = "DE123456789",
		BuyerReference = "BUYER-REF",
		EInvoiceEndpoint = "buyer@example.test",
		EInvoiceEndpointScheme = "EM",
		BillingStreet = billingAddress,
		BillingPostalCode = "53111",
		BillingCity = "Bonn",
		BillingCountryCode = "DE"
	};

	private sealed record SplitShipmentScenario(SalesOrder Order, long SecondInventoryId, IReadOnlyList<InventoryReservation> Reservations);

	private sealed class SalesFixture
	{
		private readonly DatabaseAccess _data;
		private readonly PausingTransactionRunner _transactions;
		private SalesFixture(DatabaseAccess data, PausingTransactionRunner transactions, CustomerService customers, SalesOrderService orders, ShipmentService shipments, ShipmentPackingService packing, SalesInvoiceService invoices, long itemId, long inventoryId)
		{
			_data = data; _transactions = transactions; Customers = customers; Orders = orders; Shipments = shipments; Packing = packing; Invoices = invoices; ItemId = itemId; InventoryId = inventoryId;
		}
		public CustomerService Customers { get; }
		public SalesOrderService Orders { get; }
		public ShipmentService Shipments { get; }
		public ShipmentPackingService Packing { get; }
		public SalesInvoiceService Invoices { get; }
		public long ItemId { get; }
		public long InventoryId { get; }

		public static async Task<SalesFixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data = new DatabaseAccess(factory);
			var authorization = new AuthorizationService();
			var roles = new RoleRepository(data);
			var users = new UserRepository(data);
			var admin = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
			admin.Roles = await roles.GetUserRolesAsync(admin.Id, CancellationToken.None); admin.EffectivePermissions = PermissionCatalog.All; authorization.SignIn(admin, PermissionCatalog.All);
			var company = new CompanyProfileService(data, DatabaseProvider.Local, authorization);
			await company.SaveAsync(new CompanyProfile
			{
				LegalName = "Depot Sales Integration GmbH",
				LegalForm = "GmbH",
				Street = "Test Street 1",
				PostalCode = "53111",
				City = "Bonn",
				CountryCode = "DE",
				TaxResidenceCountryCode = "DE",
				RegisteredOffice = "Bonn",
				IsRegisteredEntity = true,
				RegisterCourt = "Amtsgericht Bonn",
				RegisterType = "HRB",
				RegisterNumber = "12345",
				ManagingDirectors = "Integration Test",
				VatId = "DE111111111",
				Email = "sales-integration@depot.test",
				InvoiceEmail = "invoice-integration@depot.test",
				Phone = "+49 228 000000",
				Iban = "DE89370400440532013000",
				DefaultCurrency = "EUR",
				PaymentTermsDays = 14
			});
			var suffix = Guid.NewGuid().ToString("N");
			var itemId = await data.InsertAsync("INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($PartNumber,$Description,1);", CancellationToken.None, new DatabaseParameter("$PartNumber", $"SALES-{suffix}"), new DatabaseParameter("$Description", "Sales integration item"));
			var purposeId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT MIN(Id) FROM Purposes;", CancellationToken.None));
			var locationId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT MIN(Id) FROM StorageLocations;", CancellationToken.None));
			var inventoryId = await data.InsertAsync("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($ItemId,$PurposeId,$LocationId,1);", CancellationToken.None, new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$PurposeId", purposeId), new DatabaseParameter("$LocationId", locationId));
			await data.InsertAsync("INSERT INTO StockMovements (InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice,Reference) VALUES ($InventoryId,$Type,$Timestamp,20,0,$Reference);", CancellationToken.None, new DatabaseParameter("$InventoryId", inventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$Timestamp", DateTime.UtcNow.ToString("O")), new DatabaseParameter("$Reference", "Sales integration opening stock"));
			var auditRepository = new AuditRepository(data); var audit = new AuditService(auditRepository, authorization); var runner = new PausingTransactionRunner(new DatabaseTransactionRunner(data));
			var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
			var customerRepository = new CustomerRepository(data); var orderRepository = new SalesOrderRepository(data); var reservationRepository = new InventoryReservationRepository(data); var movementRepository = new StockMovementRepository(data); var inventoryRepository = new InventoryRepository(data); var shipmentRepository = new ShipmentRepository(data); var invoiceRepository = new SalesInvoiceRepository(data); var customerReturnRepository = new CustomerReturnRepository(data); var creditNoteRepository = new SalesCreditNoteRepository(data);
			var customers = new CustomerService(customerRepository, audit, authorization);
			var orders = new SalesOrderService(runner, orderRepository, customerRepository, new ItemRepository(data), inventoryRepository, reservationRepository, movementRepository, auditRepository, audit, authorization, notifications);
			var customerReturns = new CustomerReturnService(runner, customerReturnRepository, shipmentRepository, movementRepository, auditRepository, audit, authorization, notifications);
			var credits = new SalesCreditNoteService(runner, creditNoteRepository, invoiceRepository, auditRepository, audit, authorization, notifications);
			var shipments = new ShipmentService(runner, shipmentRepository, orderRepository, reservationRepository, inventoryRepository, movementRepository, invoiceRepository, customerReturns, auditRepository, audit, authorization, notifications);
			var packing = new ShipmentPackingService(runner, shipmentRepository, auditRepository, audit, authorization);
			var invoices = new SalesInvoiceService(runner, invoiceRepository, shipmentRepository, orderRepository, customerRepository, auditRepository, audit, authorization, notifications, credits);
			return new SalesFixture(data, runner, customers, orders, shipments, packing, invoices, itemId, inventoryId);
		}

		public Task<long> CurrentStockAsync() => CurrentStockAsync(InventoryId);
		public async Task<long> CurrentStockAsync(long inventoryId) => Convert.ToInt64(await _data.ExecuteScalarAsync("SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$InventoryId;", CancellationToken.None, new DatabaseParameter("$InventoryId", inventoryId)));
		public async Task<long> CountReturnMovementsAsync() => Convert.ToInt64(await _data.ExecuteScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE InventoryId=$InventoryId AND MovementType=$Type;", CancellationToken.None, new DatabaseParameter("$InventoryId", InventoryId), new DatabaseParameter("$Type", (int)StockMovementType.CustomerReturn)));
		public Task SetReturnQuantityAsync(long lineId, int quantity) => _data.ExecuteAsync("UPDATE CustomerReturnLines SET Quantity=$Quantity WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Quantity", quantity), new DatabaseParameter("$Id", lineId));
		public Task SetRejectReturnAuditAsync(bool reject) => _data.ExecuteAsync(reject
			? "CREATE TRIGGER RejectCustomerReturnAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType='CustomerReturn' BEGIN SELECT RAISE(ABORT, 'return audit rejected'); END;"
			: "DROP TRIGGER RejectCustomerReturnAudit;", CancellationToken.None);
		public Task SetRejectShipmentAuditAsync(bool reject) => _data.ExecuteAsync(reject
			? "CREATE TRIGGER RejectShipmentAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType='Shipment' BEGIN SELECT RAISE(ABORT, 'shipment audit rejected'); END;"
			: "DROP TRIGGER RejectShipmentAudit;", CancellationToken.None);
		public async Task<string> PersistenceSnapshotAsync()
		{
			var tables = new Dictionary<string, IReadOnlyList<object?[]>>();
			foreach (var table in new[] { "StockMovements", "Inventories", "InventoryReservations", "SalesOrders", "SalesOrderLines", "Shipments", "ShipmentLines", "CustomerReturns", "CustomerReturnLines", "AuditEntries" })
			{
				tables[table] = await _data.QueryAsync($"SELECT * FROM {table} ORDER BY Id;", reader =>
					Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray(), CancellationToken.None);
			}
			return System.Text.Json.JsonSerializer.Serialize(tables);
		}
		public async Task<Exception?> RunCompetingWritesAsync(Func<Task> first, Func<Task> second)
		{
			using var pause = _transactions.PauseNextCommit();
			var firstTask = Task.Run(() => Record.ExceptionAsync(first));
			Task<Exception?>? secondTask = null;
			Exception? firstFailure = null;
			Exception? secondFailure = null;
			try
			{
				await pause.BeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(20));
				secondTask = Task.Run(() => Record.ExceptionAsync(second));
				await pause.ContenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
			}
			finally
			{
				pause.Resume();
				try
				{
					firstFailure = await firstTask;
				}
				finally
				{
					if (secondTask is not null) secondFailure = await secondTask;
				}
			}
			Assert.Null(firstFailure);
			if (secondTask is null) throw new InvalidOperationException("The competing write did not start.");
			return secondFailure;
		}
		public async Task<Shipment> PackAsync(Shipment shipment)
		{
			var picking = await Packing.SetStatusAsync(shipment.Id, shipment.Version, ShipmentPackingStatus.Picking);
			return await Packing.SetStatusAsync(picking.Id, picking.Version, ShipmentPackingStatus.Packed);
		}
		public async Task<Shipment> PackAndPostAsync(Shipment shipment)
		{
			var packed = await PackAsync(shipment);
			return await Shipments.PostAsync(packed.Id, packed.Version);
		}
		public async Task<SplitShipmentScenario> CreateSplitOrderAsync(bool mixedOrderLines = false)
		{
			var warehouseId = Convert.ToInt64(await _data.ExecuteScalarAsync("SELECT sl.WarehouseId FROM StorageLocations sl INNER JOIN Inventories inv ON inv.StorageLocationId=sl.Id WHERE inv.Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", InventoryId)));
			var purposeId = Convert.ToInt64(await _data.ExecuteScalarAsync("SELECT PurposeId FROM Inventories WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", InventoryId)));
			var locationId = await _data.InsertAsync("INSERT INTO StorageLocations (WarehouseId,Name,Description,IsActive) VALUES ($WarehouseId,$Name,'Split shipment bin',1);", CancellationToken.None,
				new DatabaseParameter("$WarehouseId", warehouseId), new DatabaseParameter("$Name", $"Split-{Guid.NewGuid():N}"));
			var secondInventoryId = await _data.InsertAsync("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($ItemId,$PurposeId,$LocationId,1);", CancellationToken.None,
				new DatabaseParameter("$ItemId", ItemId), new DatabaseParameter("$PurposeId", purposeId), new DatabaseParameter("$LocationId", locationId));
			await _data.InsertAsync("INSERT INTO StockMovements (InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice,Reference) VALUES ($InventoryId,$Type,$Timestamp,20,0,'Split shipment opening stock');", CancellationToken.None,
				new DatabaseParameter("$InventoryId", secondInventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$Timestamp", DateTime.UtcNow.ToString("O")));
			var customer = await Customers.SaveAsync(InvoiceCustomer($"Split Customer {Guid.NewGuid():N}"));
			int[] quantities = mixedOrderLines ? [10, 3] : [10];
			var draft = await Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = quantities.Select(quantity => new SalesOrderLine { ItemId = ItemId, Quantity = quantity, UnitPrice = 15m, TaxRate = 19m }).ToArray() });
			var submitted = await Orders.SubmitAsync(draft.Id, draft.Version);
			var approved = await Orders.ApproveAsync(submitted.Id, submitted.Version);
			var requests = new List<SalesReservationRequest>
			{
				new(approved.Lines[0].Id, InventoryId, 4),
				new(approved.Lines[0].Id, secondInventoryId, 6)
			};
			if (mixedOrderLines) requests.Add(new(approved.Lines[1].Id, InventoryId, 3));
			var reserved = await Orders.SetReservationsAsync(approved.Id, approved.Version, requests);
			var released = await Orders.ReleaseAsync(reserved.Id, reserved.Version);
			var reservations = (await Orders.GetReservationsAsync(released.Id)).Where(reservation => reservation.Status == InventoryReservationStatus.Active).ToArray();
			return new SplitShipmentScenario(released, secondInventoryId, reservations);
		}
		public Task<Shipment> CreateSplitShipmentAsync(SplitShipmentScenario scenario, int firstQuantity, int secondQuantity) =>
			Shipments.CreateAsync(scenario.Order.Id, scenario.Reservations.Select(reservation => new ShipmentLineRequest(reservation.Id,
				reservation.SalesOrderLineId != scenario.Order.Lines[0].Id ? reservation.Quantity : reservation.InventoryId == InventoryId ? firstQuantity : secondQuantity)).ToArray());
		public async Task<Shipment> CreatePostedShipmentAsync(params int[] quantities)
		{
			var customer = await Customers.SaveAsync(InvoiceCustomer($"Shipment Customer {Guid.NewGuid():N}"));
			var draft = await Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = quantities.Select(quantity => new SalesOrderLine { ItemId = ItemId, Quantity = quantity, UnitPrice = 15m, TaxRate = 19m }).ToList() });
			var submitted = await Orders.SubmitAsync(draft.Id, draft.Version); var approved = await Orders.ApproveAsync(submitted.Id, submitted.Version);
			var reserved = await Orders.SetReservationsAsync(approved.Id, approved.Version, approved.Lines.Select(line => new SalesReservationRequest(line.Id, InventoryId, line.Quantity)).ToArray());
			var released = await Orders.ReleaseAsync(reserved.Id, reserved.Version);
			var reservations = (await Orders.GetReservationsAsync(released.Id)).Where(value => value.Status == InventoryReservationStatus.Active).ToArray();
			var shipment = await Shipments.CreateAsync(released.Id, reservations.Select(reservation => new ShipmentLineRequest(reservation.Id, reservation.Quantity)).ToArray()); return await PackAndPostAsync(shipment);
		}
	}

	private sealed class PausingTransactionRunner(IDatabaseTransactionRunner inner) : IDatabaseTransactionRunner
	{
		private CommitPause? _pause;
		public CommitPause PauseNextCommit() => _pause = new CommitPause();
		public Task<T> ExecuteAsync<T>(Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
		{
			var pause = _pause;
			var isFirst = pause is not null && Interlocked.CompareExchange(ref pause.Started, 1, 0) == 0;
			if (pause is not null && !isFirst) pause.ContenderStarted.TrySetResult();
			return inner.ExecuteAsync(async (transaction, token) =>
			{
				var result = await operation(transaction, token);
				if (isFirst && pause is not null)
				{
					pause.BeforeCommit.TrySetResult();
					await pause.Continue.Task.WaitAsync(token);
				}
				return result;
			}, cancellationToken);
		}
	}

	private sealed class CommitPause : IDisposable
	{
		public int Started;
		public TaskCompletionSource BeforeCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource ContenderStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public void Resume() => Continue.TrySetResult();
		public void Dispose() => Resume();
	}
}
