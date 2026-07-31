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
		var audit = new AuditService(repositories.Audit, Authorization);
		var passwordHasher = new PasswordHasher();

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
			audit);
		GoodsReceipts = new GoodsReceiptService(
			database.TransactionRunner,
			repositories.GoodsReceipts,
			repositories.PurchaseOrders,
			repositories.Inventories,
			repositories.StockMovements,
			repositories.ReasonCodes,
			repositories.Audit,
			audit);
		StockTransfers = new StockTransferService(
			database.TransactionRunner,
			repositories.StockTransfers,
			repositories.Inventories,
			repositories.Audit,
			audit);
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
			repositories.Purposes,
			repositories.StorageLocations,
			repositories.Warehouses,
			repositories.ReasonCodes,
			repositories.StockMovements,
			audit);
		Stock = new StockService(
			repositories.Items,
			repositories.Inventories,
			repositories.Purposes,
			repositories.StorageLocations,
			repositories.Warehouses,
			repositories.StockMovements);
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
	public GoodsReceiptService GoodsReceipts { get; }
	public StockTransferService StockTransfers { get; }
	public WarehouseService Warehouses { get; }
	public StorageLocationService StorageLocations { get; }
	public UserService Users { get; }
	public MovementService Movements { get; }
	public StockService Stock { get; }
	public ReportService Reports { get; }
	public ImportService Import { get; }
}
