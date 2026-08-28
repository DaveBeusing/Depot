// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceInventoryAccountingTests
{
	[Fact]
	public void FinanceMigrationCreatesInventoryAccountingSchemaVersionSix()
	{
		using var context = TestContext.Create();
		Assert.Equal(6L, context.Scalar("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceInventoryValuationLayers';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceInventoryPurchaseVariances';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceInventoryLandedCostOperations';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceInventoryReconciliationRuns';"));
	}

	[Fact]
	public void FinanceRoleIncludesInventoryAccountingWorkspacePermissions()
	{
		var role = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.FinanceInventoryAccountingView, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceInventoryAccountingManage, role.Permissions);
		Assert.Equal("FinanceInventoryAccounting.View", PermissionCatalog.Code(ApplicationPermission.FinanceInventoryAccountingView));
		Assert.Equal("FinanceInventoryAccounting.Manage", PermissionCatalog.Code(ApplicationPermission.FinanceInventoryAccountingManage));
	}

	[Fact]
	public void F4RecordsAreClassifiedAsRetainedAccountingEvidence()
	{
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceInventoryAccountingEvent)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceInventoryPurchaseVariance)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceInventoryLandedCostOperation)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AuditEvidence, BusinessRecordCatalog.Require(nameof(FinanceInventoryReconciliationRun)).RetentionCategory);
	}

	[Fact]
	public async Task HistoricalValuationReconstructsConsumptionAndLaterReversalAsOfDate()
	{
		using var context = TestContext.Create();
		var bookId = Guid.NewGuid();
		var journalId = context.InsertJournal(bookId, new DateOnly(2026, 1, 10));
		var layerId = context.InsertLayer(bookId, journalId, 100, 10, 2, 5m, new DateOnly(2026, 1, 10));
		context.InsertConsumption(layerId, 200, 8, 5m, new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));

		var repository = new FinanceInventoryCostingRepository(context.Database);
		var runner = new DatabaseTransactionRunner(context.Database);
		var beforeConsumption = await runner.ExecuteAsync((transaction, token) => repository.GetValuationReportingRowsAsync(transaction, bookId, new DateOnly(2026, 2, 28), token));
		var whileConsumed = await runner.ExecuteAsync((transaction, token) => repository.GetValuationReportingRowsAsync(transaction, bookId, new DateOnly(2026, 3, 15), token));
		var afterReversal = await runner.ExecuteAsync((transaction, token) => repository.GetValuationReportingRowsAsync(transaction, bookId, new DateOnly(2026, 4, 15), token));

		Assert.Equal(10, Assert.Single(beforeConsumption).Quantity);
		Assert.Equal(2, Assert.Single(whileConsumed).Quantity);
		Assert.Equal(10, Assert.Single(afterReversal).Quantity);
	}

	private sealed class TestContext : IDisposable
	{
		private readonly string _path;
		private TestContext(string path, DatabaseAccess database) { _path = path; Database = database; }
		public DatabaseAccess Database { get; }

		public static TestContext Create()
		{
			var path = Path.Combine(Path.GetTempPath(), $"depot-finance-f4-{Guid.NewGuid():N}.db");
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			return new TestContext(path, new DatabaseAccess(factory));
		}

		public long Scalar(string sql)
		{
			using var connection = new SqliteConnection($"Data Source={_path}");
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
		}

		public long InsertJournal(Guid bookId, DateOnly postingDate)
		{
			using var connection = Open();
			using var command = connection.CreateCommand();
			command.CommandText = "INSERT INTO FinanceJournalEntries (EntryNumber,OperationId,RequestHash,AccountingBookId,JournalId,AccountingPeriodId,PostingDate,PostedAtUtc,PostedByUserId,Description,SourceType,SourceId,SourceEvent,SourceReference,TransactionCurrencyCode,ReportingCurrencyCode,ExchangeRateId,ExchangeRate,EntryKind,ReversalOfEntryId) VALUES ($Number,$Operation,$Hash,$Book,$Journal,$Period,$Date,$At,NULL,'Receipt','InventoryAccounting','100','GoodsReceipt',NULL,'USD','USD',NULL,1,0,NULL); SELECT last_insert_rowid();";
			command.Parameters.AddWithValue("$Number", $"TEST-{Guid.NewGuid():N}");
			command.Parameters.AddWithValue("$Operation", Guid.NewGuid().ToString("D"));
			command.Parameters.AddWithValue("$Hash", new string('A', 64));
			command.Parameters.AddWithValue("$Book", bookId.ToString("D"));
			command.Parameters.AddWithValue("$Journal", Guid.NewGuid().ToString("D"));
			command.Parameters.AddWithValue("$Period", Guid.NewGuid().ToString("D"));
			command.Parameters.AddWithValue("$Date", postingDate.ToString("yyyy-MM-dd"));
			command.Parameters.AddWithValue("$At", postingDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc).ToString("O"));
			return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
		}

		public long InsertLayer(Guid bookId, long journalId, long movementId, int originalQuantity, int remainingQuantity, decimal unitCost, DateOnly acquiredDate)
		{
			using var connection = Open();
			using var transaction = connection.BeginTransaction();
			using var layer = connection.CreateCommand();
			layer.Transaction = transaction;
			layer.CommandText = "INSERT INTO FinanceInventoryValuationLayers (AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId) VALUES ($Book,42,$Movement,$Date,'USD',$Original,$Remaining,$Cost,$At,1); SELECT last_insert_rowid();";
			layer.Parameters.AddWithValue("$Book", bookId.ToString("D"));
			layer.Parameters.AddWithValue("$Movement", movementId);
			layer.Parameters.AddWithValue("$Date", acquiredDate.ToString("yyyy-MM-dd"));
			layer.Parameters.AddWithValue("$Original", originalQuantity);
			layer.Parameters.AddWithValue("$Remaining", remainingQuantity);
			layer.Parameters.AddWithValue("$Cost", unitCost);
			layer.Parameters.AddWithValue("$At", acquiredDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc).ToString("O"));
			var id = Convert.ToInt64(layer.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
			using var accountingEvent = connection.CreateCommand();
			accountingEvent.Transaction = transaction;
			accountingEvent.CommandText = "INSERT INTO FinanceInventoryAccountingEvents (MovementId,Kind,AccountingBookId,ItemId,Quantity,CurrencyCode,Amount,JournalEntryId,OperationId,ReversalOfMovementId,CreatedAtUtc,CreatedByUserId) VALUES ($Movement,1,$Book,42,$Quantity,'USD',$Amount,$Journal,$Operation,NULL,$At,1);";
			accountingEvent.Parameters.AddWithValue("$Movement", movementId);
			accountingEvent.Parameters.AddWithValue("$Book", bookId.ToString("D"));
			accountingEvent.Parameters.AddWithValue("$Quantity", originalQuantity);
			accountingEvent.Parameters.AddWithValue("$Amount", originalQuantity * unitCost);
			accountingEvent.Parameters.AddWithValue("$Journal", journalId);
			accountingEvent.Parameters.AddWithValue("$Operation", Guid.NewGuid().ToString("D"));
			accountingEvent.Parameters.AddWithValue("$At", acquiredDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc).ToString("O"));
			accountingEvent.ExecuteNonQuery();
			transaction.Commit();
			return id;
		}

		public void InsertConsumption(long layerId, long movementId, int quantity, decimal unitCost, DateTime createdAt, DateTime reversedAt)
		{
			using var connection = Open();
			using var command = connection.CreateCommand();
			command.CommandText = "INSERT INTO FinanceInventoryValuationConsumptions (MovementId,LayerId,Quantity,UnitCost,Amount,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId) VALUES ($Movement,$Layer,$Quantity,$Cost,$Amount,$Created,1,$Reversed,1);";
			command.Parameters.AddWithValue("$Movement", movementId);
			command.Parameters.AddWithValue("$Layer", layerId);
			command.Parameters.AddWithValue("$Quantity", quantity);
			command.Parameters.AddWithValue("$Cost", unitCost);
			command.Parameters.AddWithValue("$Amount", quantity * unitCost);
			command.Parameters.AddWithValue("$Created", createdAt.ToString("O"));
			command.Parameters.AddWithValue("$Reversed", reversedAt.ToString("O"));
			command.ExecuteNonQuery();
		}

		private SqliteConnection Open()
		{
			var connection = new SqliteConnection($"Data Source={_path}");
			connection.Open();
			return connection;
		}

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			try { File.Delete(_path); } catch (IOException) { }
		}
	}
}
