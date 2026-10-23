// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class DatabaseBackupProviderAcceptanceTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerRestoresCompleteTypedSnapshotAndRollsBackCancellation() => VerifyProviderAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbRestoresCompleteTypedSnapshotAndRollsBackCancellation() => VerifyProviderAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlRestoresCompleteTypedSnapshotAndRollsBackCancellation() => VerifyProviderAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public async Task SqlServerPreservesDeletedIdentityHistoryWhenRestoringIntoNeverUsedTable()
	{
		var factory = new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings());
		DatabaseProvisioningService.Initialize(factory);
		var directory = Path.Combine(Path.GetTempPath(), $"depot-backup-identity-{Guid.NewGuid():N}");
		var service = CreateManagementService(factory, directory);
		var data = new DatabaseAccess(factory);
		var table = $"BackupIdentityProbe_{Guid.NewGuid():N}";
		var createSql = $"CREATE TABLE [dbo].[{table}] (Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, Label NVARCHAR(40) NOT NULL);";
		string? baseline = null;
		try
		{
			baseline = (await service.CreateBackupAsync(Path.Combine(directory, "baseline.depotbackup"))).FilePath;
			foreach (var sourceHighWater in new long[] { 1, 50000 })
			{
				await data.ExecuteAsync(createSql, CancellationToken.None);
				var lastId = await data.InsertAsync($"INSERT INTO [dbo].[{table}] (Label) VALUES ('Deleted source record');", CancellationToken.None);
				if (sourceHighWater > 1)
				{
					await data.ExecuteAsync($"DBCC CHECKIDENT (N'dbo.{table}', RESEED, {sourceHighWater - 1}) WITH NO_INFOMSGS;", CancellationToken.None);
					lastId = await data.InsertAsync($"INSERT INTO [dbo].[{table}] (Label) VALUES ('Deleted source record');", CancellationToken.None);
				}
				Assert.Equal(sourceHighWater, lastId);
				await data.ExecuteAsync($"DELETE FROM [dbo].[{table}];", CancellationToken.None);
				var source = await service.CreateBackupAsync(Path.Combine(directory, $"deleted-{sourceHighWater}.depotbackup"));
				await data.ExecuteAsync($"DROP TABLE [dbo].[{table}];", CancellationToken.None);
				await data.ExecuteAsync(createSql, CancellationToken.None);
				Assert.Equal(1L, await ScalarAsync(data, $"SELECT COUNT(*) FROM sys.identity_columns WHERE object_id=OBJECT_ID(N'dbo.{table}') AND last_value IS NULL;"));

				await service.RestoreBackupAsync(source.FilePath);
				Assert.Equal(0L, await ScalarAsync(data, $"SELECT COUNT(*) FROM [dbo].[{table}];"));
				var nextId = await data.InsertAsync($"INSERT INTO [dbo].[{table}] (Label) VALUES ('After restore');", CancellationToken.None);
				Assert.True(nextId > sourceHighWater, $"Restoring an empty table must not reuse a previously issued ID: {nextId} <= {sourceHighWater}.");
				await data.ExecuteAsync($"DROP TABLE [dbo].[{table}];", CancellationToken.None);
			}
		}
		finally
		{
			await data.ExecuteAsync($"DROP TABLE IF EXISTS [dbo].[{table}];", CancellationToken.None);
			if (baseline is not null) await service.RestoreBackupAsync(baseline);
			Directory.Delete(directory, recursive: true);
		}
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbRejectsUnpreservableIdentityHistoryBeforeChangingTarget() => VerifyMySqlIdentityRejectionAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlRejectsUnpreservableIdentityHistoryBeforeChangingTarget() => VerifyMySqlIdentityRejectionAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyProviderAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var directory = Path.Combine(Path.GetTempPath(), $"depot-backup-provider-{Guid.NewGuid():N}");
		var service = CreateManagementService(factory, directory);
		var data = new DatabaseAccess(factory);
		string? baseline = null;
		try
		{
			baseline = (await service.CreateBackupAsync(Path.Combine(directory, "baseline.depotbackup"))).FilePath;
			var seed = await SeedModernRecordsAsync(data);
			var snapshot = await service.CreateBackupAsync(Path.Combine(directory, "seeded.depotbackup"));
			Assert.True(snapshot.IsValid, snapshot.ValidationMessage);
			Assert.Equal(factory.Provider, snapshot.Provider);
			AssertManifestIncludesModernSchema(snapshot.FilePath);

			await data.ExecuteAsync("UPDATE Items SET Description=$Description, NetWeightKg=$Weight WHERE Id=$Id;", CancellationToken.None,
				new DatabaseParameter("$Description", "Changed after backup"), new DatabaseParameter("$Weight", 9.876543m), new DatabaseParameter("$Id", seed.ItemId));
			await data.ExecuteAsync("UPDATE BusinessAttachmentContents SET Content=$Content WHERE AttachmentId=$Id;", CancellationToken.None,
				new DatabaseParameter("$Content", new byte[] { 9, 8, 7 }), new DatabaseParameter("$Id", seed.AttachmentId));
			var laterItem = await data.InsertAsync("INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($Number,'Created after backup',1);", CancellationToken.None,
				new DatabaseParameter("$Number", $"AFTER-BACKUP-{Guid.NewGuid():N}"));
			var beforeCancellation = await service.CreateBackupAsync(Path.Combine(directory, "before-cancellation.depotbackup"));

			using (var cancellation = new CancellationTokenSource())
			{
				var reachedReplacement = false;
				var progress = new InlineProgress(value =>
				{
					if (value.Message.StartsWith("Restoring ", StringComparison.Ordinal))
					{
						reachedReplacement = true;
						cancellation.Cancel();
					}
				});
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RestoreBackupAsync(snapshot.FilePath, progress, cancellation.Token));
				Assert.True(reachedReplacement, "Cancellation must happen after the replacement transaction has deleted current records.");
			}
			var afterCancellation = await service.CreateBackupAsync(Path.Combine(directory, "after-cancellation.depotbackup"));
			Assert.Equal(ReadTableSignatures(beforeCancellation.FilePath), ReadTableSignatures(afterCancellation.FilePath));
			await AssertForeignKeysEnforcedAsync(data);

			var safetyPath = await service.RestoreBackupAsync(snapshot.FilePath);
			Assert.True(File.Exists(safetyPath));
			Assert.Equal(ReadTableSignatures(beforeCancellation.FilePath), ReadTableSignatures(safetyPath));
			Assert.Equal(0L, await ScalarAsync(data, "SELECT COUNT(*) FROM Items WHERE Id=$Id;", new DatabaseParameter("$Id", laterItem)));
			Assert.Equal("Backup provider item", await data.ExecuteScalarAsync("SELECT Description FROM Items WHERE Id=$Id;", CancellationToken.None,
				new DatabaseParameter("$Id", seed.ItemId)));
			Assert.Equal(0.123456m, Convert.ToDecimal(await data.ExecuteScalarAsync("SELECT NetWeightKg FROM Items WHERE Id=$Id;", CancellationToken.None,
				new DatabaseParameter("$Id", seed.ItemId)), CultureInfo.InvariantCulture));
			Assert.Equal(seed.Content, Assert.IsType<byte[]>(await data.ExecuteScalarAsync(
				"SELECT Content FROM BusinessAttachmentContents WHERE AttachmentId=$Id AND Revision=1;", CancellationToken.None, new DatabaseParameter("$Id", seed.AttachmentId))));
			Assert.Equal(seed.Timestamp, Assert.IsType<DateTime>(await data.ExecuteScalarAsync(
				"SELECT CreatedAtUtc FROM BusinessAttachments WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", seed.AttachmentId))));
			Assert.Equal(seed.OpportunityId, await ScalarAsync(data, "SELECT ConvertedOpportunityId FROM SalesLeads WHERE Id=$Id;", new DatabaseParameter("$Id", seed.LeadId)));
			Assert.Equal(3L, await ScalarAsync(data, "SELECT SUM(mt.Quantity) FROM StockMovementTracking mt INNER JOIN StockMovements sm ON sm.Id=mt.StockMovementId WHERE sm.InventoryId=$Id;",
				new DatabaseParameter("$Id", seed.InventoryId)));
			if (factory.Provider == DatabaseProvider.MySql)
				Assert.Equal(seed.InventoryId, await ScalarAsync(data, "SELECT ActiveInventoryId FROM InventoryReservations WHERE Id=$Id;", new DatabaseParameter("$Id", seed.ReservationId)));
			await AssertForeignKeysEnforcedAsync(data);

			var roundTrip = await service.CreateBackupAsync(Path.Combine(directory, "round-trip.depotbackup"));
			Assert.Equal(ReadTableSignatures(snapshot.FilePath), ReadTableSignatures(roundTrip.FilePath));
		}
		finally
		{
			// Other acceptance tests share this isolated provider database. Restore every
			// original row, including feature metadata, before another test can run.
			if (baseline is not null) await service.RestoreBackupAsync(baseline);
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task VerifyMySqlIdentityRejectionAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var directory = Path.Combine(Path.GetTempPath(), $"depot-backup-identity-{Guid.NewGuid():N}");
		var service = CreateManagementService(factory, directory);
		var data = new DatabaseAccess(factory);
		var table = $"BackupIdentityProbe_{Guid.NewGuid():N}";
		var createSql = $"CREATE TABLE `{table}` (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Label VARCHAR(40) NOT NULL) ENGINE=InnoDB;";
		string? baseline = null;
		try
		{
			baseline = (await service.CreateBackupAsync(Path.Combine(directory, "baseline.depotbackup"))).FilePath;
			await data.ExecuteAsync(createSql, CancellationToken.None);
			await data.ExecuteAsync($"INSERT INTO `{table}` (Id,Label) VALUES (50000,'Deleted source record'); DELETE FROM `{table}`;", CancellationToken.None);
			var source = await service.CreateBackupAsync(Path.Combine(directory, "deleted-identity.depotbackup"));
			await data.ExecuteAsync($"DROP TABLE `{table}`;", CancellationToken.None);
			await data.ExecuteAsync(createSql, CancellationToken.None);
			var targetId = await data.InsertAsync($"INSERT INTO `{table}` (Label) VALUES ('Target record must survive');", CancellationToken.None);
			Assert.Equal(1L, targetId);
			var beforeRestore = await service.CreateBackupAsync(Path.Combine(directory, "before-rejection.depotbackup"));

			var validation = await service.ValidateBackupAsync(source.FilePath);
			Assert.False(validation.IsValid);
			Assert.Contains("identity counter", validation.ValidationMessage, StringComparison.OrdinalIgnoreCase);
			await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreBackupAsync(source.FilePath));
			Assert.Equal("Target record must survive", await data.ExecuteScalarAsync($"SELECT Label FROM `{table}` WHERE Id=1;", CancellationToken.None));
			var afterRestore = await service.CreateBackupAsync(Path.Combine(directory, "after-rejection.depotbackup"));
			Assert.Equal(ReadTableSignatures(beforeRestore.FilePath), ReadTableSignatures(afterRestore.FilePath));
			await AssertForeignKeysEnforcedAsync(data);
		}
		finally
		{
			await data.ExecuteAsync($"DROP TABLE IF EXISTS `{table}`;", CancellationToken.None);
			if (baseline is not null) await service.RestoreBackupAsync(baseline);
			Directory.Delete(directory, recursive: true);
		}
	}

	private static DatabaseManagementService CreateManagementService(IDatabaseConnectionFactory factory, string directory)
	{
		Directory.CreateDirectory(directory);
		var settings = new SettingsService(new SettingsRepository(Path.Combine(directory, "depot.settings")));
		settings.LoadOrCreate();
		// The dedicated provider factory owns the CI transport configuration. These
		// local settings supply backup paths, without changing its connection options.
		settings.Save(new DatabaseConnectionSettings
		{
			LocalDatabasePath = Path.Combine(directory, "unused.db"),
			BackupDirectory = directory
		});
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, IsActive = true }, [ApplicationPermission.DatabaseManage]);
		return new DatabaseManagementService(factory, settings, authorization);
	}

	private static async Task<SeedRecords> SeedModernRecordsAsync(DatabaseAccess data)
	{
		var suffix = Guid.NewGuid().ToString("N");
		var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1234560);
		var timestampText = timestamp.ToString("O", CultureInfo.InvariantCulture);
		var userId = await ScalarAsync(data, "SELECT MIN(Id) FROM Users;");
		var itemId = await data.InsertAsync(
			"INSERT INTO Items (PartNumber,Description,IsActive,Revision,TrackingMode,NetWeightKg,ContainsBattery) VALUES ($Number,'Backup provider item',1,'R2',2,$Weight,1);", CancellationToken.None,
			new DatabaseParameter("$Number", $"BACKUP-{suffix}"), new DatabaseParameter("$Weight", 0.123456m));
		var inventoryId = await data.InsertAsync(
			"INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($ItemId,$PurposeId,$LocationId,1);", CancellationToken.None,
			new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$PurposeId", await ScalarAsync(data, "SELECT MIN(Id) FROM Purposes;")),
			new DatabaseParameter("$LocationId", await ScalarAsync(data, "SELECT MIN(Id) FROM StorageLocations;")));
		var movementId = await data.InsertAsync(
			"INSERT INTO StockMovements (InventoryId,MovementType,TimestampUtc,Quantity,UnitPrice) VALUES ($InventoryId,$Type,$Timestamp,3,$Price);", CancellationToken.None,
			new DatabaseParameter("$InventoryId", inventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance),
			new DatabaseParameter("$Timestamp", timestampText), new DatabaseParameter("$Price", 12.3456m));
		var unitId = await data.InsertAsync(
			"INSERT INTO ItemTrackingUnits (ItemId,TrackingMode,Code,CreatedAtUtc) VALUES ($ItemId,2,'BACKUP-LOT',$CreatedAtUtc);", CancellationToken.None,
			new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$CreatedAtUtc", timestamp));
		await data.ExecuteAsync("INSERT INTO StockMovementTracking (StockMovementId,TrackingUnitId,Quantity) VALUES ($MovementId,$UnitId,3);", CancellationToken.None,
			new DatabaseParameter("$MovementId", movementId), new DatabaseParameter("$UnitId", unitId));

		var customerId = await data.InsertAsync("INSERT INTO Customers (CustomerNumber,Name) VALUES ($Number,'Backup customer');", CancellationToken.None,
			new DatabaseParameter("$Number", $"BACKUP-{suffix}"));
		var orderId = await data.InsertAsync("INSERT INTO SalesOrders (OrderNumber,CustomerId,OrderDate,Status) VALUES ($Number,$CustomerId,$Date,4);", CancellationToken.None,
			new DatabaseParameter("$Number", $"BACKUP-{suffix}"), new DatabaseParameter("$CustomerId", customerId), new DatabaseParameter("$Date", timestamp.Date));
		var lineId = await data.InsertAsync(
			"INSERT INTO SalesOrderLines (SalesOrderId,LineNumber,ItemId,PartNumber,Description,Quantity,UnitPrice,ReservedQuantity) VALUES ($OrderId,1,$ItemId,$Number,'Backup provider item',1,$Price,1);", CancellationToken.None,
			new DatabaseParameter("$OrderId", orderId), new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$Number", $"BACKUP-{suffix}"), new DatabaseParameter("$Price", 123.4567m));
		var reservationId = await data.InsertAsync(
			"INSERT INTO InventoryReservations (SalesOrderLineId,InventoryId,Quantity,Status,CreatedAtUtc,CreatedByUserId) VALUES ($LineId,$InventoryId,1,1,$At,$UserId);", CancellationToken.None,
			new DatabaseParameter("$LineId", lineId), new DatabaseParameter("$InventoryId", inventoryId), new DatabaseParameter("$At", timestampText), new DatabaseParameter("$UserId", userId));
		var leadId = await data.InsertAsync(
			"INSERT INTO SalesLeads (LeadNumber,CompanyName,OwnerUserId,Status,CreatedAtUtc,UpdatedAtUtc,ConvertedCustomerId) VALUES ($Number,'Backup customer',$UserId,4,$At,$At,$CustomerId);", CancellationToken.None,
			new DatabaseParameter("$Number", $"BL-{suffix}"), new DatabaseParameter("$UserId", userId), new DatabaseParameter("$At", timestamp), new DatabaseParameter("$CustomerId", customerId));
		var opportunityId = await data.InsertAsync(
			"INSERT INTO SalesOpportunities (OpportunityNumber,CustomerId,LeadId,OwnerUserId,StageId,Currency,ExpectedAmount,CreatedAtUtc,UpdatedAtUtc) VALUES ($Number,$CustomerId,$LeadId,$UserId,$StageId,'EUR',$Amount,$At,$At);", CancellationToken.None,
			new DatabaseParameter("$Number", $"BO-{suffix}"), new DatabaseParameter("$CustomerId", customerId), new DatabaseParameter("$LeadId", leadId),
			new DatabaseParameter("$UserId", userId), new DatabaseParameter("$StageId", await ScalarAsync(data, "SELECT MIN(Id) FROM SalesOpportunityStages;")),
			new DatabaseParameter("$Amount", 123456.78m), new DatabaseParameter("$At", timestamp));
		await data.ExecuteAsync("UPDATE SalesLeads SET ConvertedOpportunityId=$OpportunityId WHERE Id=$Id;", CancellationToken.None,
			new DatabaseParameter("$OpportunityId", opportunityId), new DatabaseParameter("$Id", leadId));

		var attachmentId = Guid.NewGuid().ToString("D");
		var content = Enumerable.Range(0, 1024).Select(index => (byte)(index % 256)).ToArray();
		var hash = Convert.ToHexString(SHA256.HashData(content));
		await data.ExecuteAsync(
			"INSERT INTO BusinessAttachments (Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,CreatedAtUtc,CurrentRevision,Status,Version) VALUES ($Id,3,$EntityId,'backup.bin','application/octet-stream',$Length,$Hash,$At,1,1,1);", CancellationToken.None,
			new DatabaseParameter("$Id", attachmentId), new DatabaseParameter("$EntityId", itemId), new DatabaseParameter("$Length", content.Length),
			new DatabaseParameter("$Hash", hash), new DatabaseParameter("$At", timestamp));
		await data.ExecuteAsync(
			"INSERT INTO BusinessAttachmentRevisions (AttachmentId,Revision,FileName,MediaType,ByteLength,Sha256,CreatedAtUtc) VALUES ($Id,1,'backup.bin','application/octet-stream',$Length,$Hash,$At);", CancellationToken.None,
			new DatabaseParameter("$Id", attachmentId), new DatabaseParameter("$Length", content.Length), new DatabaseParameter("$Hash", hash), new DatabaseParameter("$At", timestamp));
		await data.ExecuteAsync("INSERT INTO BusinessAttachmentContents (AttachmentId,Revision,Content) VALUES ($Id,1,$Content);", CancellationToken.None,
			new DatabaseParameter("$Id", attachmentId), new DatabaseParameter("$Content", content));
		return new(itemId, inventoryId, reservationId, leadId, opportunityId, attachmentId, timestamp, content);
	}

	private static async Task AssertForeignKeysEnforcedAsync(DatabaseAccess data)
	{
		await Assert.ThrowsAnyAsync<DbException>(() => data.ExecuteAsync(
			"INSERT INTO BusinessAttachmentContents (AttachmentId,Revision,Content) VALUES ($MissingId,1,$Content);", CancellationToken.None,
			new DatabaseParameter("$MissingId", Guid.NewGuid().ToString("D")), new DatabaseParameter("$Content", new byte[] { 1 })));
	}

	private static void AssertManifestIncludesModernSchema(string path)
	{
		using var archive = ZipFile.OpenRead(path);
		using var source = archive.GetEntry("manifest.json")!.Open();
		using var manifest = JsonDocument.Parse(source);
		var root = manifest.RootElement;
		Assert.Equal(2, root.GetProperty("FormatVersion").GetInt32());
		var names = root.GetProperty("Tables").EnumerateArray().Select(table => table.GetProperty("Schema").GetProperty("Name").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var table in new[] { "DatabaseInfo", "DepotFeatureVersions", "BusinessAttachments", "BusinessAttachmentRevisions", "BusinessAttachmentContents", "ItemTrackingUnits", "StockMovementTracking", "SalesLeads", "SalesOpportunities", "InventoryReservations", "CompanyProfile", "FinanceJournalEntries", "ProductionOrders", "ServiceCases" })
			Assert.True(names.Contains(table), $"The complete backup must include {table}.");
		var metadata = root.GetProperty("FeatureVersions");
		Assert.Equal(SalesSchemaMigration.CurrentVersion, metadata.GetProperty("Sales").GetInt32());
		Assert.Equal(FinanceInventoryAccountingSchemaMigration.CurrentVersion, metadata.GetProperty("Finance").GetInt32());
		Assert.Equal(ServiceManagementSchemaMigration.CurrentVersion, metadata.GetProperty("ServiceManagement").GetInt32());
	}

	private static string[] ReadTableSignatures(string path)
	{
		using var archive = ZipFile.OpenRead(path);
		using var source = archive.GetEntry("manifest.json")!.Open();
		using var manifest = JsonDocument.Parse(source);
		return manifest.RootElement.GetProperty("Tables").EnumerateArray()
			.Select(table => $"{table.GetProperty("Schema").GetProperty("Name").GetString()}:{table.GetProperty("RowCount").GetInt64()}:{table.GetProperty("Sha256").GetString()}")
			.OrderBy(value => value, StringComparer.Ordinal).ToArray();
	}

	private static async Task<long> ScalarAsync(DatabaseAccess data, string sql, params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await data.ExecuteScalarAsync(sql, CancellationToken.None, parameters), CultureInfo.InvariantCulture);

	private sealed record SeedRecords(long ItemId, long InventoryId, long ReservationId, long LeadId, long OpportunityId, string AttachmentId, DateTime Timestamp, byte[] Content);

	private sealed class InlineProgress(Action<DatabaseOperationProgress> report) : IProgress<DatabaseOperationProgress>
	{
		public void Report(DatabaseOperationProgress value) => report(value);
	}
}
