// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

internal sealed class StockWorkflowProviderFixture : IAsyncDisposable
{
	private readonly ProcurementTestContext _context;
	private readonly long _customerId;
	private readonly long _purposeId;
	private readonly long _locationId;

	private StockWorkflowProviderFixture(ProcurementTestContext context, ObservedProviderConnectionFactory factory,
		ProviderTransactionGate transactions, MovementService movements, ServiceManagementService service,
		ProductionService production, SalesOrderService salesOrders, ItemCostCalculationService costs,
		long customerId, long purposeId, long locationId, long warehouseId, long reasonCodeId)
	{
		_context = context;
		Factory = factory;
		Transactions = transactions;
		Movements = movements;
		Service = service;
		Production = production;
		SalesOrders = salesOrders;
		Costs = costs;
		_customerId = customerId;
		_purposeId = purposeId;
		_locationId = locationId;
		WarehouseId = warehouseId;
		ReasonCodeId = reasonCodeId;
	}

	public DatabaseAccess Data => _context.Data;
	public ObservedProviderConnectionFactory Factory { get; }
	public ProviderTransactionGate Transactions { get; }
	public MovementService Movements { get; }
	public ServiceManagementService Service { get; }
	public ProductionService Production { get; }
	public SalesOrderService SalesOrders { get; }
	public ItemCostCalculationService Costs { get; }
	public long WarehouseId { get; }
	public long ReasonCodeId { get; }

	public static async Task<StockWorkflowProviderFixture> CreateAsync(IDatabaseConnectionFactory innerFactory)
	{
		var factory = new ObservedProviderConnectionFactory(innerFactory);
		var context = await ProcurementTestContext.CreateProviderAcceptanceAsync(innerFactory, factory);
		var data = context.Data;
		var authorization = context.Authorization;
		var runner = new DatabaseTransactionRunner(data);
		var gate = new ProviderTransactionGate(runner);
		var audits = new AuditRepository(data);
		var audit = new AuditService(audits, authorization);
		var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
		var inventories = new InventoryRepository(data);
		var stockMovements = new StockMovementRepository(data);
		var reasons = new ReasonCodeRepository(data);
		var items = new ItemRepository(data);
		var warehouses = new WarehouseRepository(data);
		var traceability = new ItemTraceabilityService(new ItemTraceabilityRepository(data), audit);
		var reversals = new StockMovementReversalService(runner, inventories, stockMovements, reasons, audits, audit, traceability);
		// Standalone movement calls use their own real runner. Failure injection belongs to
		// the owning workflow so an accidentally separate stock commit remains observable.
		var movements = new MovementService(items, inventories, reasons, stockMovements, audit, reversals, runner, audits, traceability);
		var customers = new CustomerRepository(data);
		var orders = new SalesOrderRepository(data);
		var invoices = new SalesInvoiceRepository(data);
		var credits = new SalesCreditNoteService(runner, new SalesCreditNoteRepository(data), invoices, audits, audit, authorization, notifications);
		var invoiceService = new SalesInvoiceService(runner, invoices, new ShipmentRepository(data), orders, customers, audits, audit, authorization, notifications, credits);
		var service = new ServiceManagementService(gate, new ServiceManagementRepository(data), customers, audits, audit, authorization, notifications, movements, invoiceService);
		var salesOrders = new SalesOrderService(gate, orders, customers, items, inventories, new InventoryReservationRepository(data), stockMovements, audits, audit, authorization, notifications, traceability);
		var costs = new ItemCostCalculationService(runner, new ItemCostRepository(data), audits, audit, authorization);
		var replenishment = new ReplenishmentService(runner, new ReplenishmentRepository(data), items, warehouses, new SupplierRepository(data), context.Sourcing, audits, audit, authorization);
		var production = new ProductionService(gate, new ProductionRepository(data), items, warehouses, movements, stockMovements, reversals, costs, replenishment, audits, audit, authorization);
		var customer = await new CustomerService(customers, audit, authorization).SaveAsync(new Customer
		{
			Name = $"Stock workflow provider {Guid.NewGuid():N}", IsActive = true, Currency = "EUR", PaymentTermsDays = 14,
			BillingAddress = "Provider Street 1", ShippingAddress = "Provider Street 1", BillingStreet = "Provider Street 1",
			BillingPostalCode = "53111", BillingCity = "Bonn", BillingCountryCode = "DE", TaxId = "DE123456789",
			BuyerReference = "PROVIDER", EInvoiceEndpoint = "provider@example.test", EInvoiceEndpointScheme = "EM"
		});
		var purposeId = await context.ScalarAsync("SELECT PurposeId FROM Inventories WHERE Id=$Id;", new DatabaseParameter("$Id", context.InventoryId));
		var locationId = await context.ScalarAsync("SELECT StorageLocationId FROM Inventories WHERE Id=$Id;", new DatabaseParameter("$Id", context.InventoryId));
		var warehouseId = await context.ScalarAsync("SELECT WarehouseId FROM StorageLocations WHERE Id=$Id;", new DatabaseParameter("$Id", locationId));
		var reason = (await reasons.ListActiveAsync(CancellationToken.None)).First();
		return new StockWorkflowProviderFixture(context, factory, gate, movements, service, production, salesOrders, costs,
			customer.Id, purposeId, locationId, warehouseId, reason.Id);
	}

