// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

namespace Depot.Tests;

internal sealed class ProcurementTestContext : IAsyncDisposable
{
	private readonly string? _databasePath;
	private readonly bool _cleanDatabaseRows;
	private PurchaseOrderService? _orders;
	private PurchaseOrderApprovalService? _approvals;
	private GoodsReceiptService? _receipts;
	private AuthorizationService? _authorization;
	private MaterialIssueService? _materialIssues;
	private MaterialReturnService? _materialReturns;
	private User? _administrator;
	private User? _approver;

	private ProcurementTestContext(
		IDatabaseConnectionFactory connectionFactory,
		string? databasePath,
		bool cleanDatabaseRows)
	{
		ConnectionFactory = connectionFactory;
		Data = new DatabaseAccess(connectionFactory);
		_databasePath = databasePath;
		_cleanDatabaseRows = cleanDatabaseRows;
	}

	public IDatabaseConnectionFactory ConnectionFactory { get; }
	public DatabaseAccess Data { get; }
	public PurchaseOrderService Orders => _orders ?? throw new InvalidOperationException("The purchase order service was not initialized.");
	public PurchaseOrderApprovalService Approvals => _approvals ?? throw new InvalidOperationException("The purchase order approval service was not initialized.");
	public GoodsReceiptService Receipts => _receipts ?? throw new InvalidOperationException("The goods receipt service was not initialized.");
	public AuthorizationService Authorization => _authorization ?? throw new InvalidOperationException("Authorization was not initialized.");
	public MaterialIssueService MaterialIssues => _materialIssues ?? throw new InvalidOperationException("The material issue service was not initialized.");
	public MaterialReturnService MaterialReturns => _materialReturns ?? throw new InvalidOperationException("The material return service was not initialized.");
	public long SupplierId { get; private set; }
	public long InactiveSupplierId { get; private set; }
	public long ItemId { get; private set; }
	public long SecondItemId { get; private set; }
	public long InactiveItemId { get; private set; }
	public long InventoryId { get; private set; }
	public long SecondInventoryId { get; private set; }
	public long InactiveInventoryId { get; private set; }
	public long TestStorageLocationId { get; private set; }
	public long ApproverUserId => _approver?.Id ?? throw new InvalidOperationException("The test approver was not initialized.");

