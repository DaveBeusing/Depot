// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceBankingTests
{
	[Fact]
	public void FinanceMigrationCreatesBankingSchemaVersionSeven()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-finance-f5-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			using var connection = new SqliteConnection($"Data Source={path}");
			connection.Open();
			Assert.Equal(7L, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceBankAccounts';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceBankStatements';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceBankReconciliations';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinancePaymentRuns';"));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try { File.Delete(path); } catch (IOException) { }
		}
	}

	[Fact]
	public void CsvStatementParserNormalizesSignedTransactionsAndBalances()
	{
		var parsed = FinanceBankStatementParser.Parse(new FinanceBankStatementImportRequest
		{
			OperationId = Guid.NewGuid(),
			BankAccountId = 1,
			Format = FinanceBankStatementFormat.Csv,
			StatementReference = "CSV-001",
			OpeningBalance = 100m,
			Content = "BookingDate;Amount;Currency;Reference;Counterparty\n2026-08-01;50.00;EUR;CUSTOMER-1;Customer A\n2026-08-02;-20.00;EUR;SUPPLIER-1;Supplier B"
		}, new CurrencyCode("EUR"));

		Assert.Equal("CSV-001", parsed.StatementReference);
		Assert.Equal(100m, parsed.OpeningBalance);
		Assert.Equal(130m, parsed.ClosingBalance);
		Assert.Equal([50m, -20m], parsed.Lines.Select(line => line.Amount).ToArray());
		Assert.Equal(new DateOnly(2026, 8, 1), parsed.FromDate);
		Assert.Equal(new DateOnly(2026, 8, 2), parsed.ToDate);
	}

	[Fact]
	public void Camt053ParserMapsCreditAndDebitEntries()
	{
		const string xml = """
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08"><BkToCstmrStmt><Stmt><Id>CAMT-001</Id><FrToDt><FrDt>2026-08-01</FrDt><ToDt>2026-08-02</ToDt></FrToDt><Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd></Bal><Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">130.00</Amt><CdtDbtInd>CRDT</CdtDbtInd></Bal><Ntry><Amt Ccy="EUR">50.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><BookgDt><Dt>2026-08-01</Dt></BookgDt><AcctSvcrRef>A1</AcctSvcrRef></Ntry><Ntry><Amt Ccy="EUR">20.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><BookgDt><Dt>2026-08-02</Dt></BookgDt><AcctSvcrRef>A2</AcctSvcrRef></Ntry></Stmt></BkToCstmrStmt></Document>
""";
		var parsed = FinanceBankStatementParser.Parse(new FinanceBankStatementImportRequest { OperationId=Guid.NewGuid(),BankAccountId=1,Format=FinanceBankStatementFormat.Iso20022Camt053,Content=xml }, new CurrencyCode("EUR"));
		Assert.Equal("CAMT-001", parsed.StatementReference);
		Assert.Equal([50m,-20m], parsed.Lines.Select(line=>line.Amount).ToArray());
		Assert.Equal(130m, parsed.ClosingBalance);
	}

	[Fact]
	public void StatementParserRejectsCrossCurrencyRows()
	{
		var request = new FinanceBankStatementImportRequest { OperationId=Guid.NewGuid(),BankAccountId=1,Format=FinanceBankStatementFormat.Csv,Content="BookingDate,Amount,Currency\n2026-08-01,10.00,USD" };
		Assert.Throws<InvalidDataException>(() => FinanceBankStatementParser.Parse(request,new CurrencyCode("EUR")));
	}

	[Fact]
	public void FinanceRoleGetsOperationalBankingButNotPaymentProposalApproval()
	{
		var finance = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.FinanceCode);
		var approver = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.ApproverCode);
		Assert.Contains(ApplicationPermission.FinanceBankingView, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceBankStatementsCreate, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceBankReconciliationManage, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinancePaymentProposalsCreate, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinancePaymentRunsPost, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceCashPositionView, finance.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinancePaymentProposalsApprove, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinancePaymentProposalsApprove, approver.Permissions);
		Assert.Equal("FinancePaymentProposals.Approve", PermissionCatalog.Code(ApplicationPermission.FinancePaymentProposalsApprove));
	}

	[Fact]
	public void F5RecordsAreClassifiedAsRetainedEvidence()
	{
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceBankStatement)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AuditEvidence, BusinessRecordCatalog.Require(nameof(FinanceBankReconciliation)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinancePaymentRun)).RetentionCategory);
	}

	private static long Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
	}
}
