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
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Sales Integration Customer", BillingAddress = "Billing Street 1", ShippingAddress = "Shipping Street 2", Currency = "EUR", PaymentTermsDays = 30 });
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
		var secondShipment = await fixture.Shipments.CreateAsync(reReserved.Id, [new ShipmentLineRequest(secondReservation.Id, 7)]);
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

	private sealed class SalesFixture
	{
		private readonly DatabaseAccess _data;
		private SalesFixture(DatabaseAccess data, CustomerService customers, SalesOrderService orders, ShipmentService shipments, ShipmentPackingService packing, SalesInvoiceService invoices, long itemId, long inventoryId)
		{
			_data = data; Customers = customers; Orders = orders; Shipments = shipments; Packing = packing; Invoices = invoices; ItemId = itemId; InventoryId = inventoryId;
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
			var suffix = Guid.NewGuid().ToString("N");
			var itemId = await data.InsertAsync("INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($PartNumber,$Description,1);", CancellationToken.None, new DatabaseParameter("$PartNumber", $"SALES-{suffix}"), new DatabaseParameter("$Description", "Sales integration item"));
			var purposeId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT MIN(Id) FROM Purposes;", CancellationToken.None));
			var locationId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT MIN(Id) FROM StorageLocations;", CancellationToken.None));
			var inventoryId = await data.InsertAsync("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($ItemId,$PurposeId,$LocationId,1);", CancellationToken.None, new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$PurposeId", purposeId), new DatabaseParameter("$LocationId", locationId));
			await data.InsertAsync("INSERT INTO StockMovements (InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice,Reference) VALUES ($InventoryId,$Type,$Timestamp,20,0,$Reference);", CancellationToken.None, new DatabaseParameter("$InventoryId", inventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$Timestamp", DateTime.UtcNow.ToString("O")), new DatabaseParameter("$Reference", "Sales integration opening stock"));
			var auditRepository = new AuditRepository(data); var audit = new AuditService(auditRepository, authorization); var runner = new DatabaseTransactionRunner(data);
			var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
			var customerRepository = new CustomerRepository(data); var orderRepository = new SalesOrderRepository(data); var reservationRepository = new InventoryReservationRepository(data); var movementRepository = new StockMovementRepository(data); var inventoryRepository = new InventoryRepository(data); var shipmentRepository = new ShipmentRepository(data); var invoiceRepository = new SalesInvoiceRepository(data); var customerReturnRepository = new CustomerReturnRepository(data); var creditNoteRepository = new SalesCreditNoteRepository(data);
			var customers = new CustomerService(customerRepository, audit, authorization);
			var orders = new SalesOrderService(runner, orderRepository, customerRepository, new ItemRepository(data), inventoryRepository, reservationRepository, movementRepository, auditRepository, audit, authorization, notifications);
			var customerReturns = new CustomerReturnService(runner, customerReturnRepository, shipmentRepository, movementRepository, auditRepository, audit, authorization, notifications);
			var credits = new SalesCreditNoteService(runner, creditNoteRepository, invoiceRepository, auditRepository, audit, authorization, notifications);
			var shipments = new ShipmentService(runner, shipmentRepository, orderRepository, reservationRepository, inventoryRepository, movementRepository, invoiceRepository, customerReturns, auditRepository, audit, authorization, notifications);
			var packing = new ShipmentPackingService(runner, shipmentRepository, auditRepository, audit, authorization);
			var invoices = new SalesInvoiceService(runner, invoiceRepository, shipmentRepository, orderRepository, customerRepository, auditRepository, audit, authorization, notifications, credits);
			return new SalesFixture(data, customers, orders, shipments, packing, invoices, itemId, inventoryId);
		}

		public async Task<long> CurrentStockAsync() => Convert.ToInt64(await _data.ExecuteScalarAsync("SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$InventoryId;", CancellationToken.None, new DatabaseParameter("$InventoryId", InventoryId)));
		public async Task<Shipment> PackAndPostAsync(Shipment shipment)
		{
			var picking = await Packing.SetStatusAsync(shipment.Id, shipment.Version, ShipmentPackingStatus.Picking);
			var packed = await Packing.SetStatusAsync(picking.Id, picking.Version, ShipmentPackingStatus.Packed);
			return await Shipments.PostAsync(packed.Id, packed.Version);
		}
		public async Task<Shipment> CreatePostedShipmentAsync(int quantity)
		{
			var customer = await Customers.SaveAsync(new Customer { Name = $"Shipment Customer {Guid.NewGuid():N}", Currency = "EUR" });
			var draft = await Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = [new SalesOrderLine { ItemId = ItemId, Quantity = quantity, UnitPrice = 15m, TaxRate = 19m }] });
			var submitted = await Orders.SubmitAsync(draft.Id, draft.Version); var approved = await Orders.ApproveAsync(submitted.Id, submitted.Version);
			var reserved = await Orders.SetReservationsAsync(approved.Id, approved.Version, [new SalesReservationRequest(approved.Lines[0].Id, InventoryId, quantity)]);
			var released = await Orders.ReleaseAsync(reserved.Id, reserved.Version); var reservation = Assert.Single(await Orders.GetReservationsAsync(released.Id), value => value.Status == InventoryReservationStatus.Active);
			var shipment = await Shipments.CreateAsync(released.Id, [new ShipmentLineRequest(reservation.Id, quantity)]); return await PackAndPostAsync(shipment);
		}
	}
}
