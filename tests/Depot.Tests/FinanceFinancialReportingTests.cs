// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceFinancialReportingTests
{
	[Fact]
	public void CurrentFinanceMigrationCreatesReportingSchemaVersionEight()
	{
		using var context = TestContext.Create();
		Assert.Equal(FinanceInventoryAccountingSchemaMigration.CurrentVersion, context.Scalar("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceReportingAccountMappings';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceReportSnapshots';"));
	}

	[Fact]
	public void FinanceRoleIncludesFinancialReportingPermissions()
	{
		var role = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.FinanceFinancialReportingView, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceFinancialReportingManage, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceFinancialReportingExport, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceReportSnapshotsCreate, role.Permissions);
		Assert.Equal("FinanceFinancialReporting.View", PermissionCatalog.Code(ApplicationPermission.FinanceFinancialReportingView));
		Assert.Equal("FinanceReportSnapshots.Create", PermissionCatalog.Code(ApplicationPermission.FinanceReportSnapshotsCreate));
	}

	[Fact]
	public void ReportSnapshotsAreRetainedAuditEvidence()
	{
		var record = BusinessRecordCatalog.Require(nameof(FinanceReportSnapshot));
		Assert.Equal(BusinessRecordRetentionCategory.AuditEvidence, record.RetentionCategory);
		Assert.True(record.HistoricalSnapshotRequired);
	}

	[Fact]
	public async Task TrialBalanceUsesLedgerReportingCurrencyAndHonorsCutoff()
	{
		using var context = TestContext.Create();
		await context.PostAsync(new DateOnly(2026, 1, 10), 100m, "JAN");
		await context.PostAsync(new DateOnly(2026, 2, 10), 50m, "FEB");

		var result = await context.Reporting.GenerateAsync(new FinanceReportParameters
		{
			Kind = FinanceReportKind.TrialBalance,
			AccountingBookId = context.BookId,
			ToDate = new DateOnly(2026, 1, 31),
			IncludeZeroBalances = false
		});

		Assert.Equal("USD", result.ReportingCurrency.Value);
		Assert.Equal(2, result.Rows.Count);
		var cash = result.Rows.Single(row => row.AccountNumber == "1000");
		var revenue = result.Rows.Single(row => row.AccountNumber == "4000");
		Assert.Equal(100m, cash.Debit);
		Assert.Equal(0m, cash.Credit);
		Assert.Equal(100m, cash.Balance);
		Assert.Equal(0m, revenue.Debit);
		Assert.Equal(100m, revenue.Credit);
		Assert.Equal(-100m, revenue.Balance);
	}

	[Fact]
	public async Task CashFlowRequiresAndUsesExplicitCounterpartClassification()
	{
		using var context = TestContext.Create();
		await context.Reporting.SaveMappingAsync(new FinanceReportingAccountMapping
		{
			AccountingBookId = context.BookId,
			AccountId = context.CashAccountId,
			StatementSection = FinanceStatementSection.CurrentAssets,
			IsCashAccount = true,
			SortOrder = 10
		});
		await context.Reporting.SaveMappingAsync(new FinanceReportingAccountMapping
		{
			AccountingBookId = context.BookId,
			AccountId = context.RevenueAccountId,
			StatementSection = FinanceStatementSection.Revenue,
			CashFlowCategory = FinanceCashFlowCategory.Operating,
			SortOrder = 20
		});
		await context.PostAsync(new DateOnly(2026, 1, 10), 125m, "CASHFLOW");

		var result = await context.Reporting.GenerateAsync(new FinanceReportParameters
		{
			Kind = FinanceReportKind.CashFlow,
			AccountingBookId = context.BookId,
			FromDate = new DateOnly(2026, 1, 1),
			ToDate = new DateOnly(2026, 1, 31)
		});

		var row = Assert.Single(result.Rows);
		Assert.Equal("Operating", row.Group);
		Assert.Equal("4000", row.AccountNumber);
		Assert.Equal(125m, row.Amount);
		Assert.Empty(result.Warnings);
	}

	[Fact]
	public async Task SnapshotIsIdempotentContentBoundAndCsvExportIsDeterministic()
	{
		using var context = TestContext.Create();
		await context.PostAsync(new DateOnly(2026, 1, 10), 75m, "SNAPSHOT");
		var result = await context.Reporting.GenerateAsync(new FinanceReportParameters
		{
			Kind = FinanceReportKind.TrialBalance,
			AccountingBookId = context.BookId,
			ToDate = new DateOnly(2026, 1, 31),
			IncludeZeroBalances = false
		});
		var firstCsv = context.Reporting.ExportCsv(result);
		var secondCsv = context.Reporting.ExportCsv(result);
		Assert.Equal(firstCsv, secondCsv);
		Assert.Contains("Key,Group,Label,Currency", firstCsv, StringComparison.Ordinal);

		var operationId = Guid.NewGuid();
		var first = await context.Reporting.CreateSnapshotAsync(operationId, result);
		var retry = await context.Reporting.CreateSnapshotAsync(operationId, result);
		Assert.Equal(first.Id, retry.Id);
		Assert.Equal(first.ParameterHash, retry.ParameterHash);
		Assert.Equal(first.ContentHash, retry.ContentHash);
		Assert.Equal(firstCsv, first.ContentCsv);

		var altered = result with { Rows = result.Rows.Select((row, index) => index == 0 ? row with { Amount = row.Amount + 1m } : row).ToArray() };
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Reporting.CreateSnapshotAsync(operationId, altered));
	}

	private sealed class TestContext : IDisposable
	{
		private readonly string _path;
		private readonly DatabaseAccess _database;
		private readonly FinanceGeneralLedgerService _generalLedger;
		private readonly Guid _periodId;
		private readonly Guid _journalId;

		private TestContext(string path, DatabaseAccess database, FinanceGeneralLedgerService generalLedger, FinanceFinancialReportingService reporting, Guid bookId, Guid periodId, Guid journalId, Guid cashAccountId, Guid revenueAccountId)
		{
			_path = path;
			_database = database;
			_generalLedger = generalLedger;
			Reporting = reporting;
			BookId = bookId;
			_periodId = periodId;
			_journalId = journalId;
			CashAccountId = cashAccountId;
			RevenueAccountId = revenueAccountId;
		}

		public FinanceFinancialReportingService Reporting { get; }
		public Guid BookId { get; }
		public Guid CashAccountId { get; }
		public Guid RevenueAccountId { get; }

		public static TestContext Create()
		{
			var path = Path.Combine(Path.GetTempPath(), $"depot-finance-f6-{Guid.NewGuid():N}.db");
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			var database = new DatabaseAccess(factory);

			var legalEntityId = Guid.NewGuid();
			var calendarId = Guid.NewGuid();
			var periodId = Guid.NewGuid();
			var chartId = Guid.NewGuid();
			var cashAccountId = Guid.NewGuid();
			var revenueAccountId = Guid.NewGuid();
			var bookId = Guid.NewGuid();
			var journalId = Guid.NewGuid();
			var sequenceId = Guid.NewGuid();
			var userId = database.Insert("INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc) VALUES ('reporting@depot.test','Reporting','test',0,0,0,1,'2026-01-01T00:00:00.0000000Z');");
			database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('USD','US Dollar',2,1);");
			database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,'F6','F6 Entity','US','USD',1);", new DatabaseParameter("$Id", legalEntityId.ToString("D")));
			database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Legal,'CAL','Calendar',1);", new DatabaseParameter("$Id", calendarId.ToString("D")), new DatabaseParameter("$Legal", legalEntityId.ToString("D")));
			database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,'2026','2026-01-01','2026-12-31',0);", new DatabaseParameter("$Id", periodId.ToString("D")), new DatabaseParameter("$Calendar", calendarId.ToString("D")));
			database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,'COA','Chart',1);", new DatabaseParameter("$Id", chartId.ToString("D")));
			database.Execute("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,'1000','Cash',0,1,1);", new DatabaseParameter("$Id", cashAccountId.ToString("D")), new DatabaseParameter("$Chart", chartId.ToString("D")));
			database.Execute("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,'4000','Revenue',3,1,1);", new DatabaseParameter("$Id", revenueAccountId.ToString("D")), new DatabaseParameter("$Chart", chartId.ToString("D")));
			database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Legal,$Chart,'PRIMARY','Primary','USD','TEST',1,1);", new DatabaseParameter("$Id", bookId.ToString("D")), new DatabaseParameter("$Legal", legalEntityId.ToString("D")), new DatabaseParameter("$Chart", chartId.ToString("D")));
			database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,'GJ','General Journal',1);", new DatabaseParameter("$Id", journalId.ToString("D")), new DatabaseParameter("$Book", bookId.ToString("D")));
			database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$Legal,'GL',$Type,'GL-',4,1,1);", new DatabaseParameter("$Id", sequenceId.ToString("D")), new DatabaseParameter("$Legal", legalEntityId.ToString("D")), new DatabaseParameter("$Type", FinanceNumberSequenceDocumentTypes.GeneralLedger));

			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = userId, Email = "reporting@depot.test", DisplayName = "Reporting", IsActive = true },
			[
				ApplicationPermission.FinanceGeneralLedgerView,
				ApplicationPermission.FinanceGeneralLedgerPost,
				ApplicationPermission.FinanceManualJournalsPost,
				ApplicationPermission.FinanceFinancialReportingView,
				ApplicationPermission.FinanceFinancialReportingManage,
				ApplicationPermission.FinanceFinancialReportingExport,
				ApplicationPermission.FinanceReportSnapshotsCreate,
				ApplicationPermission.FinanceReceivablesView,
				ApplicationPermission.FinancePayablesView
			]);
			var auditRepository = new AuditRepository(database);
			var audit = new AuditService(auditRepository, authorization);
			var transactions = new DatabaseTransactionRunner(database);
			var generalLedger = new FinanceGeneralLedgerService(transactions, new FinanceGeneralLedgerRepository(database), new FinancePostingProfileRepository(database), auditRepository, audit, authorization);
			var receivables = new FinanceAccountsReceivableService(transactions, new FinanceAccountsReceivableRepository(database), generalLedger, auditRepository, audit, authorization);
			var payables = new FinanceAccountsPayableService(transactions, new FinanceAccountsPayableRepository(database), generalLedger, auditRepository, audit, authorization);
			var reporting = new FinanceFinancialReportingService(transactions, new FinanceFinancialReportingRepository(database), new FinanceFinancialReportingInventoryRepository(database), receivables, payables, auditRepository, audit, authorization);
			return new TestContext(path, database, generalLedger, reporting, bookId, periodId, journalId, cashAccountId, revenueAccountId);
		}

		public Task<FinanceJournalEntry> PostAsync(DateOnly postingDate, decimal amount, string sourceId) => _generalLedger.PostAsync(new FinancePostingRequest
		{
			OperationId = Guid.NewGuid(),
			AccountingBookId = BookId,
			JournalId = _journalId,
			AccountingPeriodId = _periodId,
			NumberSequenceCode = "GL",
			PostingDate = postingDate,
			Description = "Reporting test",
			SourceType = "FinanceReportingTest",
			SourceId = sourceId,
			SourceEvent = "Posted",
			TransactionCurrency = new CurrencyCode("USD"),
			EntryKind = FinanceJournalEntryKind.Manual,
			Lines =
			[
				new FinancePostingLine { AccountId = CashAccountId, Debit = amount },
				new FinancePostingLine { AccountId = RevenueAccountId, Credit = amount }
			]
		});

		public long Scalar(string sql) => Convert.ToInt64(_database.ExecuteScalarAsync(sql, CancellationToken.None).GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			try { File.Delete(_path); } catch (IOException) { }
		}
	}
}
