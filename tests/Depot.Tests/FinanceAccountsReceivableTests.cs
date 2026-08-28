// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceAccountsReceivableTests
{
	[Fact]
	public void FinanceMigrationCreatesAccountsReceivableSchemaVersionThree()
	{
		using var context = FinanceArTestContext.Create();
		Assert.Equal(3L, context.Scalar("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceReceivableOpenItems';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceReceivablePayments';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceDunningRuns';"));
	}

	[Fact]
	public async Task SalesInvoiceSourceCreatesIdempotentOpenItemAndBalancedJournal()
	{
		using var context = FinanceArTestContext.Create();
		var service = context.CreateService(ApplicationPermission.FinanceReceivablesView);
		var invoice = context.Invoice(101, "INV-101", 100m, 20m, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
		var first = await context.PostInvoiceSourceAsync(service, invoice);
		var retry = await context.PostInvoiceSourceAsync(service, invoice);
		Assert.NotNull(first);
		Assert.NotNull(retry);
		Assert.Equal(first!.Id, retry!.Id);
		Assert.Equal(120m, first.OriginalAmount);
		Assert.Equal(120m, first.RemainingAmount);
		Assert.Equal(FinanceReceivableOpenItemKind.Invoice, first.Kind);
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceReceivableOpenItems WHERE SourceType='SalesInvoice' AND SourceId='101';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries WHERE SourceType='SalesInvoice' AND SourceId='101';"));
		Assert.Equal(120m, context.DecimalScalar("SELECT SUM(TransactionDebit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", first.JournalEntryId)));
		Assert.Equal(120m, context.DecimalScalar("SELECT SUM(TransactionCredit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", first.JournalEntryId)));
	}

	[Fact]
	public async Task OverpaymentCanBeAllocatedLaterAndPaymentReversalRestoresEveryAllocation()
	{
		using var context = FinanceArTestContext.Create();
		var service = context.CreateService(ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceReceivablePaymentsPost, ApplicationPermission.FinanceReceivablePaymentsReverse);
		var firstInvoice = await context.PostInvoiceSourceAsync(service, context.Invoice(201, "INV-201", 100m, 20m, new DateTime(2026, 8, 1), new DateTime(2026, 8, 15))) ?? throw new InvalidOperationException();
		var operationId = Guid.NewGuid();
		var request = new FinanceReceivablePaymentRequest
		{
			OperationId = operationId,
			CustomerId = context.CustomerId,
			Currency = new CurrencyCode("USD"),
			PaymentDate = context.PostingDate,
			Amount = 150m,
			Reference = "BANK-001",
			Description = "Customer payment",
			Allocations = [new FinanceReceivableAllocationRequest(firstInvoice.Id, 120m)]
		};
		var payment = await service.PostPaymentAsync(request);
		var retry = await service.PostPaymentAsync(request);
		Assert.Equal(payment.Id, retry.Id);
		Assert.Equal(0m, (await service.GetOpenItemAsync(firstInvoice.Id))!.RemainingAmount);
		Assert.Equal(30m, (await service.GetOpenItemAsync(payment.OpenItemId))!.RemainingAmount);

		var secondInvoice = await context.PostInvoiceSourceAsync(service, context.Invoice(202, "INV-202", 25m, 20m, new DateTime(2026, 8, 10), new DateTime(2026, 8, 25))) ?? throw new InvalidOperationException();
		await service.AllocateCreditAsync(Guid.NewGuid(), payment.OpenItemId, context.PostingDate, [new FinanceReceivableAllocationRequest(secondInvoice.Id, 30m)]);
		Assert.Equal(0m, (await service.GetOpenItemAsync(secondInvoice.Id))!.RemainingAmount);
		Assert.Equal(0m, (await service.GetOpenItemAsync(payment.OpenItemId))!.RemainingAmount);

		var reversed = await service.ReversePaymentAsync(payment.Id, new FinanceReceivableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = context.PostingDate, Reason = "Payment returned" });
		Assert.True(reversed.IsReversed);
		Assert.Equal(120m, (await service.GetOpenItemAsync(firstInvoice.Id))!.RemainingAmount);
		Assert.Equal(30m, (await service.GetOpenItemAsync(secondInvoice.Id))!.RemainingAmount);
		var reversedCredit = await service.GetOpenItemAsync(payment.OpenItemId) ?? throw new InvalidOperationException();
		Assert.True(reversedCredit.IsVoided);
		Assert.Equal(0m, reversedCredit.RemainingAmount);
		Assert.Equal(2L, context.Scalar("SELECT COUNT(*) FROM FinanceReceivableAllocations WHERE CreditOpenItemId=$Id AND ReversedAtUtc IS NOT NULL;", new DatabaseParameter("$Id", payment.OpenItemId)));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalReversals WHERE OriginalEntryId=$Id;", new DatabaseParameter("$Id", payment.JournalEntryId)));
	}

	[Fact]
	public async Task WriteOffRequiresExplicitPermissionAndReversalRestoresReceivable()
	{
		using var context = FinanceArTestContext.Create();
		var readOnly = context.CreateService(ApplicationPermission.FinanceReceivablesView);
		var invoice = await context.PostInvoiceSourceAsync(readOnly, context.Invoice(301, "INV-301", 100m, 20m, new DateTime(2026, 8, 1), new DateTime(2026, 8, 20))) ?? throw new InvalidOperationException();
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => readOnly.PostWriteOffAsync(new FinanceReceivableWriteOffRequest { OperationId = Guid.NewGuid(), OpenItemId = invoice.Id, PostingDate = context.PostingDate, Amount = 20m, Reason = "Approved loss" }));
		var service = context.CreateService(ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceReceivableWriteOffsPost, ApplicationPermission.FinanceReceivableWriteOffsReverse);
		var writeOff = await service.PostWriteOffAsync(new FinanceReceivableWriteOffRequest { OperationId = Guid.NewGuid(), OpenItemId = invoice.Id, PostingDate = context.PostingDate, Amount = 20m, Reason = "Approved loss" });
		Assert.Equal(100m, (await service.GetOpenItemAsync(invoice.Id))!.RemainingAmount);
		var reversed = await service.ReverseWriteOffAsync(writeOff.Id, new FinanceReceivableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = context.PostingDate, Reason = "Write-off correction" });
		Assert.True(reversed.IsReversed);
		Assert.Equal(120m, (await service.GetOpenItemAsync(invoice.Id))!.RemainingAmount);
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalReversals WHERE OriginalEntryId=$Id;", new DatabaseParameter("$Id", writeOff.JournalEntryId)));
	}

	[Fact]
	public async Task AgingAndDunningUseCurrentOutstandingAndDunningRunIsIdempotent()
	{
		using var context = FinanceArTestContext.Create();
		var service = context.CreateService(ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceDunningView, ApplicationPermission.FinanceDunningManage);
		await context.PostInvoiceSourceAsync(service, context.Invoice(401, "INV-401", 100m, 20m, new DateTime(2026, 7, 1), new DateTime(2026, 7, 15)));
		var aging = await service.GetAgingAsync(new DateOnly(2026, 8, 28));
		var row = Assert.Single(aging);
		Assert.Equal(120m, row.Days31To60);
		var policy = await service.SaveDunningPolicyAsync(new FinanceDunningPolicy
		{
			LegalEntityId = context.LegalEntityId,
			Code = "STANDARD",
			Name = "Standard",
			Levels = [new FinanceDunningLevel { LevelNumber = 1, MinimumDaysOverdue = 1, Code = "L1", Name = "Level 1" }, new FinanceDunningLevel { LevelNumber = 2, MinimumDaysOverdue = 30, Code = "L2", Name = "Level 2" }]
		});
		var operationId = Guid.NewGuid();
		var runRequest = new FinanceDunningRunRequest { OperationId = operationId, PolicyId = policy.Id, AsOfDate = new DateOnly(2026, 8, 28) };
		var run = await service.RunDunningAsync(runRequest);
		var retry = await service.RunDunningAsync(runRequest);
		Assert.Equal(run.Id, retry.Id);
		var line = Assert.Single(run.Lines);
		Assert.Equal("L2", line.LevelCode);
		Assert.Equal(120m, line.OutstandingAmount);
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceDunningRuns WHERE OperationId=$OperationId;", new DatabaseParameter("$OperationId", operationId.ToString("D"))));
	}

	[Fact]
	public void FinanceSystemRoleGetsOperationalReceivablesButNotWriteOffAuthority()
	{
		var role = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.FinanceReceivablesView, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceReceivablesManage, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceReceivablePaymentsPost, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceReceivablePaymentsReverse, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceDunningManage, role.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceReceivableWriteOffsPost, role.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceReceivableWriteOffsReverse, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceReceivableWriteOffsPost, PermissionCatalog.All);
	}

	[Fact]
	public void F2RecordsAreClassifiedAsRetainedEvidence()
	{
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceReceivableOpenItem)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceReceivablePayment)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceReceivableWriteOff)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AuditEvidence, BusinessRecordCatalog.Require(nameof(FinanceDunningRun)).RetentionCategory);
	}

	private sealed class FinanceArTestContext : IDisposable
	{
		private readonly string _databasePath;
		private readonly SqliteConnectionFactory _factory;
		private FinanceArTestContext(string databasePath, SqliteConnectionFactory factory, DatabaseAccess database) { _databasePath = databasePath; _factory = factory; Database = database; Transactions = new DatabaseTransactionRunner(database); }
		public DatabaseAccess Database { get; }
		public DatabaseTransactionRunner Transactions { get; }
		public long UserId { get; private set; }
		public long CustomerId { get; private set; }
		public Guid LegalEntityId { get; private set; }
		public Guid CalendarId { get; private set; }
		public Guid PeriodId { get; private set; }
		public Guid BookId { get; private set; }
		public Guid JournalId { get; private set; }
		public Guid ArAccountId { get; private set; }
		public Guid RevenueAccountId { get; private set; }
		public Guid TaxAccountId { get; private set; }
		public Guid BankAccountId { get; private set; }
		public Guid WriteOffAccountId { get; private set; }
		public long InvoiceProfileId { get; private set; }
		public long CreditProfileId { get; private set; }
		public long PaymentProfileId { get; private set; }
		public long WriteOffProfileId { get; private set; }
		public DateOnly PostingDate { get; } = new(2026, 8, 28);

		public static FinanceArTestContext Create()
		{
			var path = Path.Combine(Path.GetTempPath(), $"depot-finance-ar-{Guid.NewGuid():N}.db");
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceAccountsReceivableSchemaMigration.Migrate(factory);
			var context = new FinanceArTestContext(path, factory, new DatabaseAccess(factory));
			context.Seed();
			return context;
		}

		public FinanceAccountsReceivableService CreateService(params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = UserId, Email = "finance-ar@depot.test", DisplayName = "Finance AR", IsActive = true }, permissions);
			var auditRepository = new AuditRepository(Database);
			var audit = new AuditService(auditRepository, authorization);
			var generalLedger = new FinanceGeneralLedgerService(Transactions, new FinanceGeneralLedgerRepository(Database), new FinancePostingProfileRepository(Database), auditRepository, audit, authorization);
			return new FinanceAccountsReceivableService(Transactions, new FinanceAccountsReceivableRepository(Database), generalLedger, auditRepository, audit, authorization);
		}

		public SalesInvoice Invoice(long id, string number, decimal net, decimal taxRate, DateTime invoiceDate, DateTime dueDate) => new()
		{
			Id = id, InvoiceNumber = number, CustomerId = CustomerId, CustomerName = "AR Customer", InvoiceDate = invoiceDate, DueDate = dueDate, Currency = "USD", Status = SalesInvoiceStatus.Posted,
			Lines = [new SalesInvoiceLine { LineNumber = 1, Quantity = 1, UnitPrice = net, TaxRate = taxRate }]
		};

		public Task<FinanceReceivableOpenItem?> PostInvoiceSourceAsync(FinanceAccountsReceivableService service, SalesInvoice invoice) => Transactions.ExecuteAsync(async (transaction, token) =>
		{
			var method = typeof(FinanceAccountsReceivableService).GetMethod("TryPostSalesInvoiceAsync", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(FinanceAccountsReceivableService), "TryPostSalesInvoiceAsync");
			var task = (Task<FinanceReceivableOpenItem?>)(method.Invoke(service, [transaction, invoice, UserId, token]) ?? throw new InvalidOperationException("Accounts Receivable source posting did not return a task."));
			return await task;
		});

		public long Scalar(string sql, params DatabaseParameter[] parameters) => Convert.ToInt64(Database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);
		public decimal DecimalScalar(string sql, params DatabaseParameter[] parameters) => Convert.ToDecimal(Database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);

		private void Seed()
		{
			LegalEntityId = Guid.NewGuid(); CalendarId = Guid.NewGuid(); PeriodId = Guid.NewGuid(); var chartId = Guid.NewGuid(); ArAccountId = Guid.NewGuid(); RevenueAccountId = Guid.NewGuid(); TaxAccountId = Guid.NewGuid(); BankAccountId = Guid.NewGuid(); WriteOffAccountId = Guid.NewGuid(); BookId = Guid.NewGuid(); JournalId = Guid.NewGuid(); var sequenceId = Guid.NewGuid();
			UserId = Database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ('finance-ar@depot.test','Finance AR','test',0,0,0,1,'2026-08-28T00:00:00.0000000Z');");
			CustomerId = Database.Insert("INSERT INTO Customers (CustomerNumber,Name,PaymentTermsDays,Currency,IsActive,Version) VALUES ('CU-AR','AR Customer',30,'USD',1,1);");
			Database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('USD','US Dollar',2,1);");
			Database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,'TEST','Test Entity','US','USD',1);", new DatabaseParameter("$Id", LegalEntityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$LegalEntityId,'CAL','Calendar',1);", new DatabaseParameter("$Id", CalendarId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$CalendarId,'2026-08','2026-07-01','2026-08-31',0);", new DatabaseParameter("$Id", PeriodId.ToString("D")), new DatabaseParameter("$CalendarId", CalendarId.ToString("D")));
			Database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,'COA','Chart',1);", new DatabaseParameter("$Id", chartId.ToString("D")));
			InsertAccount(ArAccountId, chartId, "1100", "Accounts Receivable", FinanceAccountType.Asset); InsertAccount(RevenueAccountId, chartId, "4000", "Revenue", FinanceAccountType.Revenue); InsertAccount(TaxAccountId, chartId, "2100", "Tax", FinanceAccountType.Liability); InsertAccount(BankAccountId, chartId, "1000", "Bank", FinanceAccountType.Asset); InsertAccount(WriteOffAccountId, chartId, "6900", "Write-off", FinanceAccountType.Expense);
			Database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$LegalEntityId,$ChartId,'PRIMARY','Primary book','USD','TEST',1,1);", new DatabaseParameter("$Id", BookId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$ChartId", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$BookId,'AR','Accounts Receivable',1);", new DatabaseParameter("$Id", JournalId.ToString("D")), new DatabaseParameter("$BookId", BookId.ToString("D")));
			Database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$LegalEntityId,'GL',$DocumentType,'GL-',6,1,1);", new DatabaseParameter("$Id", sequenceId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$DocumentType", FinanceNumberSequenceDocumentTypes.GeneralLedger));
			InvoiceProfileId = InsertProfile("AR-INVOICE", FinanceReceivableSourceTypes.SalesInvoice, (ArAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Gross), (RevenueAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Net), (TaxAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Tax));
			CreditProfileId = InsertProfile("AR-CREDIT", FinanceReceivableSourceTypes.SalesCreditNote, (RevenueAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Net), (TaxAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Tax), (ArAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Gross));
			PaymentProfileId = InsertProfile("AR-PAYMENT", FinanceReceivableSourceTypes.Payment, (BankAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Payment), (ArAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Payment));
			WriteOffProfileId = InsertProfile("AR-WRITEOFF", FinanceReceivableSourceTypes.WriteOff, (WriteOffAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.WriteOff), (ArAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.WriteOff));
			Database.Execute("INSERT INTO FinanceReceivablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,WriteOffPostingProfileId,IsActive) VALUES (1,$LegalEntityId,$CalendarId,$Invoice,$Credit,$Payment,$WriteOff,1);", new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$CalendarId", CalendarId.ToString("D")), new DatabaseParameter("$Invoice", InvoiceProfileId), new DatabaseParameter("$Credit", CreditProfileId), new DatabaseParameter("$Payment", PaymentProfileId), new DatabaseParameter("$WriteOff", WriteOffProfileId));
		}

		private void InsertAccount(Guid id, Guid chartId, string number, string name, FinanceAccountType type) => Database.Execute("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$ChartId,$Number,$Name,$Type,1,1);", new DatabaseParameter("$Id", id.ToString("D")), new DatabaseParameter("$ChartId", chartId.ToString("D")), new DatabaseParameter("$Number", number), new DatabaseParameter("$Name", name), new DatabaseParameter("$Type", (int)type));

		private long InsertProfile(string code, string sourceType, params (Guid AccountId, FinancePostingDirection Direction, string AmountKey)[] lines)
		{
			var id = Database.Insert("INSERT INTO FinancePostingProfiles (Version,LegalEntityId,AccountingBookId,JournalId,Code,Name,SourceType,SourceEvent,NumberSequenceCode,IsActive) VALUES (1,$LegalEntityId,$BookId,$JournalId,$Code,$Name,$SourceType,'Posted','GL',1);", new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$BookId", BookId.ToString("D")), new DatabaseParameter("$JournalId", JournalId.ToString("D")), new DatabaseParameter("$Code", code), new DatabaseParameter("$Name", code), new DatabaseParameter("$SourceType", sourceType));
			for (var index = 0; index < lines.Length; index++) { var line = lines[index]; Database.Execute("INSERT INTO FinancePostingProfileLines (PostingProfileId,LineNumber,AccountId,Direction,AmountKey,Multiplier,Description) VALUES ($ProfileId,$LineNumber,$AccountId,$Direction,$AmountKey,1,NULL);", new DatabaseParameter("$ProfileId", id), new DatabaseParameter("$LineNumber", index + 1), new DatabaseParameter("$AccountId", line.AccountId.ToString("D")), new DatabaseParameter("$Direction", (int)line.Direction), new DatabaseParameter("$AmountKey", line.AmountKey)); }
			return id;
		}

		public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_databasePath)) File.Delete(_databasePath); }
	}
}
