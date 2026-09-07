// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Full")]
public sealed class SalesProviderAcceptanceTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerOrderToCash() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbOrderToCash() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlOrderToCash() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var fixture = await SalesFixture.CreateAsync(factory);
		var customer = await fixture.Customers.SaveAsync(InvoiceCustomer($"Provider Customer {Guid.NewGuid():N}"));
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder
		{
			CustomerId = customer.Id,
			BillingAddress = "Billing Street 1",
			ShippingAddress = "Shipping Street 2",
			OrderDate = DateTime.Today,
			RequestedDeliveryDate = DateTime.Today.AddDays(7),
			Lines = [new SalesOrderLine { ItemId = fixture.ItemId, Quantity = 10, UnitPrice = 25.1234m, TaxRate = 19m }]
		});
		var submitted = await fixture.Orders.SubmitAsync(draft.Id, draft.Version);
		var approved = await fixture.Orders.ApproveAsync(submitted.Id, submitted.Version, "Provider acceptance");
		var reserved = await fixture.Orders.SetReservationsAsync(approved.Id, approved.Version, [new SalesReservationRequest(approved.Lines[0].Id, fixture.InventoryId, 10)]);
		var released = await fixture.Orders.ReleaseAsync(reserved.Id, reserved.Version);
		var reservation = Assert.Single(await fixture.Orders.GetReservationsAsync(released.Id), value => value.Status == InventoryReservationStatus.Active);
		var shipment = await fixture.Shipments.CreateAsync(released.Id, [new ShipmentLineRequest(reservation.Id, 10)], "Acceptance", "TRACK-001", "Provider shipment");
		var postedShipment = await fixture.PackAndPostAsync(shipment);
		Assert.Equal(10, await fixture.CurrentStockAsync());
		var invoice = await fixture.Invoices.CreateFromShipmentAsync(postedShipment.Id);
		var postedInvoice = await fixture.Invoices.PostAsync(invoice.Id, invoice.Version);
		var completed = await fixture.Orders.GetByIdAsync(released.Id) ?? throw new InvalidOperationException();
		Assert.Equal(SalesInvoiceStatus.Posted, postedInvoice.Status);
		Assert.Equal(SalesOrderStatus.Completed, completed.Status);
		Assert.Equal(251.234m, postedInvoice.NetAmount);
		Assert.Equal(10, completed.Lines[0].InvoicedQuantity);
	}

	private static Customer InvoiceCustomer(string name) => new()
	{
		Name = name,
		BillingAddress = "Billing Street 1",
		ShippingAddress = "Shipping Street 2",
		Currency = "EUR",
		PaymentTermsDays = 30,
		TaxId = "DE123456789",
		BuyerReference = "BUYER-REF",
		EInvoiceEndpoint = "buyer@example.test",
		EInvoiceEndpointScheme = "EM",
		BillingStreet = "Billing Street 1",
		BillingPostalCode = "53111",
		BillingCity = "Bonn",
		BillingCountryCode = "DE"
	};

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
			admin.Roles = await roles.GetUserRolesAsync(admin.Id, CancellationToken.None);
			admin.EffectivePermissions = PermissionCatalog.All;
			authorization.SignIn(admin, PermissionCatalog.All);
			var company = new CompanyProfileService(data, factory.Provider, authorization);
			await company.SaveAsync(new CompanyProfile
			{
				LegalName = "Depot Provider Acceptance GmbH", LegalForm = "GmbH", Street = "Test Street 1", PostalCode = "53111", City = "Bonn",
				CountryCode = "DE", TaxResidenceCountryCode = "DE", RegisteredOffice = "Bonn", IsRegisteredEntity = true, RegisterCourt = "Amtsgericht Bonn",
				RegisterType = "HRB", RegisterNumber = "12345", ManagingDirectors = "Acceptance Test", VatId = "DE111111111", Email = "acceptance@depot.test",
				InvoiceEmail = "invoice-acceptance@depot.test", Phone = "+49 228 000000", Iban = "DE89370400440532013000", DefaultCurrency = "EUR", PaymentTermsDays = 14
			});
			var suffix = Guid.NewGuid().ToString("N");
			var itemId = await data.InsertAsync("INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($PartNumber,$Description,1);", CancellationToken.None,
				new DatabaseParameter("$PartNumber", $"SALES-{suffix}"), new DatabaseParameter("$Description", "Provider sales item"));
			var purposeId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT MIN(Id) FROM Purposes;", CancellationToken.None));
			var locationId = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT MIN(Id) FROM StorageLocations;", CancellationToken.None));
			var inventoryId = await data.InsertAsync("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($ItemId,$PurposeId,$LocationId,1);", CancellationToken.None,
				new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$PurposeId", purposeId), new DatabaseParameter("$LocationId", locationId));
			await data.InsertAsync("INSERT INTO StockMovements (InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice,Reference) VALUES ($InventoryId,$Type,$Timestamp,20,0,$Reference);", CancellationToken.None,
				new DatabaseParameter("$InventoryId", inventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$Timestamp", DateTime.UtcNow.ToString("O")), new DatabaseParameter("$Reference", "Provider opening stock"));
			var auditRepository = new AuditRepository(data);
			var audit = new AuditService(auditRepository, authorization);
			var runner = new DatabaseTransactionRunner(data);
			var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
			var customerRepository = new CustomerRepository(data);
			var orderRepository = new SalesOrderRepository(data);
			var reservationRepository = new InventoryReservationRepository(data);
			var movementRepository = new StockMovementRepository(data);
			var inventoryRepository = new InventoryRepository(data);
			var shipmentRepository = new ShipmentRepository(data);
			var invoiceRepository = new SalesInvoiceRepository(data);
			var customerReturnRepository = new CustomerReturnRepository(data);
			var creditNoteRepository = new SalesCreditNoteRepository(data);
			var customers = new CustomerService(customerRepository, audit, authorization);
			var orders = new SalesOrderService(runner, orderRepository, customerRepository, new ItemRepository(data), inventoryRepository, reservationRepository, movementRepository, auditRepository, audit, authorization, notifications);
			var customerReturns = new CustomerReturnService(runner, customerReturnRepository, shipmentRepository, movementRepository, auditRepository, audit, authorization, notifications);
			var credits = new SalesCreditNoteService(runner, creditNoteRepository, invoiceRepository, auditRepository, audit, authorization, notifications);
			var shipments = new ShipmentService(runner, shipmentRepository, orderRepository, reservationRepository, inventoryRepository, movementRepository, invoiceRepository, customerReturns, auditRepository, audit, authorization, notifications);
			var packing = new ShipmentPackingService(runner, shipmentRepository, auditRepository, audit, authorization);
			var invoices = new SalesInvoiceService(runner, invoiceRepository, shipmentRepository, orderRepository, customerRepository, auditRepository, audit, authorization, notifications, credits);
			return new SalesFixture(data, customers, orders, shipments, packing, invoices, itemId, inventoryId);
		}

		public async Task<long> CurrentStockAsync() => Convert.ToInt64(await _data.ExecuteScalarAsync(
			"SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$InventoryId;", CancellationToken.None, new DatabaseParameter("$InventoryId", InventoryId)));

		public async Task<Shipment> PackAndPostAsync(Shipment shipment)
		{
			var picking = await Packing.SetStatusAsync(shipment.Id, shipment.Version, ShipmentPackingStatus.Picking);
			var packed = await Packing.SetStatusAsync(picking.Id, picking.Version, ShipmentPackingStatus.Packed);
			return await Shipments.PostAsync(packed.Id, packed.Version);
		}
	}
}
