// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;
using Depot.Services.Import;

namespace Depot.Composition;

internal sealed class ServiceComposition
{
	public ServiceComposition(DatabaseComposition database, RepositoryComposition repositories)
	{
		Authorization = new AuthorizationService();
		AuditLog = new AuditLogService(repositories.Audit, Authorization, new AuditJsonSanitizer());
		var audit = new AuditService(repositories.Audit, Authorization);
		var passwordHasher = new PasswordHasher();
		var movementReversals = new StockMovementReversalService(
			database.TransactionRunner,
			repositories.Inventories,
			repositories.StockMovements,
			repositories.ReasonCodes,
			repositories.Audit,
			audit);

		Authentication = new AuthenticationService(repositories.Users, passwordHasher, Authorization);
		Session = new SessionService(Authorization);
		Manufacturers = new ManufacturerService(repositories.Manufacturers, audit);
		Categories = new CategoryService(repositories.Categories, audit);
		UnitsOfMeasure = new UnitOfMeasureService(repositories.UnitsOfMeasure, audit);
		Packagings = new PackagingService(repositories.Packagings, audit);
		SupplierCategories = new SupplierCategoryService(repositories.SupplierCategories, audit);
		Suppliers = new SupplierService(
			repositories.Suppliers,
			repositories.SupplierItems,
			repositories.SupplierCategories,
			audit);
		SupplierItems = new SupplierItemService(
			repositories.SupplierItems,
			repositories.Suppliers,
			repositories.Items,
			audit);
		PurchaseOrders = new PurchaseOrderService(
			repositories.PurchaseOrders,
			repositories.Suppliers,
			repositories.Items,
			audit,
			Authorization);
		PurchaseOrderApprovals = new PurchaseOrderApprovalService(
			repositories.PurchaseOrders,
			repositories.Audit,
			PurchaseOrders,
			Authorization,
			new AuditJsonSanitizer());
		GoodsReceipts = new GoodsReceiptService(
			database.TransactionRunner,
			repositories.GoodsReceipts,
			repositories.PurchaseOrders,
			repositories.Inventories,
			repositories.StockMovements,
			repositories.ReasonCodes,
			repositories.Audit,
			audit,
			movementReversals);
		StockTransfers = new StockTransferService(
			database.TransactionRunner,
			repositories.StockTransfers,
			repositories.Inventories,
			repositories.StockMovements,
			repositories.ReasonCodes,
			repositories.Audit,
			audit,
			movementReversals);
		InventoryCounts = new InventoryCountService(
			database.TransactionRunner,
			repositories.InventoryCounts,
			repositories.Inventories,
			repositories.StockMovements,
			repositories.ReasonCodes,
			repositories.Warehouses,
			repositories.Audit,
			audit,
			movementReversals);
		MaterialIssues = new MaterialIssueService(
			database.TransactionRunner,
			repositories.MaterialIssues,
			repositories.Inventories,
			repositories.StockMovements,
			repositories.ReasonCodes,
			repositories.Audit,
			audit,
			movementReversals);
		Items = new ItemService(
			repositories.Items,
			audit,
			Manufacturers,
			Categories,
			UnitsOfMeasure,
			Packagings,
			repositories.SupplierItems);
		Purposes = new PurposeService(repositories.Purposes, audit);
		ReasonCodes = new ReasonCodeService(repositories.ReasonCodes, audit);
		Warehouses = new WarehouseService(
			repositories.Warehouses,
			repositories.StorageLocations,
			audit);
		StorageLocations = new StorageLocationService(
			repositories.StorageLocations,
			repositories.Warehouses,
			audit);
		Users = new UserService(repositories.Users, passwordHasher, Authorization, audit);
		Movements = new MovementService(
			repositories.Items,
			repositories.Inventories,
			repositories.ReasonCodes,
			repositories.StockMovements,
			audit,
			movementReversals);
		Stock = new StockService(repositories.Inventories, repositories.StockMovements);
		Reports = new ReportService(Stock);
		var inventoryManagement = new InventoryManagementService(repositories.Inventories, audit);
		Import = new ImportService(
			repositories.Items,
			Items,
			Purposes,
			Warehouses,
			StorageLocations,
			inventoryManagement,
			Movements);
	}

	public AuthorizationService Authorization { get; }
	public AuditLogService AuditLog { get; }
	public AuthenticationService Authentication { get; }
	public SessionService Session { get; }
	public ItemService Items { get; }
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
	public GoodsReceiptService GoodsReceipts { get; }
	public StockTransferService StockTransfers { get; }
	public InventoryCountService InventoryCounts { get; }
	public MaterialIssueService MaterialIssues { get; }
	public WarehouseService Warehouses { get; }
	public StorageLocationService StorageLocations { get; }
	public UserService Users { get; }
	public MovementService Movements { get; }
	public StockService Stock { get; }
	public ReportService Reports { get; }
	public ImportService Import { get; }
}