	public async Task<ProviderStock> CreateStockAsync(int quantity, ItemTrackingMode mode = ItemTrackingMode.None,
		IReadOnlyList<TrackingAllocationInput>? tracking = null)
	{
		var itemId = await Data.InsertAsync("INSERT INTO Items (PartNumber,Description,IsActive,TrackingMode) VALUES ($Part,$Description,1,$Mode);",
			CancellationToken.None, new DatabaseParameter("$Part", $"WORKFLOW-{Guid.NewGuid():N}"),
			new DatabaseParameter("$Description", "Provider workflow stock"), new DatabaseParameter("$Mode", (int)mode));
		var inventoryId = await Data.InsertAsync("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($Item,$Purpose,$Location,1);",
			CancellationToken.None, new DatabaseParameter("$Item", itemId), new DatabaseParameter("$Purpose", _purposeId), new DatabaseParameter("$Location", _locationId));
		if (quantity > 0)
			await Movements.AddOpeningBalanceAsync(inventoryId, quantity, 5m, "Provider opening stock", tracking ?? [], CancellationToken.None);
		return new ProviderStock(itemId, inventoryId);
	}

	public async Task<ServiceOrder> CreateServiceOrderAsync(ProviderStock stock)
	{
		var serviceCase = await Service.SaveCaseAsync(new ServiceCase
		{
			CustomerId = _customerId, Subject = $"Provider parts {Guid.NewGuid():N}", Priority = ServiceCasePriority.Normal
		});
		return await Service.CreateServiceOrderAsync(serviceCase.Id, null, stock.ItemId, stock.InventoryId, null, null, null);
	}

	public async Task<SalesOrder> CreateApprovedSalesOrderAsync(ProviderStock stock, int quantity)
	{
		var order = await SalesOrders.SaveDraftAsync(new SalesOrder
		{
			CustomerId = _customerId,
			Lines = [new SalesOrderLine { ItemId = stock.ItemId, Quantity = quantity, UnitPrice = 15m, TaxRate = 19m }]
		});
		order = await SalesOrders.SubmitAsync(order.Id, order.Version);
		return await SalesOrders.ApproveAsync(order.Id, order.Version);
	}

	public async Task<ProviderProductionPlan> CreateReleasedProductionAsync(ProviderStock component, int quantity)
	{
		var finished = await CreateStockAsync(0);
		await Costs.SaveProfileAsync(new ItemCostProfile
		{
			ItemId = component.ItemId, BaseCostSource = ItemCostBaseSource.ManualStandard, Currency = "EUR", ManualStandardCost = 5m
		});
		var bom = await Production.SaveBomAsync(new BillOfMaterial
		{
			FinishedItemId = finished.ItemId, Revision = "A",
			Lines = [new BillOfMaterialLine { ComponentItemId = component.ItemId, Quantity = quantity, Sequence = 10 }]
		});
		bom = await Production.ActivateBomAsync(bom.Id, bom.Version);
		var order = await Production.CreateOrderAsync(finished.ItemId, bom.Id, 1, WarehouseId, finished.InventoryId);
		order = await Production.ReleaseOrderAsync(order.Id, order.Version);
		Assert.Equal(quantity, Assert.Single(order.Requirements).RequiredQuantity);
		return new ProviderProductionPlan(component, finished, order);
	}

	public Task<long> StockAsync(ProviderStock stock) => ScalarAsync(
		"SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$Id;", new DatabaseParameter("$Id", stock.InventoryId));

	public Task<long> ReservationsAsync(ProviderStock stock) => ScalarAsync(
		"SELECT COALESCE(SUM(Quantity),0) FROM InventoryReservations WHERE InventoryId=$Id AND Status=1;", new DatabaseParameter("$Id", stock.InventoryId));

	public async Task<ProviderStockSnapshot> SnapshotAsync(ProviderStock stock) => new(
		await StockAsync(stock),
		await ReservationsAsync(stock),
		await ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE InventoryId=$Id;", new DatabaseParameter("$Id", stock.InventoryId)),
		await ScalarAsync("SELECT COUNT(*) FROM StockMovementTracking t INNER JOIN StockMovements m ON m.Id=t.StockMovementId WHERE m.InventoryId=$Id;", new DatabaseParameter("$Id", stock.InventoryId)),
		await ScalarAsync("SELECT COUNT(*) FROM ItemTrackingUnits WHERE ItemId=$Id;", new DatabaseParameter("$Id", stock.ItemId)),
		await ScalarAsync("SELECT COALESCE(SUM(Version),0) FROM ItemTrackingUnits WHERE ItemId=$Id;", new DatabaseParameter("$Id", stock.ItemId)),
		await ScalarAsync("SELECT COUNT(*) FROM AuditEntries;"),
		await ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;"));

	public async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await Data.ExecuteScalarAsync(sql, CancellationToken.None, parameters), CultureInfo.InvariantCulture);

	public ValueTask DisposeAsync() => _context.DisposeAsync();
}

internal sealed record ProviderStock(long ItemId, long InventoryId);
internal sealed record ProviderProductionPlan(ProviderStock Component, ProviderStock Finished, ProductionOrder Order);
internal sealed record ProviderStockSnapshot(long Stock, long Reserved, long Movements, long TrackingRows,
	long TrackingUnits, long TrackingVersions, long Audits, long Journals);
