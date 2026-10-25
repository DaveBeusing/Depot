// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceInventoryHistoricalValueTests
{
	[Fact]
	public async Task VersionTwelveMigrationPreservesReceiptsAndIsRepeatable()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await fixture.ReceiptAsync(1, 100m, new DateOnly(2026, 1, 1));
		await fixture.Database.ExecuteAsync("DROP TABLE FinanceInventoryReportingConsumptions; UPDATE DepotFeatureVersions SET Version=12 WHERE Name='Finance';", CancellationToken.None);
		FinanceInventoryAccountingSchemaMigration.Migrate(fixture.Factory);
		FinanceInventoryAccountingSchemaMigration.Migrate(fixture.Factory);
		Assert.Equal(13L, await fixture.ScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
		var issue = await fixture.IssueAsync(1, new DateOnly(2026, 2, 1));
		Assert.Equal(90m, await fixture.ReportingDebitAsync(issue));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 1));
	}

	[Fact]
	public async Task FullDepletionUsesReceiptRateAndExactReversal()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await VerifyFullDepletionAsync(fixture);
	}

	[Fact]
	public async Task MixedRatesLandedCostsAndOutOfOrderReversalPreserveCarryingValue()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await VerifyMixedLayersAsync(fixture);
	}

	[Fact]
	public async Task NegativeAdjustmentsConsumeAndRestoreHistoricalValue()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await VerifyNegativeAdjustmentAsync(fixture);
	}

	[Fact]
	public async Task FractionalLandedCostAllocationsDepleteAtCurrencyPrecision()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await VerifyFractionalLandedCostAsync(fixture);
	}

	[Fact]
	public async Task CurrencyRoundingCanConsumeZeroReportingValueAndStillDepleteExactly()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await fixture.ReceiptAsync(3, 0.02m, new DateOnly(2026, 3, 1));
		var first = await fixture.IssueAsync(1, new DateOnly(2026, 4, 1));
		var second = await fixture.IssueAsync(1, new DateOnly(2026, 4, 2));
		var third = await fixture.IssueAsync(1, new DateOnly(2026, 4, 3));
		Assert.Equal(0m, await fixture.ReportingDebitAsync(first));
		Assert.Equal(0.01m, await fixture.ReportingDebitAsync(second));
		Assert.Equal(0m, await fixture.ReportingDebitAsync(third));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 4, 3));
		await fixture.ReverseIssueAsync(first, new DateOnly(2026, 4, 4));
		await fixture.AssertBalanceAsync(1, 0m, new DateOnly(2026, 4, 4));
	}

	[Fact]
	public async Task LegacyConsumptionUsesItsRecordedJournalAndMissingEvidenceFailsAtomically()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await fixture.ReceiptAsync(2, 50m, new DateOnly(2026, 1, 1));
		await fixture.LegacyIssueAsync(1, 50m, new DateOnly(2026, 2, 1));
		var issue = await fixture.IssueAsync(1, new DateOnly(2026, 2, 2));
		Assert.Equal(50m, await fixture.ReportingDebitAsync(issue));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 2));
		await fixture.ReverseIssueAsync(issue, new DateOnly(2026, 2, 3));
		await fixture.Database.ExecuteAsync("UPDATE FinanceInventoryValuationConsumptions SET Amount=Amount+1 WHERE MovementId=$Movement;", CancellationToken.None, new DatabaseParameter("$Movement", issue.MovementId));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.IssueAsync(1, new DateOnly(2026, 2, 4)));
		Assert.Equal(1L, await fixture.QuantityAsync());
	}

	[Fact]
	public async Task BackdatedIssueCannotConsumeLaterLandedCostEvidence()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await fixture.ReceiptAsync(1, 100m, new DateOnly(2026, 1, 1));
		await fixture.LandedAsync(10m, new DateOnly(2026, 2, 1));
		var journals = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;");
		var consumptions = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryValuationConsumptions;");
		var reportingEvidence = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryReportingConsumptions;");

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.IssueAsync(1, new DateOnly(2026, 1, 15)));

		Assert.Equal(1L, await fixture.QuantityAsync());
		Assert.Equal(journals, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(consumptions, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryValuationConsumptions;"));
		Assert.Equal(reportingEvidence, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryReportingConsumptions;"));
		await fixture.AssertBalanceAsync(1, 90m, new DateOnly(2026, 1, 31));
		await fixture.AssertBalanceAsync(1, 98m, new DateOnly(2026, 2, 1));
	}

	[Fact]
	public async Task BackdatedIssueCannotConsumeQuantityRestoredByLaterReversal()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await fixture.ReceiptAsync(1, 100m, new DateOnly(2026, 1, 1));
		var original = await fixture.IssueAsync(1, new DateOnly(2026, 1, 20));
		await fixture.ReverseIssueAsync(original, new DateOnly(2026, 2, 10));
		var journals = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;");
		var consumptions = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryValuationConsumptions;");
		var reportingEvidence = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryReportingConsumptions;");

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.IssueAsync(1, new DateOnly(2026, 1, 15)));

		Assert.Equal(1L, await fixture.QuantityAsync());
		Assert.Equal(journals, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(consumptions, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryValuationConsumptions;"));
		Assert.Equal(reportingEvidence, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryReportingConsumptions;"));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 1, 31));
		await fixture.AssertBalanceAsync(1, 90m, new DateOnly(2026, 2, 10));
	}

	[Fact]
	public async Task AuditFailureRollsBackJournalQuantityAndReportingEvidence()
	{
		await using var fixture = await FinanceInventoryHistoricalValueFixture.CreateSqliteAsync();
		await fixture.ReceiptAsync(1, 100m, new DateOnly(2026, 1, 1));
		var journals = await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;");
		await fixture.Database.ExecuteAsync("""
			CREATE TRIGGER RejectInventoryAudit BEFORE INSERT ON AuditEntries
			WHEN NEW.EntityType='FinanceInventoryAccountingEvent'
			BEGIN SELECT RAISE(ABORT, 'injected inventory audit failure'); END;
			""", CancellationToken.None);
		await Assert.ThrowsAsync<SqliteException>(() => fixture.IssueAsync(1, new DateOnly(2026, 2, 1)));
		Assert.Equal(1L, await fixture.QuantityAsync());
		Assert.Equal(journals, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM FinanceInventoryReportingConsumptions;"));
	}

	internal static async Task VerifyFullDepletionAsync(FinanceInventoryHistoricalValueFixture fixture)
	{
		await fixture.ReceiptAsync(1, 100m, new DateOnly(2026, 1, 1));
		var issue = await fixture.IssueAsync(1, new DateOnly(2026, 2, 1));
		Assert.Equal(100m, issue.Amount);
		Assert.Equal(90m, await fixture.ReportingDebitAsync(issue));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 1));
		var reversed = await fixture.ReverseIssueAsync(issue, new DateOnly(2026, 2, 2));
		Assert.Equal(90m, await fixture.ReportingDebitAsync(reversed));
		await fixture.AssertBalanceAsync(1, 90m, new DateOnly(2026, 2, 2));
		await fixture.IssueAsync(1, new DateOnly(2026, 2, 3));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 3));
	}

	internal static async Task VerifyMixedLayersAsync(FinanceInventoryHistoricalValueFixture fixture)
	{
		await fixture.ReceiptAsync(2, 100m, new DateOnly(2026, 1, 1));
		await fixture.ReceiptAsync(2, 50m, new DateOnly(2026, 1, 15));
		await fixture.LandedAsync(10m, new DateOnly(2026, 1, 20));
		await fixture.AssertBalanceAsync(4, 284.50m, new DateOnly(2026, 1, 31));
		var first = await fixture.IssueAsync(1, new DateOnly(2026, 2, 1));
		var second = await fixture.IssueAsync(2, new DateOnly(2026, 2, 2));
		Assert.Equal(92.38m, await fixture.ReportingDebitAsync(first));
		Assert.Equal(142.25m, await fixture.ReportingDebitAsync(second));
		await fixture.AssertBalanceAsync(1, 49.87m, new DateOnly(2026, 2, 2));
		await fixture.ReverseIssueAsync(first, new DateOnly(2026, 2, 3));
		await fixture.AssertBalanceAsync(2, 142.25m, new DateOnly(2026, 2, 3));
		var final = await fixture.IssueAsync(2, new DateOnly(2026, 2, 4));
		Assert.Equal(142.25m, await fixture.ReportingDebitAsync(final));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 4));
		await fixture.AssertBalanceAsync(4, 284.50m, new DateOnly(2026, 1, 31));
		await fixture.AssertBalanceAsync(1, 49.87m, new DateOnly(2026, 2, 2));
	}

	internal static async Task VerifyNegativeAdjustmentAsync(FinanceInventoryHistoricalValueFixture fixture)
	{
		await fixture.ReceiptAsync(3, 33.33m, new DateOnly(2026, 1, 1));
		var first = await fixture.AdjustAsync(-1, new DateOnly(2026, 2, 1));
		var second = await fixture.AdjustAsync(-2, new DateOnly(2026, 2, 2));
		Assert.Equal(30m, await fixture.ReportingDebitAsync(first));
		Assert.Equal(59.99m, await fixture.ReportingDebitAsync(second));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 2));
		await fixture.ReverseAdjustmentAsync(first, new DateOnly(2026, 2, 3));
		await fixture.AssertBalanceAsync(1, 30m, new DateOnly(2026, 2, 3));
	}

	internal static async Task VerifyFractionalLandedCostAsync(FinanceInventoryHistoricalValueFixture fixture)
	{
		for (var index = 0; index < 3; index++) await fixture.ReceiptAsync(1, 1m, new DateOnly(2026, 1, 15));
		await fixture.LandedAsync(0.01m, new DateOnly(2026, 1, 20));
		await fixture.AssertBalanceAsync(3, 2.86m, new DateOnly(2026, 1, 31));
		var first = await fixture.IssueAsync(1, new DateOnly(2026, 2, 1));
		var second = await fixture.IssueAsync(1, new DateOnly(2026, 2, 2));
		var third = await fixture.IssueAsync(1, new DateOnly(2026, 2, 3));
		Assert.Equal(1m, first.Amount);
		Assert.Equal(1.01m, second.Amount);
		Assert.Equal(1m, third.Amount);
		Assert.Equal(0.95m, await fixture.ReportingDebitAsync(first));
		Assert.Equal(0.96m, await fixture.ReportingDebitAsync(second));
		Assert.Equal(0.95m, await fixture.ReportingDebitAsync(third));
		await fixture.AssertBalanceAsync(0, 0m, new DateOnly(2026, 2, 3));
	}
}

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Full")]
public sealed class FinanceInventoryHistoricalValueProviderAcceptanceTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerHistoricalInventoryValues() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbHistoricalInventoryValues() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlHistoricalInventoryValues() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		FinanceInventoryAccountingSchemaMigration.Migrate(factory);
		FinanceInventoryAccountingSchemaMigration.Migrate(factory);
		await using (var fixture = await FinanceInventoryHistoricalValueFixture.CreateAsync(factory))
			await FinanceInventoryHistoricalValueTests.VerifyFullDepletionAsync(fixture);
		await using (var fixture = await FinanceInventoryHistoricalValueFixture.CreateAsync(factory))
			await FinanceInventoryHistoricalValueTests.VerifyMixedLayersAsync(fixture);
		await using (var fixture = await FinanceInventoryHistoricalValueFixture.CreateAsync(factory))
			await FinanceInventoryHistoricalValueTests.VerifyNegativeAdjustmentAsync(fixture);
		await using (var fixture = await FinanceInventoryHistoricalValueFixture.CreateAsync(factory))
			await FinanceInventoryHistoricalValueTests.VerifyFractionalLandedCostAsync(fixture);
	}
}

