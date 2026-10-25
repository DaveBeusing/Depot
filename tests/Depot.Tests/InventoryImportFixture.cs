// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using ClosedXML.Excel;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Depot.Services.Import;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

internal sealed class InventoryImportFixture : IAsyncDisposable
{
	private readonly string? _databasePath;
	private readonly AuthorizationService _authorization;

	private InventoryImportFixture(ObservedProviderConnectionFactory factory, AuthorizationService authorization, string? databasePath)
	{
		Factory = factory;
		Data = new DatabaseAccess(factory);
		Transactions = new ProviderTransactionGate(new DatabaseTransactionRunner(Data));
		_authorization = authorization;
		_databasePath = databasePath;
	}

	public DatabaseAccess Data { get; }
	public ObservedProviderConnectionFactory Factory { get; }
	public ProviderTransactionGate Transactions { get; }
	public string Prefix { get; } = $"IMPORT-{Guid.NewGuid():N}";

	public static async Task<InventoryImportFixture> CreateSqliteAsync()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-import-{Guid.NewGuid():N}.db");
		var factory = new SqliteConnectionFactory(path);
		DatabaseProvisioningService.Initialize(factory);
		return await CreateAsync(factory, path);
	}

	public static async Task<InventoryImportFixture> CreateAsync(IDatabaseConnectionFactory inner, string? databasePath = null)
	{
		var factory = new ObservedProviderConnectionFactory(inner);
		var authorization = new AuthorizationService();
		var admin = await new UserRepository(new DatabaseAccess(factory)).GetByEmailAsync("admin@depot.local", CancellationToken.None)
			?? throw new InvalidOperationException("Default administrator missing.");
		authorization.SignIn(admin, PermissionCatalog.All);
		return new InventoryImportFixture(factory, authorization, databasePath);
	}

	public ImportService CreateService(IDatabaseTransactionRunner? transactions = null)
	{
		var audits = new AuditRepository(Data);
		var audit = new AuditService(audits, _authorization);
		var items = new ItemRepository(Data);
		var inventories = new InventoryRepository(Data);
		var warehouses = new WarehouseRepository(Data);
		var locations = new StorageLocationRepository(Data);
		var movements = new StockMovementRepository(Data);
		var reasons = new ReasonCodeRepository(Data);
		var runner = new DatabaseTransactionRunner(Data);
		var traceability = new ItemTraceabilityService(new ItemTraceabilityRepository(Data), audit);
		var reversals = new StockMovementReversalService(runner, inventories, movements, reasons, audits, audit, traceability);
		var movementService = new MovementService(items, inventories, reasons, movements, audit, reversals, runner, audits, traceability);
		var itemService = new ItemService(items, audit,
			new ManufacturerService(new ManufacturerRepository(Data), audit),
			new CategoryService(new CategoryRepository(Data), audit),
			new UnitOfMeasureService(new UnitOfMeasureRepository(Data), audit),
			new PackagingService(new PackagingRepository(Data), audit),
			new SupplierItemRepository(Data));
		return new ImportService(items, itemService,
			new PurposeService(new PurposeRepository(Data), audit),
			new WarehouseService(warehouses, locations, audit),
			new StorageLocationService(locations, warehouses, audit),
			new InventoryManagementService(inventories, audit),
			movementService, _authorization, transactions ?? Transactions);
	}

	public ImportPreview CreatePreview() => new() { Items = [NewRow($"{Prefix}-NEW", 3, 2.50m)] };

	public async Task<ImportPreview> CreateSerialFailurePreviewAsync()
	{
		var serialPart = $"{Prefix}-SERIAL";
		await Data.InsertAsync("INSERT INTO Items (PartNumber,Description,TrackingMode,IsActive) VALUES ($Part,'Existing tracked item',$Mode,1);",
			CancellationToken.None, new DatabaseParameter("$Part", serialPart), new DatabaseParameter("$Mode", (int)ItemTrackingMode.SerialNumber));
		return new ImportPreview
		{
			Items = [NewRow($"{Prefix}-NEW", 3, 2.50m), NewRow(serialPart, 2, 7m, true)]
		};
	}

	public Task MakeSerialItemUntrackedAsync() => Data.ExecuteAsync(
		"UPDATE Items SET TrackingMode=$Mode WHERE PartNumber=$Part;", CancellationToken.None,
		new DatabaseParameter("$Mode", (int)ItemTrackingMode.None), new DatabaseParameter("$Part", $"{Prefix}-SERIAL"));

	private ImportPreviewItem NewRow(string partNumber, int quantity, decimal unitPrice, bool existing = false) => new()
	{
		PartNumber = partNumber,
		Description = "Imported stock",
		Manufacturer = $"{Prefix}-MANUFACTURER",
		Category = $"{Prefix}-CATEGORY",
		Purpose = $"{Prefix}-PURPOSE",
		Warehouse = $"{Prefix}-WAREHOUSE",
		Location = $"{Prefix}-LOCATION",
		Quantity = quantity,
		UnitPrice = unitPrice,
		TotalValue = quantity * unitPrice,
		ItemAlreadyExists = existing
	};

	public static ImportPreview WithChangedQuantity(ImportPreview preview) =>
		WithFirstRowValues(preview, preview.Items[0].Quantity + 1, preview.Items[0].UnitPrice);

	public static ImportPreview WithEquivalentPriceScale(ImportPreview preview) =>
		WithFirstRowValues(preview, preview.Items[0].Quantity, preview.Items[0].UnitPrice * 1.00m);

	private static ImportPreview WithFirstRowValues(ImportPreview preview, int quantity, decimal unitPrice)
	{
		var first = preview.Items[0];
		var changed = new ImportPreviewItem
		{
			PartNumber = first.PartNumber, Description = first.Description,
			Manufacturer = first.Manufacturer, Category = first.Category,
			Purpose = first.Purpose, Warehouse = first.Warehouse, Location = first.Location,
			Quantity = quantity, UnitPrice = unitPrice,
			TotalValue = quantity * unitPrice, ItemAlreadyExists = first.ItemAlreadyExists
		};
		return new ImportPreview { OperationId = preview.OperationId, Items = [changed, .. preview.Items.Skip(1)] };
	}

	public async Task<InventoryImportSnapshot> SnapshotAsync() =>
		await Data.QuerySingleOrDefaultAsync(
			"""
			SELECT (SELECT COUNT(*) FROM Items), (SELECT COUNT(*) FROM Manufacturers),
			       (SELECT COUNT(*) FROM Categories), (SELECT COUNT(*) FROM Purposes),
			       (SELECT COUNT(*) FROM Warehouses), (SELECT COUNT(*) FROM StorageLocations),
			       (SELECT COUNT(*) FROM Inventories), (SELECT COUNT(*) FROM StockMovements),
			       (SELECT COUNT(*) FROM ItemTrackingUnits), (SELECT COUNT(*) FROM StockMovementTracking),
			       (SELECT COUNT(*) FROM AuditEntries), (SELECT COUNT(*) FROM WorkflowOperations);
			""",
			reader => new InventoryImportSnapshot(
				Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(7), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(8), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(9), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(10), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(11), CultureInfo.InvariantCulture)),
			CancellationToken.None) ?? throw new InvalidOperationException("Import snapshot missing.");

	public async Task AssertCommittedAsync(ImportPreview preview)
	{
		var reference = $"IMPORT:{preview.OperationId:D}";
		var rows = await Data.QueryAsync(
			"""
			SELECT i.PartNumber, m.MovementType, m.Quantity, m.UnitPrice
			FROM StockMovements m INNER JOIN Inventories inventory ON inventory.Id=m.InventoryId
			INNER JOIN Items i ON i.Id=inventory.ItemId WHERE m.Reference=$Reference;
			""",
			reader => new ImportMovementEvidence(reader.GetString(0), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
				Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture)),
			CancellationToken.None, new DatabaseParameter("$Reference", reference));
		Assert.Equal(preview.Items.Count, rows.Count);
		foreach (var item in preview.Items)
		{
			var movement = Assert.Single(rows, row => row.PartNumber == item.PartNumber);
			Assert.Equal((int)StockMovementType.OpeningBalance, movement.MovementType);
			Assert.Equal((long)item.Quantity, movement.Quantity);
			Assert.Equal(item.UnitPrice, movement.UnitPrice);
		}
		Assert.Equal((long)rows.Count, await ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries a INNER JOIN StockMovements m ON a.EntityId=m.Id WHERE a.EntityType='StockMovement' AND a.Action='Created' AND m.Reference=$Reference;",
			new DatabaseParameter("$Reference", reference)));
		Assert.Equal(1L, await ScalarAsync(
			"SELECT COUNT(*) FROM WorkflowOperations w INNER JOIN Inventories inventory ON inventory.Id=w.EntityId INNER JOIN Items i ON i.Id=inventory.ItemId WHERE w.OperationId=$Operation AND i.PartNumber=$Part;",
			new DatabaseParameter("$Operation", preview.OperationId.ToString("D")), new DatabaseParameter("$Part", preview.Items[0].PartNumber)));
	}

	public static void AssertReplay(ImportResult result, ImportPreview preview)
	{
		Assert.True(result.AlreadyCompleted);
		Assert.Equal(0, result.ImportedItems);
		Assert.Equal(0, result.ImportedMovements);
		Assert.Equal(preview.Items.Count, result.SkippedItems);
	}

	public string WriteWorkbook(ImportPreview preview)
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-import-workbook-{Guid.NewGuid():N}.xlsx");
		using var workbook = new XLWorkbook();
		var sheet = workbook.AddWorksheet("Import");
		ImportTargetField[] fields = [ImportTargetField.PartNumber, ImportTargetField.Description, ImportTargetField.Manufacturer,
			ImportTargetField.Category, ImportTargetField.Purpose, ImportTargetField.Warehouse, ImportTargetField.Location,
			ImportTargetField.Quantity, ImportTargetField.UnitPrice];
		for (var column = 0; column < fields.Length; column++)
		{
			sheet.Cell(1, column + 1).Value = ImportTargetCatalog.Get(fields[column]).DefaultHeader;
			for (var row = 0; row < preview.Items.Count; row++)
			{
				var item = preview.Items[row];
				var cell = sheet.Cell(row + 2, column + 1);
				if (fields[column] == ImportTargetField.Quantity) cell.Value = item.Quantity;
				else if (fields[column] == ImportTargetField.UnitPrice) cell.Value = item.UnitPrice;
				else cell.Value = fields[column] switch
				{
					ImportTargetField.PartNumber => item.PartNumber,
					ImportTargetField.Description => item.Description,
					ImportTargetField.Manufacturer => item.Manufacturer ?? string.Empty,
					ImportTargetField.Category => item.Category ?? string.Empty,
					ImportTargetField.Purpose => item.Purpose,
					ImportTargetField.Warehouse => item.Warehouse,
					ImportTargetField.Location => item.Location,
					_ => string.Empty
				};
			}
		}
		workbook.SaveAs(path);
		return path;
	}

	private async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await Data.ExecuteScalarAsync(sql, CancellationToken.None, parameters), CultureInfo.InvariantCulture);

	public ValueTask DisposeAsync()
	{
		if (_databasePath is not null)
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(_databasePath)) File.Delete(_databasePath);
		}
		return ValueTask.CompletedTask;
	}
}

internal sealed record InventoryImportSnapshot(long Items, long Manufacturers, long Categories, long Purposes,
	long Warehouses, long Locations, long Inventories, long Movements, long TrackingUnits, long TrackingRows, long Audits, long Operations);
internal sealed record ImportMovementEvidence(string PartNumber, int MovementType, long Quantity, decimal UnitPrice);

internal sealed class ImportCommitResponseLossRunner(IDatabaseTransactionRunner inner) : IDatabaseTransactionRunner
{
	private int _loseNextResponse = 1;

	public async Task<T> ExecuteAsync<T>(Func<DatabaseTransactionContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
	{
		var result = await inner.ExecuteAsync(operation, cancellationToken);
		if (Interlocked.Exchange(ref _loseNextResponse, 0) == 1) throw new IOException("Committed import response was lost.");
		return result;
	}
}
