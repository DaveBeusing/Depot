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
		Inventories = new InventoryRepository(dataAccess);
		Warehouses = new WarehouseRepository(dataAccess);
		StorageLocations = new StorageLocationRepository(dataAccess);
		StockMovements = new StockMovementRepository(dataAccess);
		Users = new UserRepository(dataAccess);
		Audit = new AuditRepository(dataAccess);
	}

	public ItemRepository Items { get; }
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
	public InventoryRepository Inventories { get; }
	public WarehouseRepository Warehouses { get; }
	public StorageLocationRepository StorageLocations { get; }
	public StockMovementRepository StockMovements { get; }
	public UserRepository Users { get; }
	public AuditRepository Audit { get; }
}