internal sealed class FinanceInventoryHistoricalValueFixture : IAsyncDisposable
{
	private readonly string? _path;
	private readonly string _suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
	private readonly Guid _entity = Guid.NewGuid(), _calendar = Guid.NewGuid(), _period = Guid.NewGuid(), _chart = Guid.NewGuid(), _book = Guid.NewGuid(), _journal = Guid.NewGuid();
	private readonly Guid _inventoryAccount = Guid.NewGuid(), _offsetAccount = Guid.NewGuid(), _expenseAccount = Guid.NewGuid();
	private readonly DatabaseTransactionRunner _transactions;
	private readonly FinanceInventoryAccountingRepository _repository;
	private readonly FinanceInventoryAccountingService _accounting;
	private readonly FinanceInventoryCostingService _costing;
	private readonly FinanceGeneralLedgerService _ledger;
	private FinanceInventoryAccountingConfiguration? _previousConfiguration;
	private FinanceInventoryAccountingPolicy? _previousPolicy;
	private long _userId, _itemId, _issueProfile;
	private static readonly CurrencyCode TransactionCurrency = new("XTS"), ReportingCurrency = new("XCR");

	private FinanceInventoryHistoricalValueFixture(IDatabaseConnectionFactory factory, string? path)
	{
		_path = path;
		Factory = factory;
		Database = new DatabaseAccess(factory);
		_transactions = new DatabaseTransactionRunner(Database);
		_repository = new FinanceInventoryAccountingRepository(Database);
		var authorization = new AuthorizationService();
		_userId = Database.Query("SELECT Id FROM Users WHERE Email='admin@depot.local';", reader => reader.GetInt64(0)).Single();
		authorization.SignIn(new User { Id = _userId, IsActive = true }, PermissionCatalog.All);
		var auditEntries = new AuditRepository(Database);
		var audit = new AuditService(auditEntries, authorization);
		_ledger = new FinanceGeneralLedgerService(_transactions, new FinanceGeneralLedgerRepository(Database), new FinancePostingProfileRepository(Database), auditEntries, audit, authorization);
		_accounting = new FinanceInventoryAccountingService(_transactions, _repository, _ledger, auditEntries, audit, authorization);
		_costing = new FinanceInventoryCostingService(_transactions, _repository, new FinanceInventoryCostingRepository(Database), _ledger, auditEntries, audit, authorization);
	}

