// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class BusinessRecordIntegrityTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-gobd-{Guid.NewGuid():N}.db");
	private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"depot-gobd-{Guid.NewGuid():N}.json");
	private readonly SqliteConnectionFactory _factory;
	private readonly DatabaseAccess _database;

	public BusinessRecordIntegrityTests()
	{
		_factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(_factory).Initialize();
		SalesSchemaMigration.Migrate(_factory);
		_database = new DatabaseAccess(_factory);
	}

	[Fact]
	public void CatalogClassifiesCoreFinalizedBusinessRecords()
	{
		var types = BusinessRecordCatalog.All.Select(value => value.EntityType).ToArray();
		Assert.Equal(types.Length, types.Distinct(StringComparer.Ordinal).Count());
		Assert.Contains(nameof(PurchaseOrder), types);
		Assert.Contains(nameof(GoodsReceipt), types);
		Assert.Contains(nameof(SalesOrder), types);
		Assert.Contains(nameof(Shipment), types);
		Assert.Contains(nameof(SalesInvoice), types);
		Assert.Contains(nameof(SalesCreditNote), types);
		Assert.Contains(nameof(StockMovement), types);
		Assert.All(BusinessRecordCatalog.All, value => Assert.True(value.HistoricalSnapshotRequired));
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(SalesInvoice)).RetentionCategory);
		Assert.Contains("Credit note", BusinessRecordCatalog.Require(nameof(SalesInvoice)).CorrectionMechanism, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task EvidenceExportContainsChronologicalSanitizedHistoryAndCurrentSnapshot()
	{
		var authorization = Authorized(ApplicationPermission.AuditLogView, ApplicationPermission.AuditLogExport);
		var repository = new AuditRepository(_database);
		await repository.CreateAsync(new AuditEntry
		{
			TimestampUtc = new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc),
			UserId = 7,
			UserEmail = "accounting@depot.test",
			EntityType = nameof(SalesInvoice),
			EntityId = 42,
			Action = "Created",
			AfterJson = "{\"invoiceNumber\":\"INV-000042\",\"status\":1,\"passwordHash\":\"must-not-leak\"}"
		}, CancellationToken.None);
		await repository.CreateAsync(new AuditEntry
		{
			TimestampUtc = new DateTime(2026, 8, 22, 11, 0, 0, DateTimeKind.Utc),
			UserId = 8,
			UserEmail = "poster@depot.test",
			EntityType = nameof(SalesInvoice),
			EntityId = 42,
			Action = "Updated",
			BeforeJson = "{\"invoiceNumber\":\"INV-000042\",\"status\":1}",
			AfterJson = "{\"invoiceNumber\":\"INV-000042\",\"status\":2}"
		}, CancellationToken.None);

		var service = new AuditLogService(repository, authorization, new AuditJsonSanitizer());
		await service.ExportBusinessRecordEvidenceAsync(nameof(SalesInvoice), 42, _exportPath, CancellationToken.None);

		using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_exportPath));
		var root = document.RootElement;
		Assert.Equal("depot-business-record-evidence/1.0", root.GetProperty("schema").GetString());
		Assert.Equal(nameof(SalesInvoice), root.GetProperty("entityType").GetString());
		Assert.Equal(42, root.GetProperty("entityId").GetInt64());
		Assert.Equal(2, root.GetProperty("eventCount").GetInt32());
		Assert.Equal("Created", root.GetProperty("events")[0].GetProperty("action").GetString());
		Assert.Equal("Updated", root.GetProperty("events")[1].GetProperty("action").GetString());
		Assert.Equal(2, root.GetProperty("currentSnapshot").GetProperty("status").GetInt32());
		Assert.DoesNotContain("must-not-leak", await File.ReadAllTextAsync(_exportPath), StringComparison.Ordinal);
	}

	[Fact]
	public async Task SalesOrderDraftAndAuditRollbackTogetherWhenAuditInsertFails()
	{
		var customerId = await _database.InsertAsync(
			"INSERT INTO Customers (CustomerNumber,Name,PaymentTermsDays,Currency,IsActive) VALUES ('CU-ATOMIC','Atomic Customer',30,'EUR',1);",
			CancellationToken.None);
		var itemId = await _database.InsertAsync(
			"INSERT INTO Items (PartNumber,Description,IsActive) VALUES ('ATOMIC-ITEM','Atomic Item',1);",
			CancellationToken.None);
		await _database.ExecuteAsync(
			"CREATE TRIGGER RejectSalesOrderAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'SalesOrder' BEGIN SELECT RAISE(ABORT, 'audit rejected'); END;",
			CancellationToken.None);

		var authorization = Authorized(ApplicationPermission.SalesOrdersCreate);
		var transactions = new DatabaseTransactionRunner(_database);
		var auditRepository = new AuditRepository(_database);
		var service = new SalesOrderService(
			transactions,
			new SalesOrderRepository(_database),
			new CustomerRepository(_database),
			new ItemRepository(_database),
			new InventoryRepository(_database),
			new InventoryReservationRepository(_database),
			new StockMovementRepository(_database),
			auditRepository,
			new AuditService(auditRepository, authorization),
			authorization,
			new NotificationService(transactions, new NotificationRepository(_database), authorization));
		var order = new SalesOrder
		{
			CustomerId = customerId,
			OrderDate = DateTime.Today,
			Currency = "EUR",
			Status = SalesOrderStatus.Draft,
			Lines = [new SalesOrderLine { ItemId = itemId, Quantity = 1, UnitPrice = 10m, DiscountPercent = 0m, TaxRate = 19m }]
		};

		await Assert.ThrowsAnyAsync<Exception>(() => service.SaveDraftAsync(order, CancellationToken.None));
		Assert.Equal(0L, Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM SalesOrders;", CancellationToken.None)));
		Assert.Equal(0L, Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType='SalesOrder';", CancellationToken.None)));
	}

	[Fact]
	public async Task EvidenceExportRequiresDedicatedExportPermission()
	{
		var repository = new AuditRepository(_database);
		var service = new AuditLogService(repository, Authorized(ApplicationPermission.AuditLogView), new AuditJsonSanitizer());
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExportBusinessRecordEvidenceAsync(nameof(SalesInvoice), 1, _exportPath, CancellationToken.None));
	}

	[Fact]
	public async Task EvidenceExportRejectsUnclassifiedEntities()
	{
		var repository = new AuditRepository(_database);
		var service = new AuditLogService(repository, Authorized(ApplicationPermission.AuditLogView, ApplicationPermission.AuditLogExport), new AuditJsonSanitizer());
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportBusinessRecordEvidenceAsync("Customer", 1, _exportPath, CancellationToken.None));
	}

	private static AuthorizationService Authorized(params ApplicationPermission[] permissions)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, Email = "auditor@depot.test", DisplayName = "Auditor", IsActive = true }, permissions);
		return authorization;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		if (File.Exists(_exportPath)) File.Delete(_exportPath);
	}
}
