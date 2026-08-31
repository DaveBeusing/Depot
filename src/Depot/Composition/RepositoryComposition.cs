// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Repositories;

namespace Depot.Composition;

internal sealed class RepositoryComposition
{
	public RepositoryComposition(DatabaseAccess dataAccess)
	{
		Items = new ItemRepository(dataAccess);
		ItemCosts = new ItemCostRepository(dataAccess);
		ItemTraceability = new ItemTraceabilityRepository(dataAccess);
		Purposes = new PurposeRepository(dataAccess);
		ReasonCodes = new ReasonCodeRepository(dataAccess);
		Manufacturers = new ManufacturerRepository(dataAccess);
		Categories = new CategoryRepository(dataAccess);
		UnitsOfMeasure = new UnitOfMeasureRepository(dataAccess);
		Packagings = new PackagingRepository(dataAccess);
		SupplierCategories = new SupplierCategoryRepository(dataAccess);
		Suppliers = new SupplierRepository(dataAccess);
		SupplierItems = new SupplierItemRepository(dataAccess);
		PurchaseOrders = new PurchaseOrderRepository(dataAccess);
		GoodsReceipts = new GoodsReceiptRepository(dataAccess);
		StockTransfers = new StockTransferRepository(dataAccess);
		InventoryCounts = new InventoryCountRepository(dataAccess);
		MaterialIssues = new MaterialIssueRepository(dataAccess);
		MaterialReturns = new MaterialReturnRepository(dataAccess);
		SupplierReturns = new SupplierReturnRepository(dataAccess);
		Customers = new CustomerRepository(dataAccess);
		SalesOrders = new SalesOrderRepository(dataAccess);
		SalesTimeline = new SalesTimelineRepository(dataAccess);
		SalesPriceLists = new SalesPriceListRepository(dataAccess);
		SalesQuotes = new SalesQuoteRepository(dataAccess);
		InventoryReservations = new InventoryReservationRepository(dataAccess);
		Shipments = new ShipmentRepository(dataAccess);
		SalesInvoices = new SalesInvoiceRepository(dataAccess);
		CustomerReturns = new CustomerReturnRepository(dataAccess);
		SalesCreditNotes = new SalesCreditNoteRepository(dataAccess);
		FinanceGeneralLedger = new FinanceGeneralLedgerRepository(dataAccess);
		FinancePostingProfiles = new FinancePostingProfileRepository(dataAccess);
		FinanceAccountsReceivable = new FinanceAccountsReceivableRepository(dataAccess);
		FinanceAccountsPayable = new FinanceAccountsPayableRepository(dataAccess);
		FinanceInventoryAccounting = new FinanceInventoryAccountingRepository(dataAccess);
		FinanceInventoryCosting = new FinanceInventoryCostingRepository(dataAccess);
		FinanceBanking = new FinanceBankingRepository(dataAccess);
		FinanceFinancialReporting = new FinanceFinancialReportingRepository(dataAccess);
		FinanceFinancialReportingInventory = new FinanceFinancialReportingInventoryRepository(dataAccess);
		FinanceLocalization = new FinanceLocalizationRepository(dataAccess);
		Inventories = new InventoryRepository(dataAccess);
		Warehouses = new WarehouseRepository(dataAccess);
		StorageLocations = new StorageLocationRepository(dataAccess);
		StockMovements = new StockMovementRepository(dataAccess);
		Users = new UserRepository(dataAccess);
		UserSessions = new UserSessionRepository(dataAccess);
		Roles = new RoleRepository(dataAccess);
		Audit = new AuditRepository(dataAccess);
		Notifications = new NotificationRepository(dataAccess);
		Dashboard = new DashboardRepository(dataAccess);
	}

	public ItemRepository Items { get; }
	public ItemCostRepository ItemCosts { get; }
	public ItemTraceabilityRepository ItemTraceability { get; }
	public PurposeRepository Purposes { get; }
	public ReasonCodeRepository ReasonCodes { get; }
	public ManufacturerRepository Manufacturers { get; }
	public CategoryRepository Categories { get; }
	public UnitOfMeasureRepository UnitsOfMeasure { get; }
	public PackagingRepository Packagings { get; }
	public SupplierCategoryRepository SupplierCategories { get; }
	public SupplierRepository Suppliers { get; }
	public SupplierItemRepository SupplierItems { get; }
	public PurchaseOrderRepository PurchaseOrders { get; }
	public GoodsReceiptRepository GoodsReceipts { get; }
	public StockTransferRepository StockTransfers { get; }
	public InventoryCountRepository InventoryCounts { get; }
	public MaterialIssueRepository MaterialIssues { get; }
	public MaterialReturnRepository MaterialReturns { get; }
	public SupplierReturnRepository SupplierReturns { get; }
	public CustomerRepository Customers { get; }
	public SalesOrderRepository SalesOrders { get; }
	public SalesTimelineRepository SalesTimeline { get; }
	public SalesPriceListRepository SalesPriceLists { get; }
	public SalesQuoteRepository SalesQuotes { get; }
	public InventoryReservationRepository InventoryReservations { get; }
	public ShipmentRepository Shipments { get; }
	public SalesInvoiceRepository SalesInvoices { get; }
	public CustomerReturnRepository CustomerReturns { get; }
	public SalesCreditNoteRepository SalesCreditNotes { get; }
	public FinanceGeneralLedgerRepository FinanceGeneralLedger { get; }
	public FinancePostingProfileRepository FinancePostingProfiles { get; }
	public FinanceAccountsReceivableRepository FinanceAccountsReceivable { get; }
	public FinanceAccountsPayableRepository FinanceAccountsPayable { get; }
	public FinanceInventoryAccountingRepository FinanceInventoryAccounting { get; }
	public FinanceInventoryCostingRepository FinanceInventoryCosting { get; }
	public FinanceBankingRepository FinanceBanking { get; }
	public FinanceFinancialReportingRepository FinanceFinancialReporting { get; }
	public FinanceFinancialReportingInventoryRepository FinanceFinancialReportingInventory { get; }
	public FinanceLocalizationRepository FinanceLocalization { get; }
	public InventoryRepository Inventories { get; }
	public WarehouseRepository Warehouses { get; }
	public StorageLocationRepository StorageLocations { get; }
	public StockMovementRepository StockMovements { get; }
	public UserRepository Users { get; }
	public UserSessionRepository UserSessions { get; }
	public RoleRepository Roles { get; }
	public AuditRepository Audit { get; }
	public NotificationRepository Notifications { get; }
	public DashboardRepository Dashboard { get; }
}