	internal IDatabaseConnectionFactory Factory { get; }
	internal DatabaseAccess Database { get; }
	internal static async Task<FinanceInventoryHistoricalValueFixture> CreateSqliteAsync()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-inventory-history-{Guid.NewGuid():N}.db");
		var factory = new SqliteConnectionFactory(path);
		DatabaseProvisioningService.Initialize(factory);
		return await CreateAsync(factory, path);
	}
	internal static async Task<FinanceInventoryHistoricalValueFixture> CreateAsync(IDatabaseConnectionFactory factory, string? path = null)
	{
		var fixture = new FinanceInventoryHistoricalValueFixture(factory, path);
		await fixture.SeedAsync();
		return fixture;
	}

	private async Task SeedAsync()
	{
		foreach (var currency in new[] { TransactionCurrency, ReportingCurrency })
			Database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) SELECT $Code,$Code,2,1 WHERE NOT EXISTS (SELECT 1 FROM FinanceCurrencies WHERE Code=$Code);", new DatabaseParameter("$Code", currency.Value));
		Database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,'Historical inventory','DE',$Currency,1);", P("$Id", _entity), P("$Code", "IV-" + _suffix), P("$Currency", ReportingCurrency.Value));
		Database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,$Code,'Inventory calendar',1);", P("$Id", _calendar), P("$Entity", _entity), P("$Code", "IV-" + _suffix));
		Database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,'2026','2026-01-01','2026-12-31',0);", P("$Id", _period), P("$Calendar", _calendar));
		Database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,$Code,'Inventory chart',1);", P("$Id", _chart), P("$Code", "IV-" + _suffix));
		foreach (var (account, number, type) in new[] { (_inventoryAccount, "1500", FinanceAccountType.Asset), (_offsetAccount, "2000", FinanceAccountType.Liability), (_expenseAccount, "6000", FinanceAccountType.Expense) })
			Database.Execute("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,$Number,$Number,$Type,1,1);", P("$Id", account), P("$Chart", _chart), P("$Number", number), P("$Type", (int)type));
		Database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,$Code,'Inventory book',$Currency,'TEST',1,1);", P("$Id", _book), P("$Entity", _entity), P("$Chart", _chart), P("$Code", "IV-" + _suffix), P("$Currency", ReportingCurrency.Value));
		Database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,$Code,'Inventory journal',1);", P("$Id", _journal), P("$Book", _book), P("$Code", "IV-" + _suffix));
		Database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$Entity,$Code,$Type,'IV-',8,1,1);", P("$Id", Guid.NewGuid()), P("$Entity", _entity), P("$Code", "IV-" + _suffix), P("$Type", FinanceNumberSequenceDocumentTypes.GeneralLedger));
		foreach (var (date, rate) in new[] { ("2026-01-01T00:00:00Z", 0.90m), ("2026-01-15T00:00:00Z", 0.95m), ("2026-02-01T00:00:00Z", 0.80m), ("2026-03-01T00:00:00Z", 0.10m) })
			Database.Execute("INSERT INTO FinanceExchangeRates (Id,BaseCurrencyCode,QuoteCurrencyCode,Rate,EffectiveAtUtc,SourceCode) VALUES ($Id,$Base,$Quote,$Rate,$At,'Inventory history');", P("$Id", Guid.NewGuid()), P("$Base", TransactionCurrency.Value), P("$Quote", ReportingCurrency.Value), P("$Rate", rate), P("$At", date));
		_itemId = Database.Insert("INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($Part,'Historical FIFO item',1);", P("$Part", "IV-" + _suffix));
		var receipt = await ProfileAsync(FinanceInventoryAccountingEvents.GoodsReceipt, ("Cost", _inventoryAccount, _offsetAccount));
		_issueProfile = await ProfileAsync(FinanceInventoryAccountingEvents.SalesShipment, ("Cost", _expenseAccount, _inventoryAccount));
		var adjustment = await ProfileAsync(FinanceInventoryAccountingEvents.InventoryAdjustment, ("AdjustmentDebit", _inventoryAccount, _offsetAccount), ("AdjustmentCredit", _expenseAccount, _inventoryAccount));
		var landed = await ProfileAsync(FinanceInventoryAccountingEvents.LandedCost, ("Cost", _inventoryAccount, _offsetAccount));
		var variance = await ProfileAsync(FinanceInventoryAccountingEvents.PurchaseVariance, ("VarianceDebit", _expenseAccount, _offsetAccount), ("VarianceCredit", _offsetAccount, _expenseAccount));
		_previousConfiguration = await _accounting.GetConfigurationAsync();
		_previousPolicy = await _costing.GetPolicyAsync();
		await _accounting.SaveConfigurationAsync(new FinanceInventoryAccountingConfiguration { Id = _previousConfiguration?.Id ?? 0, Version = _previousConfiguration?.Version ?? 1, LegalEntityId = _entity, FiscalCalendarId = _calendar, PurchaseOrderPriceCurrency = TransactionCurrency, GoodsReceiptPostingProfileId = receipt, SalesIssuePostingProfileId = _issueProfile, IsActive = true });
		await _costing.SavePolicyAsync(new FinanceInventoryAccountingPolicy { Id = _previousPolicy?.Id ?? 0, Version = _previousPolicy?.Version ?? 1, InventoryControlAccountId = _inventoryAccount, InventoryAdjustmentPostingProfileId = adjustment, LandedCostPostingProfileId = landed, PurchaseVariancePostingProfileId = variance, IsActive = true });
	}

	private async Task<long> ProfileAsync(string eventName, params (string Key, Guid Debit, Guid Credit)[] amounts)
	{
		var lines = amounts.SelectMany((amount, index) => new[]
		{
			new FinancePostingProfileLine { LineNumber = index * 2 + 1, AccountId = amount.Debit, Direction = FinancePostingDirection.Debit, AmountKey = amount.Key },
			new FinancePostingProfileLine { LineNumber = index * 2 + 2, AccountId = amount.Credit, Direction = FinancePostingDirection.Credit, AmountKey = amount.Key }
		}).ToArray();
		return (await _ledger.SavePostingProfileAsync(new FinancePostingProfile { LegalEntityId = _entity, AccountingBookId = _book, JournalId = _journal, Code = eventName + "-" + _suffix, Name = eventName, SourceType = FinanceInventoryAccountingEvents.SourceType, SourceEvent = eventName, NumberSequenceCode = "IV-" + _suffix, Lines = lines })).Id;
	}

	internal async Task<FinanceInventoryAccountingEvent> ReceiptAsync(int quantity, decimal unitCost, DateOnly date) =>
		await _transactions.ExecuteAsync(async (transaction, token) => await _accounting.RecordGoodsReceiptAsync(transaction, Movement(StockMovementType.Purchase, quantity), _itemId, unitCost, date, "Receipt", _userId, token) ?? throw new InvalidOperationException(), CancellationToken.None);
	internal async Task<FinanceInventoryAccountingEvent> IssueAsync(int quantity, DateOnly date) =>
		await _transactions.ExecuteAsync(async (transaction, token) => await _accounting.RecordSalesShipmentAsync(transaction, Movement(StockMovementType.SalesShipment, -quantity), _itemId, date, "Issue", _userId, token) ?? throw new InvalidOperationException(), CancellationToken.None);
	internal async Task<FinanceInventoryAccountingEvent> AdjustAsync(int quantity, DateOnly date) =>
		await _transactions.ExecuteAsync(async (transaction, token) => await _costing.RecordInventoryAdjustmentAsync(transaction, Movement(StockMovementType.Correction, quantity), _itemId, date, "Count", _userId, token) ?? throw new InvalidOperationException(), CancellationToken.None);
	internal async Task<FinanceInventoryAccountingEvent> ReverseIssueAsync(FinanceInventoryAccountingEvent original, DateOnly date) =>
		await _transactions.ExecuteAsync(async (transaction, token) => await _accounting.ReverseMovementAsync(transaction,
			new StockMovement { Id = original.MovementId, MovementType = StockMovementType.SalesShipment, Quantity = original.Quantity },
			Reversal(original), date, "Historical value reversal", _userId, token) ?? throw new InvalidOperationException(), CancellationToken.None);
	internal async Task<FinanceInventoryAccountingEvent> ReverseAdjustmentAsync(FinanceInventoryAccountingEvent original, DateOnly date) =>
		await _transactions.ExecuteAsync(async (transaction, token) => await _costing.ReverseInventoryAdjustmentAsync(transaction,
			new StockMovement { Id = original.MovementId, MovementType = StockMovementType.Correction, Quantity = original.Quantity },
			Reversal(original), date, "Historical adjustment reversal", _userId, token) ?? throw new InvalidOperationException(), CancellationToken.None);

	internal async Task LandedAsync(decimal amount, DateOnly date)
	{
		var ids = await Database.QueryAsync("SELECT Id FROM FinanceInventoryValuationLayers WHERE AccountingBookId=$Book AND ItemId=$Item ORDER BY Id;", reader => reader.GetInt64(0), CancellationToken.None, P("$Book", _book), P("$Item", _itemId));
		await _costing.AllocateLandedCostAsync(new FinanceInventoryLandedCostRequest { OperationId = Guid.NewGuid(), PostingDate = date, Currency = TransactionCurrency, Amount = amount, LayerIds = ids, AllocationMethod = FinanceLandedCostAllocationMethod.Quantity });
	}

	internal async Task LegacyIssueAsync(int quantity, decimal amount, DateOnly date)
	{
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var movement = Movement(StockMovementType.SalesShipment, -quantity);
			var layer = Assert.Single(await _repository.LockAvailableLayersAsync(transaction, _book, _itemId, token));
			var rate = await _ledger.ResolveExchangeRateIdForProfileAsync(transaction, _issueProfile, TransactionCurrency, date, token);
			var operation = Guid.NewGuid();
			var journal = await _ledger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest { OperationId = operation, PostingProfileId = _issueProfile, AccountingPeriodId = _period, PostingDate = date, Description = "Legacy inventory issue", SourceId = movement.Id.ToString(CultureInfo.InvariantCulture), TransactionCurrency = TransactionCurrency, ExchangeRateId = rate, Amounts = new Dictionary<string, decimal> { ["Cost"] = amount } }, _userId, token);
			await _repository.SetRemainingQuantityAsync(transaction, layer.Id, layer.RemainingQuantity, layer.RemainingQuantity - quantity, token);
			await _repository.CreateConsumptionAsync(transaction, new FinanceInventoryValuationConsumption { MovementId = movement.Id, LayerId = layer.Id, Quantity = quantity, UnitCost = layer.UnitCost, Amount = amount, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = _userId }, token);
			await _repository.CreateEventAsync(transaction, new FinanceInventoryAccountingEvent { MovementId = movement.Id, Kind = FinanceInventoryAccountingEventKind.SalesShipment, AccountingBookId = _book, ItemId = _itemId, Quantity = -quantity, Currency = TransactionCurrency, Amount = amount, JournalEntryId = journal.Id, OperationId = operation, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = _userId }, token);
		}, CancellationToken.None);
	}

	internal async Task AssertBalanceAsync(int quantity, decimal amount, DateOnly date)
	{
		var run = await _costing.ReconcileAsync(new FinanceInventoryReconciliationRequest { OperationId = Guid.NewGuid(), AsOfDate = date });
		Assert.Equal(quantity, run.Lines.Sum(line => line.Quantity));
		Assert.Equal(amount, run.ValuationAmount);
		Assert.Equal(amount, run.GeneralLedgerAmount);
		Assert.Equal(0m, run.Difference);
		var reportRows = await _transactions.ExecuteAsync((transaction, token) =>
			new FinanceFinancialReportingInventoryRepository(Database).GetValuationAsync(transaction, _book, date, token), CancellationToken.None);
		Assert.Equal(quantity, reportRows.Sum(row => row.Quantity));
		Assert.Equal(amount, reportRows.Sum(row => row.ReportingValue));
	}
	internal async Task<decimal> ReportingDebitAsync(FinanceInventoryAccountingEvent value) =>
		(await _ledger.GetByIdAsync(value.JournalEntryId) ?? throw new InvalidOperationException()).Lines.Sum(line => line.ReportingDebit);
	internal Task<long> QuantityAsync() => ScalarAsync("SELECT COALESCE(SUM(RemainingQuantity),0) FROM FinanceInventoryValuationLayers WHERE AccountingBookId=$Book AND ItemId=$Item;", P("$Book", _book), P("$Item", _itemId));
	internal async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters) => Convert.ToInt64(await Database.ExecuteScalarAsync(sql, CancellationToken.None, parameters), CultureInfo.InvariantCulture);
	private static DatabaseParameter P(string name, object value) => new(name, value is Guid guid ? guid.ToString("D") : value);
	private static StockMovement Movement(StockMovementType type, int quantity) => new() { Id = (BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) & long.MaxValue) | 1, MovementType = type, Quantity = quantity };
	private static StockMovement Reversal(FinanceInventoryAccountingEvent original)
	{
		var reversal = Movement(StockMovementType.Correction, -original.Quantity);
		reversal.ReversalOfMovementId = original.MovementId;
		return reversal;
	}

	public async ValueTask DisposeAsync()
	{
		if (_path is not null)
		{
			SqliteConnection.ClearAllPools();
			File.Delete(_path);
			return;
		}
		// Restore shared test-provider configuration without deleting retained accounting evidence.
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var policyRepository = new FinanceInventoryCostingRepository(Database);
			var configuration = await _repository.GetConfigurationAsync(transaction, token) ?? throw new InvalidOperationException();
			var policy = await policyRepository.GetPolicyAsync(transaction, token) ?? throw new InvalidOperationException();
			await _repository.UpdateConfigurationAsync(transaction,
				_previousConfiguration ?? (configuration with { IsActive = false }), configuration.Version, token);
			await policyRepository.UpdatePolicyAsync(transaction,
				_previousPolicy ?? (policy with { IsActive = false }), policy.Version, token);
		}, CancellationToken.None);
	}
}
