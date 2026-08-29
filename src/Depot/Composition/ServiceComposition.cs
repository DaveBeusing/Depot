// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;
using Depot.Services.Import;
using Depot.Services.Help;

namespace Depot.Composition;

internal sealed class ServiceComposition
{
	public ServiceComposition(DatabaseComposition database, RepositoryComposition repositories)
	{
		Authorization = new AuthorizationService();
		NotificationNavigation = new NotificationNavigationService(Authorization);
		Notifications = new NotificationService(database.TransactionRunner, repositories.Notifications, Authorization);
		database.ConfigureNotifications(Notifications);
		HelpContent = new EmbeddedHelpContentProvider(typeof(ServiceComposition).Assembly);
		Help = new HelpService(HelpContent, Authorization, new HelpSearchService());
		HelpRenderer = new HelpMarkdownRenderer();
		AuditLog = new AuditLogService(repositories.Audit, Authorization, new AuditJsonSanitizer());
		DataSubjectAccess = new DataSubjectAccessService(database.DataAccess, Authorization);
		var audit = new AuditService(repositories.Audit, Authorization);
		FinanceGeneralLedger = new FinanceGeneralLedgerService(database.TransactionRunner, repositories.FinanceGeneralLedger, repositories.FinancePostingProfiles, repositories.Audit, audit, Authorization);
		AccountsReceivable = new FinanceAccountsReceivableService(database.TransactionRunner, repositories.FinanceAccountsReceivable, FinanceGeneralLedger, repositories.Audit, audit, Authorization);
		InventoryAccounting = new FinanceInventoryAccountingService(database.TransactionRunner, repositories.FinanceInventoryAccounting, FinanceGeneralLedger, repositories.Audit, audit, Authorization);
		InventoryCosting = new FinanceInventoryCostingService(database.TransactionRunner, repositories.FinanceInventoryAccounting, repositories.FinanceInventoryCosting, FinanceGeneralLedger, repositories.Audit, audit, Authorization, repositories.FinanceAccountsPayable);
		InventoryMovementAccounting = new FinanceInventoryMovementAccountingService(database.TransactionRunner, repositories.FinanceInventoryCosting, repositories.Inventories, InventoryCosting, Authorization);
		AccountsPayable = new FinanceAccountsPayableService(database.TransactionRunner, repositories.FinanceAccountsPayable, FinanceGeneralLedger, repositories.Audit, audit, Authorization);
		Banking = new FinanceBankingService(database.TransactionRunner, repositories.FinanceBanking, AccountsPayable, repositories.Audit, audit, Authorization);
		FinancialReporting = new FinanceFinancialReportingService(database.TransactionRunner, repositories.FinanceFinancialReporting, repositories.FinanceFinancialReportingInventory, AccountsReceivable, AccountsPayable, repositories.Audit, audit, Authorization);
		Localization = new FinanceLocalizationService(database.TransactionRunner, repositories.FinanceLocalization, repositories.Audit, audit, Authorization);
		var passwordHasher = new PasswordHasher();
		ItemTraceability = new ItemTraceabilityService(repositories.ItemTraceability, audit);
		var movementReversals = new StockMovementReversalService(database.TransactionRunner, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Audit, audit, ItemTraceability);
		Authentication = new AuthenticationService(repositories.Users, repositories.Roles, passwordHasher, Authorization);
		Session = new SessionService(Authorization);
		Manufacturers = new ManufacturerService(repositories.Manufacturers, audit);
		Categories = new CategoryService(repositories.Categories, audit);
		UnitsOfMeasure = new UnitOfMeasureService(repositories.UnitsOfMeasure, audit);
		Packagings = new PackagingService(repositories.Packagings, audit);
		SupplierCategories = new SupplierCategoryService(repositories.SupplierCategories, audit);
		Suppliers = new SupplierService(repositories.Suppliers, repositories.SupplierItems, repositories.SupplierCategories, audit);
		SupplierItems = new SupplierItemService(repositories.SupplierItems, repositories.Suppliers, repositories.Items, audit);
		PurchaseOrders = new PurchaseOrderService(repositories.PurchaseOrders, repositories.Suppliers, repositories.Items, audit, Authorization, Notifications);
		PurchaseOrderApprovals = new PurchaseOrderApprovalService(repositories.PurchaseOrders, repositories.Audit, PurchaseOrders, Authorization, new AuditJsonSanitizer());
		PurchaseOrderHistory = new PurchaseOrderHistoryService(repositories.Audit, Authorization, new AuditJsonSanitizer());
		GoodsReceipts = new GoodsReceiptService(database.TransactionRunner, repositories.GoodsReceipts, repositories.PurchaseOrders, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Audit, audit, movementReversals, ItemTraceability, InventoryAccounting);
		StockTransfers = new StockTransferService(database.TransactionRunner, repositories.StockTransfers, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Audit, audit, movementReversals, ItemTraceability);
		InventoryCounts = new InventoryCountService(database.TransactionRunner, repositories.InventoryCounts, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Warehouses, repositories.Audit, audit, movementReversals, Notifications, ItemTraceability);
		MaterialIssues = new MaterialIssueService(database.TransactionRunner, repositories.MaterialIssues, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Audit, audit, movementReversals, Authorization, ItemTraceability);
		MaterialReturns = new MaterialReturnService(database.TransactionRunner, repositories.MaterialReturns, repositories.MaterialIssues, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Audit, audit, movementReversals, Authorization, ItemTraceability);
		SupplierReturns = new SupplierReturnService(database.TransactionRunner, repositories.SupplierReturns, repositories.PurchaseOrders, repositories.GoodsReceipts, repositories.Inventories, repositories.StockMovements, repositories.ReasonCodes, repositories.Audit, audit, movementReversals, Authorization, ItemTraceability);
		Customers = new CustomerService(repositories.Customers, audit, Authorization);
		SalesPricing = new SalesPricingService(repositories.SalesPriceLists, audit, Authorization);
		SalesTimeline = new SalesTimelineService(repositories.SalesTimeline, Authorization);
		SalesOrders = new SalesOrderService(database.TransactionRunner, repositories.SalesOrders, repositories.Customers, repositories.Items, repositories.Inventories, repositories.InventoryReservations, repositories.StockMovements, repositories.Audit, audit, Authorization, Notifications, ItemTraceability);
		SalesQuotes = new SalesQuoteService(repositories.SalesQuotes, repositories.Customers, SalesOrders, audit, Authorization);
		CustomerReturns = new CustomerReturnService(database.TransactionRunner, repositories.CustomerReturns, repositories.Shipments, repositories.StockMovements, repositories.Audit, audit, Authorization, Notifications, ItemTraceability);
		SalesCreditNotes = new SalesCreditNoteService(database.TransactionRunner, repositories.SalesCreditNotes, repositories.SalesInvoices, repositories.Audit, audit, Authorization, Notifications, AccountsReceivable);
		Shipments = new ShipmentService(database.TransactionRunner, repositories.Shipments, repositories.SalesOrders, repositories.InventoryReservations, repositories.Inventories, repositories.StockMovements, repositories.SalesInvoices, CustomerReturns, repositories.Audit, audit, Authorization, Notifications, ItemTraceability, InventoryAccounting);
		ShipmentPacking = new ShipmentPackingService(database.TransactionRunner, repositories.Shipments, repositories.Audit, audit, Authorization);
		SalesInvoices = new SalesInvoiceService(database.TransactionRunner, repositories.SalesInvoices, repositories.Shipments, repositories.SalesOrders, repositories.Customers, repositories.Audit, audit, Authorization, Notifications, SalesCreditNotes, AccountsReceivable);
		CompanyDocumentIdentity = new CompanyDocumentIdentityService(database.DataAccess, database.Settings.CurrentSettings.Provider);
		DocumentIssuerSnapshots = new DocumentIssuerSnapshotService(database.DataAccess);
		SalesInvoiceFinalizations = new SalesInvoiceFinalizationService(database.DataAccess);
		SalesDocuments = new SalesDocumentService(CompanyDocumentIdentity, DocumentIssuerSnapshots, SalesInvoiceFinalizations);
		SalesEmail = new SalesDocumentEmailService();
		Items = new ItemService(repositories.Items, audit, Manufacturers, Categories, UnitsOfMeasure, Packagings, repositories.SupplierItems);
		Purposes = new PurposeService(repositories.Purposes, audit);
		ReasonCodes = new ReasonCodeService(repositories.ReasonCodes, audit);
		Warehouses = new WarehouseService(repositories.Warehouses, repositories.StorageLocations, audit);
		StorageLocations = new StorageLocationService(repositories.StorageLocations, repositories.Warehouses, audit);
		Roles = new RoleService(database.TransactionRunner, repositories.Roles, repositories.Audit, audit, Authorization);
		Users = new UserService(database.TransactionRunner, repositories.Users, repositories.Roles, repositories.Audit, passwordHasher, Authorization, audit);
		Movements = new MovementService(repositories.Items, repositories.Inventories, repositories.ReasonCodes, repositories.StockMovements, audit, movementReversals, database.TransactionRunner, repositories.Audit, ItemTraceability);
		Stock = new StockService(repositories.Inventories, repositories.StockMovements, ItemTraceability);
		Dashboard = new DashboardService(Stock, repositories.Dashboard, Authorization);
		Reports = new ReportService(Stock, Authorization);
		var inventoryManagement = new InventoryManagementService(repositories.Inventories, audit);
		Import = new ImportService(repositories.Items, Items, Purposes, Warehouses, StorageLocations, inventoryManagement, Movements, Authorization);
		Sales = new SalesServices(Customers, SalesPricing, SalesTimeline, SalesOrders, SalesQuotes, Shipments, ShipmentPacking, SalesInvoices, CustomerReturns, SalesCreditNotes, Items, Authorization, SalesDocuments, SalesEmail, SalesInvoiceFinalizations);
	}

