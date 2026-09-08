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

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Full")]
public sealed class FinanceBankingReportingProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SQLiteBankingAndReportingContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-provider-finance-{Guid.NewGuid():N}.db");
		try
		{
			await VerifyAsync(new SqliteConnectionFactory(path));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerBankingAndReportingContract() =>
		VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbBankingAndReportingContract() =>
		VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlBankingAndReportingContract() =>
		VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var database = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(database);
		var fixture = Seed(database);

		var authorization = new AuthorizationService();
		var permissions = new[]
		{
			ApplicationPermission.FinanceBankingView,
			ApplicationPermission.FinanceBankingManage,
			ApplicationPermission.FinanceBankStatementsCreate,
			ApplicationPermission.FinanceBankReconciliationManage,
			ApplicationPermission.FinanceFinancialReportingView,
			ApplicationPermission.FinanceFinancialReportingManage,
			ApplicationPermission.FinanceFinancialReportingExport,
			ApplicationPermission.FinanceReportSnapshotsCreate
		};
		authorization.SignIn(
			new User
			{
				Id = fixture.UserId,
				Email = fixture.UserEmail,
				DisplayName = "Provider banking/reporting",
				IsActive = true
			},
			permissions);

		var auditRepository = new AuditRepository(database);
		var audit = new AuditService(auditRepository, authorization);
		var generalLedger = new FinanceGeneralLedgerService(
			transactions,
			new FinanceGeneralLedgerRepository(database),
			new FinancePostingProfileRepository(database),
			auditRepository,
			audit,
			authorization);
		var receivables = new FinanceAccountsReceivableService(
			transactions,
			new FinanceAccountsReceivableRepository(database),
			generalLedger,
			auditRepository,
			audit,
			authorization);
		var payables = new FinanceAccountsPayableService(
			transactions,
			new FinanceAccountsPayableRepository(database),
			generalLedger,
			auditRepository,
			audit,
			authorization);
		var banking = new FinanceBankingService(
			transactions,
			new FinanceBankingRepository(database),
			payables,
			auditRepository,
			audit,
			authorization);
		var reporting = new FinanceFinancialReportingService(
			transactions,
			new FinanceFinancialReportingRepository(database),
			new FinanceFinancialReportingInventoryRepository(database),
			receivables,
			payables,
			auditRepository,
			audit,
			authorization);

		var bankAccount = await banking.SaveBankAccountAsync(new FinanceBankAccount
		{
			LegalEntityId = fixture.LegalEntityId,
			AccountingBookId = fixture.BookId,
			GeneralLedgerAccountId = fixture.BankAccountId,
			Currency = new CurrencyCode("XTS"),
			Name = $"Provider bank {fixture.Suffix[..8]}",
			BankName = "Provider Test Bank",
			LocalAccountNumber = fixture.Suffix[..12],
			IsActive = true
		});
		Assert.True(bankAccount.Id > 0);

		var importOperation = Guid.NewGuid();
		var statement = await banking.ImportStatementAsync(new FinanceBankStatementImportRequest
		{
			OperationId = importOperation,
			BankAccountId = bankAccount.Id,
			Format = FinanceBankStatementFormat.Csv,
			StatementReference = $"STMT-{fixture.Suffix[..8]}",
			OpeningBalance = 100m,
			ClosingBalance = 150m,
			Content = "BookingDate,Amount,Currency,Reference,Counterparty\n2026-09-08,50.00,XTS,PROVIDER-GL,Provider Customer"
		});
		Assert.Equal(150m, statement.ClosingBalance);
		var statementLine = Assert.Single(statement.Lines);
		Assert.Equal(50m, statementLine.Amount);
		var importRetry = await banking.ImportStatementAsync(new FinanceBankStatementImportRequest
		{
			OperationId = importOperation,
			BankAccountId = bankAccount.Id,
			Format = FinanceBankStatementFormat.Csv,
			StatementReference = $"STMT-{fixture.Suffix[..8]}",
			OpeningBalance = 100m,
			ClosingBalance = 150m,
			Content = "BookingDate,Amount,Currency,Reference,Counterparty\n2026-09-08,50.00,XTS,PROVIDER-GL,Provider Customer"
		});
		Assert.Equal(statement.Id, importRetry.Id);

		var reconciliation = await banking.ReconcileAsync(new FinanceBankReconciliationRequest
		{
			OperationId = Guid.NewGuid(),
			StatementLineId = statementLine.Id,
			TargetKind = FinanceBankReconciliationTargetKind.GeneralLedgerEntry,
			TargetId = fixture.JournalEntryId
		});
		Assert.Equal(fixture.JournalEntryId, reconciliation.TargetJournalEntryId);
		Assert.Equal(50m, reconciliation.MatchedAmount);
		var reversed = await banking.ReverseReconciliationAsync(reconciliation.Id, Guid.NewGuid(), "Provider acceptance reversal");
		Assert.True(reversed.IsReversed);

		var parameters = new FinanceReportParameters
		{
			Kind = FinanceReportKind.TrialBalance,
			AccountingBookId = fixture.BookId,
			FromDate = new DateOnly(2026, 9, 1),
			ToDate = new DateOnly(2026, 9, 30),
			IncludeZeroBalances = false
		};
		var report = await reporting.GenerateAsync(parameters);
		Assert.Equal(new CurrencyCode("XTS"), report.ReportingCurrency);
		Assert.Equal(50m, report.Rows.Sum(value => value.Debit));
		Assert.Equal(50m, report.Rows.Sum(value => value.Credit));
		Assert.Contains(report.Rows, value => value.AccountNumber == "1000" && value.Debit == 50m);
		Assert.Contains(report.Rows, value => value.AccountNumber == "4000" && value.Credit == 50m);
		Assert.Contains("1000", reporting.ExportCsv(report), StringComparison.Ordinal);

		var snapshotOperation = Guid.NewGuid();
		var snapshot = await reporting.CreateSnapshotAsync(snapshotOperation, report);
		Assert.True(snapshot.Id > 0);
		Assert.False(string.IsNullOrWhiteSpace(snapshot.ContentHash));
		var snapshotRetry = await reporting.CreateSnapshotAsync(snapshotOperation, report);
		Assert.Equal(snapshot.Id, snapshotRetry.Id);
		Assert.Contains(await reporting.GetRecentSnapshotsAsync(fixture.BookId), value => value.Id == snapshot.Id);
	}

	private static Fixture Seed(DatabaseAccess database)
	{
		var suffix = Guid.NewGuid().ToString("N");
		var userEmail = $"finance-bank-report-{suffix}@depot.test";
		var userId = database.Insert(
			"INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ($Email,'Provider Banking Reporting','test',0,0,0,1,$Created);",
			new DatabaseParameter("$Email", userEmail),
			new DatabaseParameter("$Created", "2026-09-08T00:00:00.0000000Z"));

		if (Convert.ToInt32(database.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceCurrencies WHERE Code='XTS';", CancellationToken.None).GetAwaiter().GetResult(), CultureInfo.InvariantCulture) == 0)
		{
			database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('XTS','Testing Currency',2,1);");
		}

		var legalEntityId = Guid.NewGuid();
		var calendarId = Guid.NewGuid();
		var periodId = Guid.NewGuid();
		var chartId = Guid.NewGuid();
		var bookId = Guid.NewGuid();
		var journalId = Guid.NewGuid();
		var bankAccountId = Guid.NewGuid();
		var revenueAccountId = Guid.NewGuid();

		database.Execute(
			"INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,'Provider Banking Entity','DE','XTS',1);",
			new DatabaseParameter("$Id", legalEntityId.ToString("D")),
			new DatabaseParameter("$Code", $"LE-{suffix[..8]}"));
		database.Execute(
			"INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,$Code,'Provider Banking Calendar',1);",
			new DatabaseParameter("$Id", calendarId.ToString("D")),
			new DatabaseParameter("$Entity", legalEntityId.ToString("D")),
			new DatabaseParameter("$Code", $"CAL-{suffix[..8]}"));
		database.Execute(
			"INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,'2026-09','2026-09-01','2026-09-30',0);",
			new DatabaseParameter("$Id", periodId.ToString("D")),
			new DatabaseParameter("$Calendar", calendarId.ToString("D")));
		database.Execute(
			"INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,$Code,'Provider Banking Chart',1);",
			new DatabaseParameter("$Id", chartId.ToString("D")),
			new DatabaseParameter("$Code", $"COA-{suffix[..8]}"));
		database.Execute(
			"INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,'1000','Provider Bank',1,1,1);",
			new DatabaseParameter("$Id", bankAccountId.ToString("D")),
			new DatabaseParameter("$Chart", chartId.ToString("D")));
		database.Execute(
			"INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,'4000','Provider Revenue',4,1,1);",
			new DatabaseParameter("$Id", revenueAccountId.ToString("D")),
			new DatabaseParameter("$Chart", chartId.ToString("D")));
		database.Execute(
			"INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,$Code,'Provider Banking Book','XTS','TEST',1,1);",
			new DatabaseParameter("$Id", bookId.ToString("D")),
			new DatabaseParameter("$Entity", legalEntityId.ToString("D")),
			new DatabaseParameter("$Chart", chartId.ToString("D")),
			new DatabaseParameter("$Code", $"BOOK-{suffix[..8]}"));
		database.Execute(
			"INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,$Code,'Provider Banking Journal',1);",
			new DatabaseParameter("$Id", journalId.ToString("D")),
			new DatabaseParameter("$Book", bookId.ToString("D")),
			new DatabaseParameter("$Code", $"J-{suffix[..8]}"));

		var journalEntryId = database.Insert(
			"INSERT INTO FinanceJournalEntries (EntryNumber,OperationId,RequestHash,AccountingBookId,JournalId,AccountingPeriodId,PostingDate,PostedAtUtc,PostedByUserId,Description,SourceType,SourceId,SourceEvent,SourceReference,TransactionCurrencyCode,ReportingCurrencyCode,ExchangeRateId,ExchangeRate,EntryKind,ReversalOfEntryId) VALUES ($Number,$Operation,$Hash,$Book,$Journal,$Period,'2026-09-08',$PostedAtUtc,$User,'Provider banking/reporting entry','ProviderAcceptance',$Source,'Posted','PROVIDER-GL','XTS','XTS',NULL,1,$Kind,NULL);",
			new DatabaseParameter("$Number", $"BR-{suffix[..10]}"),
			new DatabaseParameter("$Operation", Guid.NewGuid().ToString("D")),
			new DatabaseParameter("$Hash", $"provider-{suffix}"),
			new DatabaseParameter("$Book", bookId.ToString("D")),
			new DatabaseParameter("$Journal", journalId.ToString("D")),
			new DatabaseParameter("$Period", periodId.ToString("D")),
			new DatabaseParameter("$PostedAtUtc", "2026-09-08T12:34:56.1234560Z"),
			new DatabaseParameter("$User", userId),
			new DatabaseParameter("$Source", $"BR-{suffix}"),
			new DatabaseParameter("$Kind", (int)FinanceJournalEntryKind.Manual));
		database.Execute(
			"INSERT INTO FinanceJournalEntryLines (JournalEntryId,LineNumber,AccountId,Description,TransactionDebit,TransactionCredit,ReportingDebit,ReportingCredit) VALUES ($Entry,1,$Bank,'Provider bank movement',50,0,50,0);",
			new DatabaseParameter("$Entry", journalEntryId),
			new DatabaseParameter("$Bank", bankAccountId.ToString("D")));
		database.Execute(
			"INSERT INTO FinanceJournalEntryLines (JournalEntryId,LineNumber,AccountId,Description,TransactionDebit,TransactionCredit,ReportingDebit,ReportingCredit) VALUES ($Entry,2,$Revenue,'Provider revenue',0,50,0,50);",
			new DatabaseParameter("$Entry", journalEntryId),
			new DatabaseParameter("$Revenue", revenueAccountId.ToString("D")));

		return new Fixture(suffix, userId, userEmail, legalEntityId, bookId, bankAccountId, journalEntryId);
	}

	private sealed record Fixture(
		string Suffix,
		long UserId,
		string UserEmail,
		Guid LegalEntityId,
		Guid BookId,
		Guid BankAccountId,
		long JournalEntryId);
}
