// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class DatabaseManagementTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"depot-management-{Guid.NewGuid():N}");
	private readonly string _databasePath;
	private readonly string _settingsPath;
	private readonly SettingsService _settingsService;
	private readonly DatabaseManagementService _service;
	private readonly ItemRepository _items;
	private readonly SqliteConnectionFactory _factory;

	public DatabaseManagementTests()
	{
		Directory.CreateDirectory(_directory);
		_databasePath = Path.Combine(_directory, "depot.db");
		_settingsPath = Path.Combine(_directory, "depot.settings");
		var settingsRepository = new SettingsRepository(_settingsPath);
		_settingsService = new SettingsService(settingsRepository);
		_settingsService.LoadOrCreate();
		_settingsService.Save(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.Local,
			LocalDatabasePath = _databasePath,
			BackupDirectory = Path.Combine(_directory, "Backups")
		});
		_factory = new SqliteConnectionFactory(_databasePath);
		DatabaseProvisioningService.Initialize(_factory);
		var access = new DatabaseAccess(_factory);
		_items = new ItemRepository(access);
		_service = new DatabaseManagementService(_factory, _settingsService, CreateAuthorization());
	}

	[Fact]
	public async Task BackupIsValidatedAndRestoreReplacesCurrentRecords()
	{
		var preserved = new Item { PartNumber = "BACKUP-1", Description = "Preserved", IsActive = true };
		preserved.Id = await _items.CreateAsync(preserved, CancellationToken.None);
		var backupPath = Path.Combine(_directory, "manual.depotbackup");
		var backup = await _service.CreateBackupAsync(backupPath);

		Assert.True(backup.IsValid);
		Assert.True(File.Exists(backupPath));

		var discarded = new Item { PartNumber = "AFTER-1", Description = "Discarded", IsActive = true };
		discarded.Id = await _items.CreateAsync(discarded, CancellationToken.None);
		var safetyPath = await _service.RestoreBackupAsync(backupPath);

		Assert.NotNull(await _items.GetByPartNumberAsync("BACKUP-1", CancellationToken.None));
		Assert.Null(await _items.GetByPartNumberAsync("AFTER-1", CancellationToken.None));
		Assert.True(File.Exists(safetyPath));
	}

	[Fact]
	public async Task CorruptedBackupIsRejectedBeforeRestore()
	{
		var backupPath = Path.Combine(_directory, "damaged.depotbackup");
		await File.WriteAllTextAsync(backupPath, "not a depot backup");

		var validation = await _service.ValidateBackupAsync(backupPath);

		Assert.False(validation.IsValid);
		Assert.Contains("damaged", validation.ValidationMessage, StringComparison.OrdinalIgnoreCase);
		await Assert.ThrowsAsync<InvalidDataException>(() => _service.RestoreBackupAsync(backupPath));
	}

	[Fact]
	public async Task ScheduledBackupPersistsCompletionAndSurvivesSettingsReload()
	{
		var settings = _settingsService.CurrentSettings;
		settings.AutomaticBackupsEnabled = true;
		settings.BackupIntervalDays = 1;
		settings.LastSuccessfulBackupUtc = null;
		_settingsService.Save(settings);
		using var scheduler = new DatabaseBackupScheduler(_service, _settingsService);

		Assert.True(await scheduler.RunDueBackupAsync());
		Assert.NotNull(_settingsService.CurrentSettings.LastSuccessfulBackupUtc);
		Assert.Single(Directory.EnumerateFiles(settings.BackupDirectory, "*.depotbackup"));

		var reloaded = new SettingsService(new SettingsRepository(_settingsPath));
		var persisted = reloaded.LoadOrCreate();
		Assert.True(persisted.AutomaticBackupsEnabled);
		Assert.Equal(1, persisted.BackupIntervalDays);
		Assert.NotNull(persisted.LastSuccessfulBackupUtc);
	}

	[Fact]
	public async Task OverviewIntegrityAndCompactWorkForSqlite()
	{
		var integrity = await _service.CheckIntegrityAsync();
		await _service.CompactAsync();
		var overview = await _service.GetOverviewAsync();

		Assert.True(integrity.IsValid);
		Assert.Equal(DatabaseProvider.Local, overview.Provider);
		Assert.Equal(DatabaseVersion.CurrentVersion, overview.SchemaVersion);
		Assert.True(overview.SizeBytes > 0);
		Assert.DoesNotContain("Password", overview.ConnectionDisplayName, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task CompleteRoundTripPreservesModernFeaturesBinaryContentsAndAllTableHashes()
	{
		await SeedModernDataAsync();
		var original = Path.Combine(_directory, "complete.depotbackup");
		await _service.CreateBackupAsync(original);
		var before = await ReadManifestAsync(original);
		Assert.Equal(2, before.FormatVersion);
		Assert.Equal(DatabaseBackupSchema.ContractVersion, before.ContractVersion);
		Assert.Equal(DatabaseBackupSchema.CurrentFeatureVersions.Count, before.FeatureVersions.Count);
		await using (var connection = _factory.CreateConnection())
		{
			await connection.OpenAsync();
			var liveTables = await DatabaseBackupSchema.QueryAsync(connection, null,
				"SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", CancellationToken.None);
			Assert.Equal(liveTables.Select(row => (string)row[0]).OrderBy(name => name, StringComparer.Ordinal), before.Tables.Select(table => table.Schema.Name));
			foreach (var table in before.Tables)
			{
				var liveColumns = await DatabaseBackupSchema.QueryAsync(connection, null, $"PRAGMA table_xinfo([{table.Schema.Name}]);", CancellationToken.None);
				Assert.Equal(liveColumns.Select(row => (string)row[1]).OrderBy(name => name, StringComparer.Ordinal), table.Schema.Columns.Select(column => column.Name));
			}
		}
		foreach (var name in new[] { "BusinessAttachments", "BusinessAttachmentContents", "FinanceLegalEntities", "FinanceJournalEntries", "FinanceJournalEntryLines", "SalesInvoices", "SalesInvoiceLines", "Projects", "UserSessions", "UserWorkspaceFavorites", "DatabaseInfo", "DepotFeatureVersions",
			"SubscriptionContracts", "SubscriptionContractLines", "SubscriptionContractRevisions", "SubscriptionLifecycleEvents", "SubscriptionBillingInstances", "SubscriptionBillingPriceEvidence",
			"SalesLeads", "SalesOpportunities", "ItemTrackingUnits", "StockMovementTracking" })
			Assert.True(Assert.Single(before.Tables, table => table.Schema.Name == name).RowCount > 0, name);

		await ExecuteAsync("UPDATE Items SET Gtin='changed',Revision='changed',NetWeightKg=99 WHERE Id=1001; UPDATE FinanceJournalEntryLines SET TransactionDebit=999 WHERE Id=1011; UPDATE BusinessAttachmentContents SET Content=X'00'; INSERT INTO Customers(CustomerNumber,Name) VALUES('AFTER-SNAPSHOT','Discard after restore');");
		await ExecuteAsync("""
			UPDATE SubscriptionContractRevisions SET TermsSnapshotJson='{}' WHERE ContractId=1101 AND TermsRevision=1;
			UPDATE SubscriptionBillingInstances SET PeriodEnd='2026-10-20',SalesInvoiceId=NULL WHERE Id=1106;
			UPDATE SubscriptionBillingPriceEvidence SET UnitPrice=999.9999,DiscountPercent=0 WHERE Id=1107;
			""");
		var safety = await _service.RestoreBackupAsync(original);
		Assert.True((await _service.ValidateBackupAsync(safety)).IsValid);
		Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM Customers WHERE CustomerNumber='AFTER-SNAPSHOT';"));
		Assert.Equal("04012345678901", await ScalarAsync("SELECT Gtin FROM Items WHERE Id=1001;"));
		Assert.Equal("R7", await ScalarAsync("SELECT Revision FROM Items WHERE Id=1001;"));
		Assert.Equal(1.234567, Convert.ToDouble(await ScalarAsync("SELECT NetWeightKg FROM Items WHERE Id=1001;"), CultureInfo.InvariantCulture));
		Assert.Equal(AttachmentBytes, (byte[])(await ScalarAsync("SELECT Content FROM BusinessAttachmentContents;"))!);
		Assert.Equal("blob", await ScalarAsync("SELECT typeof(Content) FROM BusinessAttachmentContents;"));
		Assert.Equal(123.456789, Convert.ToDouble(await ScalarAsync("SELECT TransactionDebit FROM FinanceJournalEntryLines WHERE Id=1011;"), CultureInfo.InvariantCulture));
		Assert.Equal(SubscriptionTermsSnapshot, await ScalarAsync("SELECT TermsSnapshotJson FROM SubscriptionContractRevisions WHERE ContractId=1101 AND TermsRevision=1;"));
		Assert.Equal("2026-10-31", await ScalarAsync("SELECT PeriodEnd FROM SubscriptionBillingInstances WHERE Id=1106;"));
		Assert.Equal(1104L, await ScalarAsync("SELECT SalesInvoiceId FROM SubscriptionBillingInstances WHERE Id=1106;"));
		Assert.Equal(12.3456m, Convert.ToDecimal(await ScalarAsync("SELECT UnitPrice FROM SubscriptionBillingPriceEvidence WHERE Id=1107;"), CultureInfo.InvariantCulture));
		Assert.Equal(5.4321m, Convert.ToDecimal(await ScalarAsync("SELECT DiscountPercent FROM SubscriptionBillingPriceEvidence WHERE Id=1107;"), CultureInfo.InvariantCulture));
		Assert.Equal(1111L, await ScalarAsync("SELECT ConvertedOpportunityId FROM SalesLeads WHERE Id=1110;"));
		Assert.Equal(2L, await ScalarAsync("SELECT SUM(Quantity) FROM StockMovementTracking WHERE StockMovementId=1120;"));
		Assert.True((await _service.CheckIntegrityAsync()).IsValid);
		var afterPath = Path.Combine(_directory, "after-restore.depotbackup");
		await _service.CreateBackupAsync(afterPath);
		AssertTableHashesEqual(before, await ReadManifestAsync(afterPath));
	}

	[Theory]
	[InlineData("legacy")]
	[InlineData("format-type")]
	[InlineData("missing-table")]
	[InlineData("missing-column")]
	[InlineData("feature-version")]
	[InlineData("metadata-row")]
	[InlineData("checksum")]
	[InlineData("binary-type")]
	[InlineData("identity-type")]
	[InlineData("feature-name-type")]
	[InlineData("duplicate-entry")]
	[InlineData("unexpected-entry")]
	[InlineData("sql-identifier")]
	public async Task IncompleteOrTamperedArchivesAreRejectedBeforeSafetyBackupOrDeletion(string corruption)
	{
		await SeedModernDataAsync();
		var path = Path.Combine(_directory, $"invalid-{corruption}.depotbackup");
		await _service.CreateBackupAsync(path);
		MutateArchive(path, corruption);
		var validation = await _service.ValidateBackupAsync(path);
		Assert.False(validation.IsValid, corruption);
		await Assert.ThrowsAsync<InvalidDataException>(() => _service.RestoreBackupAsync(path));
		Assert.False(Directory.Exists(_settingsService.CurrentSettings.BackupDirectory));
		Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Items WHERE Id=1001 AND Gtin='04012345678901';"));
		Assert.Equal(AttachmentBytes, (byte[])(await ScalarAsync("SELECT Content FROM BusinessAttachmentContents;"))!);
	}

	[Fact]
	public async Task ForeignKeyViolationDuringRestoreRollsBackEveryTable()
	{
		await SeedModernDataAsync();
		var path = Path.Combine(_directory, "invalid-relationship.depotbackup");
		await _service.CreateBackupAsync(path);
		MutateTable(path, "UserSessions", (rows, columns) => rows[0]![Array.IndexOf(columns, "UserId")] = new JsonArray("integer", long.MaxValue.ToString(CultureInfo.InvariantCulture)));
		Assert.True((await _service.ValidateBackupAsync(path)).IsValid);
		await ExecuteAsync("INSERT INTO Customers(CustomerNumber,Name) VALUES('KEEP-ON-FAILURE','Current state'); UPDATE Items SET Revision='current' WHERE Id=1001;");
		var beforePath = Path.Combine(_directory, "before-failed-restore.depotbackup");
		await _service.CreateBackupAsync(beforePath);
		await Assert.ThrowsAsync<InvalidDataException>(() => _service.RestoreBackupAsync(path));
		var afterPath = Path.Combine(_directory, "after-failed-restore.depotbackup");
		await _service.CreateBackupAsync(afterPath);
		AssertTableHashesEqual(await ReadManifestAsync(beforePath), await ReadManifestAsync(afterPath));
		Assert.True((await _service.CheckIntegrityAsync()).IsValid);
		await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync("INSERT INTO UserWorkspaceFavorites(UserId,RouteId,PinnedUtc) VALUES(9223372036854775807,'invalid','2026-10-01');"));
	}

	[Fact]
	public async Task AValidBackupCanReplaceExistingOrphansWhileKeepingTheirSafetySnapshot()
	{
		var path = Path.Combine(_directory, "healthy.depotbackup");
		await _service.CreateBackupAsync(path);
		await ExecuteAsync("PRAGMA foreign_keys=OFF; INSERT INTO UserWorkspaceFavorites(UserId,RouteId,PinnedUtc) VALUES(9223372036854775807,'orphan','2026-10-01'); PRAGMA foreign_keys=ON;");
		Assert.False((await _service.CheckIntegrityAsync()).IsValid);
		var safetyPath = await _service.RestoreBackupAsync(path);
		Assert.Equal(1L, Assert.Single((await ReadManifestAsync(safetyPath)).Tables, table => table.Schema.Name == "UserWorkspaceFavorites").RowCount);
		Assert.True((await _service.CheckIntegrityAsync()).IsValid);
		Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM UserWorkspaceFavorites;"));
	}

	[Fact]
	public async Task CancellationAfterDeletionRollsBackRowsAndRestoresTriggers()
	{
		await SeedModernDataAsync();
		var path = Path.Combine(_directory, "cancelled-restore.depotbackup");
		await _service.CreateBackupAsync(path);
		await ExecuteAsync("UPDATE Items SET Revision='current' WHERE Id=1001; INSERT INTO Customers(CustomerNumber,Name) VALUES('KEEP-ON-CANCEL','Current state');");
		var beforePath = Path.Combine(_directory, "before-cancel.depotbackup");
		await _service.CreateBackupAsync(beforePath);
		var triggersBefore = await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger';");
		using var cancellation = new CancellationTokenSource();
		var progress = new InlineProgress(value => { if (value.Message.StartsWith("Restoring ", StringComparison.Ordinal)) cancellation.Cancel(); });
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RestoreBackupAsync(path, progress, cancellation.Token));
		var afterPath = Path.Combine(_directory, "after-cancel.depotbackup");
		await _service.CreateBackupAsync(afterPath);
		AssertTableHashesEqual(await ReadManifestAsync(beforePath), await ReadManifestAsync(afterPath));
		Assert.Equal(triggersBefore, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger';"));
		Assert.True((await _service.CheckIntegrityAsync()).IsValid);
	}

	[Fact]
	public async Task RestoreHandlesCyclicForeignKeysSelfReferencesAndGeneratedColumns()
	{
		await ExecuteAsync("""
			CREATE TABLE BackupCycleA(Id INTEGER PRIMARY KEY,OtherId INTEGER NOT NULL REFERENCES BackupCycleB(Id));
			CREATE TABLE BackupCycleB(Id INTEGER PRIMARY KEY,OtherId INTEGER NOT NULL REFERENCES BackupCycleA(Id));
			CREATE TABLE BackupSelfReference(Id INTEGER PRIMARY KEY,ParentId INTEGER NOT NULL REFERENCES BackupSelfReference(Id),Squared INTEGER GENERATED ALWAYS AS(Id*Id) STORED);
			BEGIN;
			PRAGMA defer_foreign_keys=ON;
			INSERT INTO BackupCycleA VALUES(1,2);
			INSERT INTO BackupCycleB VALUES(2,1);
			INSERT INTO BackupSelfReference(Id,ParentId) VALUES(3,3);
			COMMIT;
			""");
		var path = Path.Combine(_directory, "cycles.depotbackup");
		await _service.CreateBackupAsync(path);
		var generated = Assert.Single((await ReadManifestAsync(path)).Tables, table => table.Schema.Name == "BackupSelfReference");
		Assert.True(Assert.Single(generated.Schema.Columns, column => column.Name == "Squared").IsGenerated);
		await _service.RestoreBackupAsync(path);
		Assert.Equal(2L, await ScalarAsync("SELECT OtherId FROM BackupCycleA WHERE Id=1;"));
		Assert.Equal(1L, await ScalarAsync("SELECT OtherId FROM BackupCycleB WHERE Id=2;"));
		Assert.Equal(9L, await ScalarAsync("SELECT Squared FROM BackupSelfReference WHERE Id=3;"));
		Assert.True((await _service.CheckIntegrityAsync()).IsValid);
	}

	[Fact]
	public async Task MissingCurrentFeatureTableCannotProduceApparentlyCompleteBackup()
	{
		await ExecuteAsync("DROP TABLE FinanceExchangeRates;");
		var path = Path.Combine(_directory, "incomplete-source.depotbackup");
		await Assert.ThrowsAsync<InvalidDataException>(() => _service.CreateBackupAsync(path));
		Assert.False(File.Exists(path));
		Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp-*", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task DeletedIdentityHighWaterMarkIsPreservedOnFreshSqliteTarget()
	{
		await ExecuteAsync("INSERT INTO Purposes(Id,Name) VALUES(50000,'Deleted identity'); DELETE FROM Purposes WHERE Id=50000;");
		var path = Path.Combine(_directory, "identity.depotbackup");
		await _service.CreateBackupAsync(path);
		var targetFactory = new SqliteConnectionFactory(Path.Combine(_directory, "fresh-target.db"));
		DatabaseProvisioningService.Initialize(targetFactory);
		var target = new DatabaseManagementService(targetFactory, _settingsService, CreateAuthorization());
		await target.RestoreBackupAsync(path);
		await using var connection = targetFactory.CreateConnection();
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO Purposes(Name) VALUES('After fresh restore'); SELECT last_insert_rowid();";
		Assert.True(Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 50000);
	}

	[Fact]
	public void CompleteSchemaMatchingAcceptsProviderIdentifierCasingAndRejectsMissingColumns()
	{
		DatabaseBackupColumn Column(string name) => new(name, "text", true, false, false, 0);
		DatabaseBackupTable[] required = [new("Items", [Column("Id"), Column("Gtin")])];
		DatabaseBackupSchema.RequireCompleteTables([new("items", [Column("id"), Column("gtin"), Column("provider_generated")])], required);
		Assert.Throws<InvalidDataException>(() => DatabaseBackupSchema.RequireCompleteTables([new("items", [Column("id")])], required));
	}

	[Fact]
	public void TypedValuesPreserveBinaryDecimalAndTemporalPrecision()
	{
		object[] values = [AttachmentBytes, 1234567890123456789.123456789m,
			new DateTime(2026, 10, 1, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1234567),
			new DateTimeOffset(2026, 10, 1, 12, 34, 56, TimeSpan.FromHours(5.5)).AddTicks(7654321), Guid.NewGuid()];
		foreach (var value in values)
		{
			using var stream = new MemoryStream();
			using (var writer = new Utf8JsonWriter(stream)) { DatabaseBackupArchive.WriteValue(writer, value); writer.Flush(); }
			using var document = JsonDocument.Parse(stream.ToArray());
			var column = new DatabaseBackupColumn("Value", value is byte[] ? "varbinary:max" : "value", false, false, false, 0);
			var restored = DatabaseBackupArchive.ReadValue(document.RootElement, column, DatabaseProvider.SqlServer);
			if (value is byte[] bytes) Assert.Equal(bytes, Assert.IsType<byte[]>(restored));
			else Assert.Equal(value, restored);
			if (value is DateTime date) Assert.Equal(date.Kind, Assert.IsType<DateTime>(restored).Kind);
		}
	}

	private static readonly byte[] AttachmentBytes = [0, 1, 2, 127, 128, 255, 0, 13, 10, 42];
	private const string SubscriptionTermsSnapshot = """
		{"id":1101,"contractNumber":"SUB-COMPLETE","customerId":1001,"legalEntityId":"00000000-0000-0000-0000-000000000001",
		"currency":"EUR","startDate":"2026-10-01","endDate":null,"cadence":1,"termsRevision":1,
		"lines":[{"id":1102,"contractId":1101,"termsRevision":1,"lineNumber":1,"itemId":1001,"partNumber":"COMPLETE-ITEM",
		"description":"Complete recurring line","quantity":2,"unitPrice":12.3456,"discountPercent":5.4321,"taxRate":19,"pricePolicy":1,
		"priceSourceListId":null,"priceSourceName":"Contract snapshot","priceSourceScope":null,"priceSourceCurrency":"EUR"}]}
		""";

	private async Task SeedModernDataAsync()
	{
		await ExecuteAsync("""
			INSERT INTO Items(Id,PartNumber,Description,Gtin,Revision,NetWeightKg,TrackingMode,ContainsBattery,Notes)
			VALUES(1001,'COMPLETE-ITEM','Complete backup item','04012345678901','R7',1.234567,1,1,'Preserve item master fields');
			INSERT INTO Customers(Id,CustomerNumber,Name) VALUES(1001,'COMPLETE-CUSTOMER','Complete backup customer');
			INSERT INTO SalesOrders(Id,OrderNumber,CustomerId,OrderDate,CreatedByUserId) VALUES(1002,'SO-COMPLETE',1001,'2026-10-01',1);
			INSERT INTO SalesOrderLines(Id,SalesOrderId,LineNumber,ItemId,PartNumber,Description,Quantity,UnitPrice) VALUES(1003,1002,1,1001,'COMPLETE-ITEM','Complete line',2,12.3456);
			INSERT INTO Inventories(Id,ItemId,PurposeId,StorageLocationId) SELECT 1004,1001,MIN(p.Id),MIN(s.Id) FROM Purposes p CROSS JOIN StorageLocations s;
			INSERT INTO InventoryReservations(Id,SalesOrderLineId,InventoryId,Quantity,CreatedAtUtc,CreatedByUserId) VALUES(1005,1003,1004,2,'2026-10-01T12:00:00.0000000Z',1);
			INSERT INTO Shipments(Id,ShipmentNumber,SalesOrderId,CustomerId,ShipmentDate,CreatedByUserId) VALUES(1006,'SHIP-COMPLETE',1002,1001,'2026-10-01',1);
			INSERT INTO ShipmentLines(Id,ShipmentId,SalesOrderLineId,InventoryReservationId,InventoryId,Quantity) VALUES(1007,1006,1003,1005,1004,2);
			INSERT INTO SalesInvoices(Id,InvoiceNumber,CustomerId,SalesOrderId,ShipmentId,InvoiceDate,DueDate,CreatedByUserId) VALUES(1008,'INV-COMPLETE',1001,1002,1006,'2026-10-01','2026-10-31',1);
			INSERT INTO SalesInvoiceLines(Id,SalesInvoiceId,LineNumber,SalesOrderLineId,ShipmentLineId,PartNumber,Description,Quantity,UnitPrice) VALUES(1009,1008,1,1003,1007,'COMPLETE-ITEM','Complete invoice line',2,12.3456);
			INSERT INTO FinanceLegalEntities(Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES('00000000-0000-0000-0000-000000000001','COMPLETE','Complete legal entity','DE','EUR',1);
			INSERT INTO FinanceJournalEntries(Id,EntryNumber,OperationId,RequestHash,AccountingBookId,JournalId,AccountingPeriodId,PostingDate,PostedAtUtc,PostedByUserId,Description,SourceType,SourceId,SourceEvent,TransactionCurrencyCode,ReportingCurrencyCode,ExchangeRate,EntryKind)
			VALUES(1010,'GL-COMPLETE','00000000-0000-0000-0000-000000000002','hash','book','journal','period','2026-10-01','2026-10-01T12:00:00.0000000Z',1,'Complete ledger','SalesInvoice','1008','Posted','EUR','EUR',1,1);
			INSERT INTO FinanceJournalEntryLines(Id,JournalEntryId,LineNumber,AccountId,TransactionDebit,TransactionCredit,ReportingDebit,ReportingCredit) VALUES(1011,1010,1,'debit',123.456789,0,123.456789,0),(1012,1010,2,'credit',0,123.456789,0,123.456789);
			INSERT INTO Projects(Id,Code,Name,LegalEntityId,OwnerUserId,CustomerId,Status,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId) VALUES(1013,'COMPLETE','Complete project','00000000-0000-0000-0000-000000000001',1,1001,1,'2026-10-01T12:00:00Z',1,'2026-10-01T12:00:00Z',1);
			INSERT INTO UserSessions(SessionId,UserId,StartedUtc,LastSeenUtc,ClientInstanceId,MachineName) VALUES('00000000-0000-0000-0000-000000000003',1,'2026-10-01T12:00:00Z','2026-10-01T12:30:00Z','00000000-0000-0000-0000-000000000004','Complete backup');
			INSERT INTO UserWorkspaceFavorites(UserId,RouteId,PinnedUtc) VALUES(1,'finance.general-ledger','2026-10-01T12:00:00Z');
			""");
		await ExecuteAsync("""
			INSERT INTO SubscriptionContracts(Id,Version,ContractNumber,CustomerId,LegalEntityId,Currency,StartDate,Cadence,NextBillingDate,Status,StatusEffectiveDate,OwnerUserId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId)
			VALUES(1101,3,'SUB-COMPLETE',1001,'00000000-0000-0000-0000-000000000001','EUR','2026-10-01',1,'2026-11-01',2,'2026-10-01',1,'2026-10-01T12:00:00Z',1,'2026-10-01T12:00:00Z',1);
			INSERT INTO SubscriptionContractLines(Id,ContractId,TermsRevision,LineNumber,ItemId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate,PricePolicy,PriceSourceName,PriceSourceCurrency)
			VALUES(1102,1101,1,1,1001,'COMPLETE-ITEM','Complete recurring line',2,12.3456,5.4321,19,1,'Contract snapshot','EUR');
			INSERT INTO SubscriptionContractRevisions(ContractId,TermsRevision,EffectiveFrom,TermsSnapshotJson,CreatedAtUtc,CreatedByUserId)
			VALUES(1101,1,'2026-10-01',@TermsSnapshot,'2026-10-01T12:00:00Z',1);
			INSERT INTO SubscriptionLifecycleEvents(Id,ContractId,Action,EffectiveDate,PreviousStatus,NewStatus,Comment,CreatedAtUtc,CreatedByUserId)
			VALUES(1103,1101,'Activated','2026-10-01',1,2,'Initial activation','2026-10-01T12:00:00Z',1);
			INSERT INTO SalesInvoices(Id,InvoiceNumber,CustomerId,InvoiceDate,DueDate,Currency,CustomerReference,CreatedByUserId)
			VALUES(1104,'INV-SUB-COMPLETE',1001,'2026-10-01','2026-10-31','EUR','SUB-COMPLETE 2026-10-01/2026-10-31',1);
			INSERT INTO SalesInvoiceLines(Id,SalesInvoiceId,LineNumber,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate)
			VALUES(1105,1104,1,'COMPLETE-ITEM','Complete recurring line',2,12.3456,5.4321,19);
			INSERT INTO SubscriptionBillingInstances(Id,Version,ContractId,TermsRevision,PeriodStart,PeriodEnd,BillingDate,Status,SalesInvoiceId,CreatedAtUtc,GeneratedAtUtc,GeneratedByUserId)
			VALUES(1106,3,1101,1,'2026-10-01','2026-10-31','2026-10-01',2,1104,'2026-10-01T12:00:00Z','2026-10-01T12:01:00Z',1);
			INSERT INTO SubscriptionBillingPriceEvidence(Id,BillingInstanceId,ContractLineId,LineNumber,UnitPrice,DiscountPercent,PriceSourceName,PriceSourceCurrency,ResolvedAtUtc)
			VALUES(1107,1106,1102,1,12.3456,5.4321,'Contract snapshot','EUR','2026-10-01T12:01:00Z');
			INSERT INTO SalesLeads(Id,LeadNumber,CompanyName,OwnerUserId,Status,CreatedAtUtc,UpdatedAtUtc,ConvertedCustomerId,ConvertedAtUtc)
			VALUES(1110,'LEAD-COMPLETE','Complete backup customer',1,4,'2026-10-01T12:00:00Z','2026-10-01T12:00:00Z',1001,'2026-10-01T12:00:00Z');
			INSERT INTO SalesOpportunities(Id,OpportunityNumber,CustomerId,LeadId,OwnerUserId,StageId,Currency,ExpectedAmount,CreatedAtUtc,UpdatedAtUtc)
			SELECT 1111,'OPP-COMPLETE',1001,1110,1,MIN(Id),'EUR',1234.56,'2026-10-01T12:00:00Z','2026-10-01T12:00:00Z' FROM SalesOpportunityStages;
			UPDATE SalesLeads SET ConvertedOpportunityId=1111 WHERE Id=1110;
			INSERT INTO StockMovements(Id,InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice)
			VALUES(1120,1004,@OpeningBalance,'2026-10-01T11:00:00Z',2,12.3456);
			INSERT INTO ItemTrackingUnits(Id,ItemId,TrackingMode,Code,CreatedAtUtc)
			VALUES(1121,1001,1,'COMPLETE-SERIAL-1','2026-10-01T11:00:00Z'),(1122,1001,1,'COMPLETE-SERIAL-2','2026-10-01T11:00:00Z');
			INSERT INTO StockMovementTracking(StockMovementId,TrackingUnitId,Quantity) VALUES(1120,1121,1),(1120,1122,1);
			""", ("@TermsSnapshot", SubscriptionTermsSnapshot), ("@OpeningBalance", (int)StockMovementType.OpeningBalance));
		var digest = Convert.ToHexString(SHA256.HashData(AttachmentBytes));
		await ExecuteAsync("""
			INSERT INTO BusinessAttachments(Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,CreatedByUserId,CreatedAtUtc,CurrentRevision,Status,Version)
			VALUES('00000000-0000-0000-0000-000000000005',1,1001,'binary.bin','application/octet-stream',@Length,@Hash,1,'2026-10-01T12:00:00Z',1,1,1);
			INSERT INTO BusinessAttachmentRevisions(AttachmentId,Revision,FileName,MediaType,ByteLength,Sha256,CreatedByUserId,CreatedAtUtc)
			VALUES('00000000-0000-0000-0000-000000000005',1,'binary.bin','application/octet-stream',@Length,@Hash,1,'2026-10-01T12:00:00Z');
			INSERT INTO BusinessAttachmentContents(AttachmentId,Revision,Content) VALUES('00000000-0000-0000-0000-000000000005',1,@Bytes);
			""", ("@Length", AttachmentBytes.Length), ("@Hash", digest), ("@Bytes", AttachmentBytes));
	}

	private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
	{
		await using var connection = _factory.CreateConnection();
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
		await command.ExecuteNonQueryAsync();
	}

	private async Task<object?> ScalarAsync(string sql)
	{
		await using var connection = _factory.CreateConnection();
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return await command.ExecuteScalarAsync();
	}

	private static AuthorizationService CreateAuthorization()
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, Email = "backup-tests@depot.test", DisplayName = "Backup tests", IsActive = true }, [ApplicationPermission.DatabaseManage]);
		return authorization;
	}

	private static async Task<DatabaseBackupManifest> ReadManifestAsync(string path)
	{
		using var archive = ZipFile.OpenRead(path);
		await using var stream = archive.GetEntry("manifest.json")!.Open();
		return (await JsonSerializer.DeserializeAsync<DatabaseBackupManifest>(stream))!;
	}

	private static void AssertTableHashesEqual(DatabaseBackupManifest expected, DatabaseBackupManifest actual)
	{
		Assert.Equal(expected.SchemaFingerprint, actual.SchemaFingerprint);
		Assert.Equal(expected.Tables.Select(table => (table.Schema.Name, table.RowCount, table.Sha256)), actual.Tables.Select(table => (table.Schema.Name, table.RowCount, table.Sha256)));
	}

	private static void MutateArchive(string path, string corruption)
	{
		if (corruption == "metadata-row") { MutateTable(path, "DepotFeatureVersions", (rows, columns) => rows[0]![Array.IndexOf(columns, "Version")] = new JsonArray("integer", "999")); return; }
		if (corruption == "binary-type") { MutateTable(path, "BusinessAttachmentContents", (rows, columns) => rows[0]![Array.IndexOf(columns, "Content")] = new JsonArray("string", "base64-is-not-a-blob")); return; }
		if (corruption == "identity-type") { MutateTable(path, "Items", (rows, columns) => rows[0]![Array.IndexOf(columns, "Id")] = new JsonArray("string", "1001")); return; }
		if (corruption == "feature-name-type") { MutateTable(path, "DepotFeatureVersions", (rows, columns) => rows[0]![Array.IndexOf(columns, "Name")] = new JsonArray("integer", "123")); return; }
		using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
		var manifest = JsonNode.Parse(ReadEntry(archive, "manifest.json"))!;
		switch (corruption)
		{
			case "legacy": manifest["FormatVersion"] = 1; break;
			case "format-type": manifest["FormatVersion"] = "2"; break;
			case "missing-table": archive.GetEntry("tables/FinanceJournalEntries.json")!.Delete(); break;
			case "missing-column": manifest["Tables"]![0]!["Schema"]!["Columns"]!.AsArray().RemoveAt(0); break;
			case "feature-version": manifest["FeatureVersions"]!["Finance"] = 0; break;
			case "checksum": WriteEntry(archive, "tables/Items.json", "[]"); break;
			case "duplicate-entry": using (var stream = archive.CreateEntry("tables/Items.json").Open()) stream.Write(Encoding.UTF8.GetBytes("[]")); break;
			case "unexpected-entry": WriteEntry(archive, "tables/Unknown.json", "[]"); break;
			case "sql-identifier": manifest["Tables"]![0]!["Schema"]!["Name"] = "Items]; DELETE FROM Users;--"; break;
			default: throw new ArgumentOutOfRangeException(nameof(corruption));
		}
		WriteEntry(archive, "manifest.json", manifest.ToJsonString());
	}

	private static void MutateTable(string path, string name, Action<JsonArray, string[]> mutation)
	{
		using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
		var manifest = JsonNode.Parse(ReadEntry(archive, "manifest.json"))!;
		var table = manifest["Tables"]!.AsArray().Single(node => node!["Schema"]!["Name"]!.GetValue<string>() == name)!;
		var columns = table["Schema"]!["Columns"]!.AsArray().Where(node => !node!["IsGenerated"]!.GetValue<bool>()).Select(node => node!["Name"]!.GetValue<string>()).ToArray();
		var rows = JsonNode.Parse(ReadEntry(archive, $"tables/{name}.json"))!.AsArray();
		mutation(rows, columns);
		var json = rows.ToJsonString();
		table["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
		WriteEntry(archive, $"tables/{name}.json", json);
		WriteEntry(archive, "manifest.json", manifest.ToJsonString());
	}

	private static string ReadEntry(ZipArchive archive, string name)
	{
		using var reader = new StreamReader(archive.GetEntry(name)!.Open());
		return reader.ReadToEnd();
	}

	private static void WriteEntry(ZipArchive archive, string name, string text)
	{
		archive.GetEntry(name)?.Delete();
		using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
		writer.Write(text);
	}

	private sealed class InlineProgress(Action<DatabaseOperationProgress> report) : IProgress<DatabaseOperationProgress>
	{
		public void Report(DatabaseOperationProgress value) => report(value);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}
}