	public static async Task<ProcurementTestContext> CreateSqliteAsync()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-procurement-{Guid.NewGuid():N}.db");
		var factory = new SqliteConnectionFactory(path);
		new DepotDatabase(factory).Initialize();
		return await CreateAsync(factory, path, false);
	}

	public static async Task<ProcurementTestContext> CreateServerAsync(
		IDatabaseConnectionFactory connectionFactory,
		IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		return await CreateAsync(connectionFactory, null, true);
	}

	public PurchaseOrder NewOrder(
		int quantity = 10,
		decimal unitPrice = 12.50m,
		long? supplierId = null,
		long? itemId = null) =>
		new()
		{
			SupplierId = supplierId ?? SupplierId,
			OrderDate = DateTime.Today,
			ExpectedDeliveryDate = DateTime.Today.AddDays(7),
			Lines =
			[
				new PurchaseOrderLine
				{
					ItemId = itemId ?? ItemId,
					Quantity = quantity,
					UnitPrice = unitPrice
				}
			]
		};

	public GoodsReceipt NewReceipt(
		PurchaseOrder order,
		int quantity,
		long? inventoryId = null,
		long? purchaseOrderLineId = null) =>
		new()
		{
			PurchaseOrderId = order.Id,
			SupplierDeliveryNoteNumber = $"DN-{Guid.NewGuid():N}",
			ReceiptDate = DateTime.Today,
			Notes = "Procurement integration test receipt",
			Lines =
			[
				new GoodsReceiptLine
				{
					PurchaseOrderLineId = purchaseOrderLineId ?? order.Lines[0].Id,
					InventoryId = inventoryId ?? InventoryId,
					Quantity = quantity
				}
			]
		};

	public async Task<PurchaseOrder> ApproveAndOrderAsync(PurchaseOrder order)
	{
		var submitted = await Orders.SubmitForApprovalAsync(order.Id, order.Version);
		SignInApprover();
		try
		{
			var approved = await Orders.ApproveAsync(submitted.Id, submitted.Version, "Approved by integration test");
			SignInAdministrator();
			return await Orders.MarkOrderedAsync(approved.Id, approved.Version);
		}
		finally
		{
			SignInAdministrator();
		}
	}

	public void SignInApprover() => Authorization.SignIn(
		_approver ?? throw new InvalidOperationException("The test approver was not initialized."));

	public void SignInAdministrator() => Authorization.SignIn(
		_administrator ?? throw new InvalidOperationException("The test administrator was not initialized."));

	public async Task<long> ScalarAsync(
		string sql,
		params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await Data.ExecuteScalarAsync(sql, CancellationToken.None, parameters));

	public async ValueTask DisposeAsync()
	{
		try
		{
			if (_cleanDatabaseRows) await RemoveTestRowsAsync();
		}
		finally
		{
			if (_databasePath is not null)
			{
				SqliteConnection.ClearAllPools();
				if (File.Exists(_databasePath)) File.Delete(_databasePath);
			}
		}
	}

	private static async Task<ProcurementTestContext> CreateAsync(
		IDatabaseConnectionFactory connectionFactory,
		string? databasePath,
		bool cleanDatabaseRows)
	{
		var context = new ProcurementTestContext(connectionFactory, databasePath, cleanDatabaseRows);
		var suffix = Guid.NewGuid().ToString("N");
		var accountNumber = DateTime.UtcNow.Ticks;
		context.SupplierId = await context.Data.InsertAsync(
			"INSERT INTO Suppliers (SupplierNumber, AccountNumber, Name, IsActive) VALUES ($Number, $AccountNumber, $Name, 1);",
			CancellationToken.None,
			new DatabaseParameter("$Number", $"TEST-SUP-{suffix}"),
			new DatabaseParameter("$AccountNumber", accountNumber),
			new DatabaseParameter("$Name", $"Test Supplier {suffix}"));
		context.InactiveSupplierId = await context.Data.InsertAsync(
			"INSERT INTO Suppliers (SupplierNumber, AccountNumber, Name, IsActive) VALUES ($Number, $AccountNumber, $Name, 0);",
			CancellationToken.None,
			new DatabaseParameter("$Number", $"TEST-INACTIVE-SUP-{suffix}"),
			new DatabaseParameter("$AccountNumber", accountNumber + 1),
			new DatabaseParameter("$Name", $"Inactive Test Supplier {suffix}"));
		context.ItemId = await InsertItemAsync(context.Data, $"TEST-ITEM-{suffix}", true);
		context.SecondItemId = await InsertItemAsync(context.Data, $"TEST-ITEM-2-{suffix}", true);
		context.InactiveItemId = await InsertItemAsync(context.Data, $"TEST-INACTIVE-ITEM-{suffix}", false);
		var purposeId = Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT MIN(Id) FROM Purposes WHERE Name = 'Stock';",
			CancellationToken.None));
		var locationId = Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT MIN(Id) FROM StorageLocations;",
			CancellationToken.None));
		var warehouseId = Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT MIN(Id) FROM Warehouses;",
			CancellationToken.None));
		context.TestStorageLocationId = await context.Data.InsertAsync(
			"INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive) VALUES ($WarehouseId, $Name, $Description, 1);",
			CancellationToken.None,
			new DatabaseParameter("$WarehouseId", warehouseId),
			new DatabaseParameter("$Name", $"TEST-LOCATION-{suffix}"),
			new DatabaseParameter("$Description", "Procurement integration test location"));
		context.InventoryId = await InsertInventoryAsync(context.Data, context.ItemId, purposeId, locationId, true);
		context.SecondInventoryId = await InsertInventoryAsync(context.Data, context.SecondItemId, purposeId, locationId, true);
		context.InactiveInventoryId = await InsertInventoryAsync(context.Data, context.ItemId, purposeId, context.TestStorageLocationId, false);

		var authorization = new AuthorizationService();
		var administrator = await new UserRepository(context.Data).GetByEmailAsync("admin@depot.local", CancellationToken.None)
			?? throw new InvalidOperationException("The default administrator was not initialized.");
		authorization.SignIn(administrator);
		context._administrator = administrator;
		var approverId = await context.Data.InsertAsync(
			"INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, IsActive, CreatedUtc) VALUES ($Email, $DisplayName, $PasswordHash, 0, 1, 1, $CreatedUtc);",
			CancellationToken.None,
			new DatabaseParameter("$Email", $"approver-{suffix}@depot.test"),
			new DatabaseParameter("$DisplayName", "Procurement Approver"),
			new DatabaseParameter("$PasswordHash", "test-only-not-used"),
			new DatabaseParameter("$CreatedUtc", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
		context._approver = await new UserRepository(context.Data).GetByIdAsync(approverId, CancellationToken.None)
			?? throw new InvalidOperationException("The procurement approver was not initialized.");
		context._authorization = authorization;
		var audit = new AuditService(new AuditRepository(context.Data), authorization);
		context._orders = new PurchaseOrderService(
			new PurchaseOrderRepository(context.Data),
			new SupplierRepository(context.Data),
			new ItemRepository(context.Data),
			audit,
			authorization);
		context._approvals = new PurchaseOrderApprovalService(
			new PurchaseOrderRepository(context.Data),
			new AuditRepository(context.Data),
			context._orders,
			authorization,
			new AuditJsonSanitizer());
		var issueMovements = new StockMovementRepository(context.Data);
		var issueReversals = new StockMovementReversalService(
			new DatabaseTransactionRunner(context.Data),
			new InventoryRepository(context.Data),
			issueMovements,
			new ReasonCodeRepository(context.Data),
			new AuditRepository(context.Data),
			audit);
		context._materialIssues = new MaterialIssueService(
			new DatabaseTransactionRunner(context.Data),
			new MaterialIssueRepository(context.Data),
			new InventoryRepository(context.Data),
			issueMovements,
			new ReasonCodeRepository(context.Data),
			new AuditRepository(context.Data),
			audit,
			issueReversals);
		context._materialReturns = new MaterialReturnService(
			new DatabaseTransactionRunner(context.Data),
			new MaterialReturnRepository(context.Data),
			new MaterialIssueRepository(context.Data),
			new InventoryRepository(context.Data),
			issueMovements,
			new ReasonCodeRepository(context.Data),
			new AuditRepository(context.Data),
			audit,
			issueReversals);
		context._receipts = new GoodsReceiptService(
			new DatabaseTransactionRunner(context.Data),
			new GoodsReceiptRepository(context.Data),
			new PurchaseOrderRepository(context.Data),
			new InventoryRepository(context.Data),
			new StockMovementRepository(context.Data),
			new ReasonCodeRepository(context.Data),
			new AuditRepository(context.Data),
			audit,
			new StockMovementReversalService(
				new DatabaseTransactionRunner(context.Data),
				new InventoryRepository(context.Data),
				new StockMovementRepository(context.Data),
				new ReasonCodeRepository(context.Data),
				new AuditRepository(context.Data),
				audit));
		return context;
	}

	private static Task<long> InsertItemAsync(DatabaseAccess data, string partNumber, bool isActive) =>
		data.InsertAsync(
			"INSERT INTO Items (PartNumber, Description, IsActive) VALUES ($PartNumber, $Description, $IsActive);",
			CancellationToken.None,
			new DatabaseParameter("$PartNumber", partNumber),
			new DatabaseParameter("$Description", "Procurement integration test item"),
			new DatabaseParameter("$IsActive", isActive ? 1 : 0));

	private static Task<long> InsertInventoryAsync(
		DatabaseAccess data,
		long itemId,
		long purposeId,
		long storageLocationId,
		bool isActive) =>
		data.InsertAsync(
			"INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive) VALUES ($ItemId, $PurposeId, $StorageLocationId, $IsActive);",
			CancellationToken.None,
			new DatabaseParameter("$ItemId", itemId),
			new DatabaseParameter("$PurposeId", purposeId),
			new DatabaseParameter("$StorageLocationId", storageLocationId),
			new DatabaseParameter("$IsActive", isActive ? 1 : 0));

	private async Task RemoveTestRowsAsync()
	{
		var supplierIds = new[] { SupplierId, InactiveSupplierId };
		var itemIds = new[] { ItemId, SecondItemId, InactiveItemId };
		var inventoryIds = new[] { InventoryId, SecondInventoryId, InactiveInventoryId };
		await Data.ExecuteAsync(
			"DELETE FROM AuditEntries WHERE (EntityType = 'PurchaseOrder' AND EntityId IN (SELECT Id FROM PurchaseOrders WHERE SupplierId IN ($SupplierId, $InactiveSupplierId))) OR (EntityType = 'GoodsReceipt' AND EntityId IN (SELECT gr.Id FROM GoodsReceipts gr INNER JOIN PurchaseOrders po ON po.Id = gr.PurchaseOrderId WHERE po.SupplierId IN ($SupplierId, $InactiveSupplierId)));",
			CancellationToken.None,
			new DatabaseParameter("$SupplierId", supplierIds[0]),
			new DatabaseParameter("$InactiveSupplierId", supplierIds[1]));
		await Data.ExecuteAsync("DELETE FROM AuditEntries WHERE EntityType = 'MaterialIssue' AND EntityId IN (SELECT Id FROM MaterialIssues WHERE Recipient LIKE 'Material issue test %');", CancellationToken.None);
		await Data.ExecuteAsync("DELETE FROM AuditEntries WHERE EntityType = 'MaterialReturn' AND EntityId IN (SELECT Id FROM MaterialReturns WHERE RecipientOrSource LIKE 'Material return test %');", CancellationToken.None);
		await Data.ExecuteAsync("DELETE FROM StockMovements WHERE InventoryId IN ($First, $Second, $Inactive);", CancellationToken.None, new DatabaseParameter("$First", inventoryIds[0]), new DatabaseParameter("$Second", inventoryIds[1]), new DatabaseParameter("$Inactive", inventoryIds[2]));
		await Data.ExecuteAsync("DELETE FROM MaterialIssueLines WHERE MaterialIssueId IN (SELECT Id FROM MaterialIssues WHERE Recipient LIKE 'Material issue test %');", CancellationToken.None);
		await Data.ExecuteAsync("DELETE FROM MaterialReturnLines WHERE MaterialReturnId IN (SELECT Id FROM MaterialReturns WHERE RecipientOrSource LIKE 'Material return test %');", CancellationToken.None);
		await Data.ExecuteAsync("DELETE FROM MaterialReturns WHERE RecipientOrSource LIKE 'Material return test %';", CancellationToken.None);
		await Data.ExecuteAsync("DELETE FROM MaterialIssues WHERE Recipient LIKE 'Material issue test %';", CancellationToken.None);
		await Data.ExecuteAsync("DELETE FROM GoodsReceiptLines WHERE InventoryId IN ($First, $Second, $Inactive);", CancellationToken.None, new DatabaseParameter("$First", inventoryIds[0]), new DatabaseParameter("$Second", inventoryIds[1]), new DatabaseParameter("$Inactive", inventoryIds[2]));
		await Data.ExecuteAsync("DELETE FROM GoodsReceipts WHERE PurchaseOrderId IN (SELECT Id FROM PurchaseOrders WHERE SupplierId IN ($First, $Second));", CancellationToken.None, new DatabaseParameter("$First", supplierIds[0]), new DatabaseParameter("$Second", supplierIds[1]));
		await Data.ExecuteAsync("DELETE FROM PurchaseOrderLines WHERE PurchaseOrderId IN (SELECT Id FROM PurchaseOrders WHERE SupplierId IN ($First, $Second));", CancellationToken.None, new DatabaseParameter("$First", supplierIds[0]), new DatabaseParameter("$Second", supplierIds[1]));
		await Data.ExecuteAsync("DELETE FROM PurchaseOrders WHERE SupplierId IN ($First, $Second);", CancellationToken.None, new DatabaseParameter("$First", supplierIds[0]), new DatabaseParameter("$Second", supplierIds[1]));
		await Data.ExecuteAsync("DELETE FROM Users WHERE Id = $Id;", CancellationToken.None, new DatabaseParameter("$Id", ApproverUserId));
		await Data.ExecuteAsync("DELETE FROM Inventories WHERE Id IN ($First, $Second, $Inactive);", CancellationToken.None, new DatabaseParameter("$First", inventoryIds[0]), new DatabaseParameter("$Second", inventoryIds[1]), new DatabaseParameter("$Inactive", inventoryIds[2]));
		await Data.ExecuteAsync("DELETE FROM StorageLocations WHERE Id = $Id;", CancellationToken.None, new DatabaseParameter("$Id", TestStorageLocationId));
		await Data.ExecuteAsync("DELETE FROM Items WHERE Id IN ($First, $Second, $Inactive);", CancellationToken.None, new DatabaseParameter("$First", itemIds[0]), new DatabaseParameter("$Second", itemIds[1]), new DatabaseParameter("$Inactive", itemIds[2]));
		await Data.ExecuteAsync("DELETE FROM Suppliers WHERE Id IN ($First, $Second);", CancellationToken.None, new DatabaseParameter("$First", supplierIds[0]), new DatabaseParameter("$Second", supplierIds[1]));
	}
}