	public AuthorizationService Authorization { get; }
	public NotificationService Notifications { get; }
	public NotificationNavigationService NotificationNavigation { get; }
	public IHelpContentProvider HelpContent { get; }
	public IHelpService Help { get; }
	public HelpMarkdownRenderer HelpRenderer { get; }
	public AuditLogService AuditLog { get; }
	public DataSubjectAccessService DataSubjectAccess { get; }
	public FinanceGeneralLedgerService FinanceGeneralLedger { get; }
	public FinanceAccountsReceivableService AccountsReceivable { get; }
	public FinanceAccountsPayableService AccountsPayable { get; }
	public FinanceInventoryAccountingService InventoryAccounting { get; }
	public FinanceInventoryCostingService InventoryCosting { get; }
	public FinanceInventoryMovementAccountingService InventoryMovementAccounting { get; }
	public FinanceBankingService Banking { get; }
	public FinanceFinancialReportingService FinancialReporting { get; }
	public FinanceLocalizationService Localization { get; }
	public AuthenticationService Authentication { get; }
	public SessionService Session { get; }
	public ItemService Items { get; }
	public ItemTraceabilityService ItemTraceability { get; }
	public PurposeService Purposes { get; }
	public ReasonCodeService ReasonCodes { get; }
	public ManufacturerService Manufacturers { get; }
	public CategoryService Categories { get; }
	public UnitOfMeasureService UnitsOfMeasure { get; }
	public PackagingService Packagings { get; }
	public SupplierCategoryService SupplierCategories { get; }
	public SupplierService Suppliers { get; }
	public SupplierItemService SupplierItems { get; }
	public PurchaseOrderService PurchaseOrders { get; }
	public PurchaseOrderApprovalService PurchaseOrderApprovals { get; }
	public PurchaseOrderHistoryService PurchaseOrderHistory { get; }
	public GoodsReceiptService GoodsReceipts { get; }
	public StockTransferService StockTransfers { get; }
	public InventoryCountService InventoryCounts { get; }
	public MaterialIssueService MaterialIssues { get; }
	public MaterialReturnService MaterialReturns { get; }
	public SupplierReturnService SupplierReturns { get; }
	public CustomerService Customers { get; }
	public SalesPricingService SalesPricing { get; }
	public SalesTimelineService SalesTimeline { get; }
	public SalesOrderService SalesOrders { get; }
	public SalesQuoteService SalesQuotes { get; }
	public ShipmentService Shipments { get; }
	public ShipmentPackingService ShipmentPacking { get; }
	public SalesInvoiceService SalesInvoices { get; }
	public CustomerReturnService CustomerReturns { get; }
	public SalesCreditNoteService SalesCreditNotes { get; }
	public CompanyDocumentIdentityService CompanyDocumentIdentity { get; }
	public DocumentIssuerSnapshotService DocumentIssuerSnapshots { get; }
	public SalesInvoiceFinalizationService SalesInvoiceFinalizations { get; }
	public SalesDocumentService SalesDocuments { get; }
	public SalesDocumentEmailService SalesEmail { get; }
	public SalesServices Sales { get; }
	public WarehouseService Warehouses { get; }
	public StorageLocationService StorageLocations { get; }
	public UserService Users { get; }
	public RoleService Roles { get; }
	public MovementService Movements { get; }
	public StockService Stock { get; }
	public DashboardService Dashboard { get; }
	public ReportService Reports { get; }
	public ImportService Import { get; }
}
