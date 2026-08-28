// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceAccountsPayableTests
{
	[Fact]
	public void FinanceMigrationCreatesAccountsPayableSchemaVersionFour()
	{
		using var context = FinanceApTestContext.Create();
		Assert.Equal(4L, context.Scalar("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceSupplierDocuments';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinancePayableOpenItems';"));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinancePayablePayments';"));
	}

	[Fact]
	public async Task SupplierInvoicePostsAtomicallyToPayablesAndBalancedGeneralLedger()
	{
		using var context = FinanceApTestContext.Create();
		var creator = context.CreateService(context.CreatorUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesSubmit);
		var approver = context.CreateService(context.ApproverUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesApprove);
		var poster = context.CreateService(context.CreatorUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesPost);

		var draft = await creator.SaveDraftAsync(context.InvoiceDraft("SUP-INV-001", 100m, 20m));
		var submitted = await creator.SubmitAsync(draft.Id, draft.Version);
		var approved = await approver.DecideAsync(submitted.Id, new FinanceSupplierApprovalRequest { ExpectedVersion = submitted.Version, Approve = true, Comment = "Validated" });
		var operationId = Guid.NewGuid();
		var posted = await poster.PostAsync(approved.Id, new FinanceSupplierPostingRequest { OperationId = operationId, ExpectedVersion = approved.Version });

		Assert.Equal(FinancePayableDocumentStatus.Posted, posted.Status);
		Assert.NotNull(posted.OpenItemId);
		Assert.NotNull(posted.JournalEntryId);
		var openItem = await poster.GetOpenItemAsync(posted.OpenItemId!.Value);
		Assert.NotNull(openItem);
		Assert.Equal(120m, openItem!.OriginalAmount);
		Assert.Equal(120m, openItem.RemainingAmount);
		Assert.Equal(FinancePayableDirection.Credit, openItem.Direction);
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$OperationId;", new DatabaseParameter("$OperationId", operationId.ToString("D"))));
		Assert.Equal(120m, context.DecimalScalar("SELECT SUM(TransactionDebit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", posted.JournalEntryId!.Value)));
		Assert.Equal(120m, context.DecimalScalar("SELECT SUM(TransactionCredit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", posted.JournalEntryId!.Value)));
	}

	[Fact]
	public async Task ThreeWayMatchFailsClosedAndRequiresSeparateExceptionPermission()
	{
		using var context = FinanceApTestContext.Create();
		var poLineId = await context.CreatePurchaseOrderLineAsync(10, 100m);
		var creator = context.CreateService(context.CreatorUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesSubmit);
		var normalApprover = context.CreateService(context.ApproverUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesApprove);
		var exceptionApprover = context.CreateService(context.ApproverUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesApprove, ApplicationPermission.FinanceSupplierMatchExceptionsApprove);

		var draft = await creator.SaveDraftAsync(new FinanceSupplierDocumentDraft
		{
			Kind = FinancePayableDocumentKind.Invoice,
			SupplierId = context.SupplierId,
			SupplierDocumentNumber = "MATCH-001",
			DocumentDate = context.PostingDate,
			DueDate = context.PostingDate.AddDays(30),
			Currency = new CurrencyCode("USD"),
			Lines = [new FinanceSupplierDocumentLineDraft { PurchaseOrderLineId = poLineId, Description = "PO item", Quantity = 2m, UnitPrice = 100m, TaxAmount = 0m }]
		});
		Assert.Equal(FinancePayableMatchStatus.Exception, Assert.Single(draft.Lines).MatchStatus);
		var submitted = await creator.SubmitAsync(draft.Id, draft.Version);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => normalApprover.DecideAsync(submitted.Id, new FinanceSupplierApprovalRequest { ExpectedVersion = submitted.Version, Approve = true, ApproveMatchException = true, MatchExceptionReason = "No receipt posted" }));

		var approved = await exceptionApprover.DecideAsync(submitted.Id, new FinanceSupplierApprovalRequest { ExpectedVersion = submitted.Version, Approve = true, ApproveMatchException = true, MatchExceptionReason = "Approved documented exception" });
		Assert.True(approved.MatchExceptionApproved);
		Assert.Equal(FinancePayableDocumentStatus.Approved, approved.Status);
	}

	[Fact]
	public async Task SupplierOverpaymentAndPaymentReversalRestoreAllocations()
	{
		using var context = FinanceApTestContext.Create();
		var invoice = await context.CreateAndPostInvoiceAsync("SUP-INV-PAY", 100m, 20m);
		var payments = context.CreateService(context.CreatorUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinancePayablePaymentsPost, ApplicationPermission.FinancePayablePaymentsReverse);
		var operationId = Guid.NewGuid();
		var paymentRequest = new FinancePayablePaymentRequest
		{
			OperationId = operationId,
			SupplierId = context.SupplierId,
			Currency = new CurrencyCode("USD"),
			PaymentDate = context.PostingDate,
			Amount = 150m,
			Reference = "BANK-AP-001",
			Description = "Supplier payment",
			Allocations = [new FinancePayableAllocationRequest(invoice.OpenItemId!.Value, 120m)]
		};
		var payment = await payments.PostPaymentAsync(paymentRequest);
		var retry = await payments.PostPaymentAsync(paymentRequest);
		Assert.Equal(payment.Id, retry.Id);
		Assert.Equal(0m, (await payments.GetOpenItemAsync(invoice.OpenItemId.Value))!.RemainingAmount);
		Assert.Equal(30m, (await payments.GetOpenItemAsync(payment.OpenItemId))!.RemainingAmount);

		var reversed = await payments.ReversePaymentAsync(payment.Id, new FinancePayableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = context.PostingDate, Reason = "Payment returned" });
		Assert.True(reversed.IsReversed);
		Assert.Equal(120m, (await payments.GetOpenItemAsync(invoice.OpenItemId.Value))!.RemainingAmount);
		var reversedDebit = await payments.GetOpenItemAsync(payment.OpenItemId);
		Assert.NotNull(reversedDebit);
		Assert.True(reversedDebit!.IsVoided);
		Assert.Equal(0m, reversedDebit.RemainingAmount);
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalReversals WHERE OriginalEntryId=$Id;", new DatabaseParameter("$Id", payment.JournalEntryId)));
	}

	[Fact]
	public void FinanceRoleGetsOperationalPayablesButNotApprovalOrMatchExceptionAuthority()
	{
		var role = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.FinancePayablesView, role.Permissions);
		Assert.Contains(ApplicationPermission.FinancePayablesManage, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceSupplierInvoicesCreate, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceSupplierInvoicesSubmit, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceSupplierInvoicesPost, role.Permissions);
		Assert.Contains(ApplicationPermission.FinancePayablePaymentsPost, role.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierInvoicesApprove, role.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierMatchExceptionsApprove, role.Permissions);
		Assert.Contains(ApplicationPermission.FinanceSupplierInvoicesApprove, PermissionCatalog.All);
		Assert.Contains(ApplicationPermission.FinanceSupplierMatchExceptionsApprove, PermissionCatalog.All);
	}

	[Fact]
	public void F3RecordsAreClassifiedAsRetainedAccountingEvidence()
	{
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinanceSupplierDocument)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinancePayableOpenItem)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant, BusinessRecordCatalog.Require(nameof(FinancePayablePayment)).RetentionCategory);
	}

	private sealed class FinanceApTestContext : IDisposable
	{
		private readonly string _databasePath;
		private FinanceApTestContext(string databasePath, DatabaseAccess database) { _databasePath = databasePath; Database = database; Transactions = new DatabaseTransactionRunner(database); }
		public DatabaseAccess Database { get; }
		public DatabaseTransactionRunner Transactions { get; }
		public long CreatorUserId { get; private set; }
		public long ApproverUserId { get; private set; }
		public long SupplierId { get; private set; }
		public Guid LegalEntityId { get; private set; }
		public Guid CalendarId { get; private set; }
		public Guid PeriodId { get; private set; }
		public Guid BookId { get; private set; }
		public Guid JournalId { get; private set; }
		public Guid ApAccountId { get; private set; }
		public Guid ExpenseAccountId { get; private set; }
		public Guid TaxAccountId { get; private set; }
		public Guid BankAccountId { get; private set; }
		public long InvoiceProfileId { get; private set; }
		public long CreditProfileId { get; private set; }
		public long PaymentProfileId { get; private set; }
		public DateOnly PostingDate { get; } = new(2026, 8, 28);

		public static FinanceApTestContext Create()
		{
			var path = Path.Combine(Path.GetTempPath(), $"depot-finance-ap-{Guid.NewGuid():N}.db");
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceAccountsPayableSchemaMigration.Migrate(factory);
			var context = new FinanceApTestContext(path, new DatabaseAccess(factory));
			context.Seed();
			return context;
		}

		public FinanceAccountsPayableService CreateService(long userId, params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = userId, Email = $"finance-ap-{userId}@depot.test", DisplayName = "Finance AP", IsActive = true }, permissions);
			var auditRepository = new AuditRepository(Database);
			var audit = new AuditService(auditRepository, authorization);
			var generalLedger = new FinanceGeneralLedgerService(Transactions, new FinanceGeneralLedgerRepository(Database), new FinancePostingProfileRepository(Database), auditRepository, audit, authorization);
			return new FinanceAccountsPayableService(Transactions, new FinanceAccountsPayableRepository(Database), generalLedger, auditRepository, audit, authorization);
		}

		public FinanceSupplierDocumentDraft InvoiceDraft(string number, decimal net, decimal tax) => new()
		{
			Kind = FinancePayableDocumentKind.Invoice,
			SupplierId = SupplierId,
			SupplierDocumentNumber = number,
			DocumentDate = PostingDate,
			DueDate = PostingDate.AddDays(30),
			Currency = new CurrencyCode("USD"),
			Lines = [new FinanceSupplierDocumentLineDraft { Description = "Supplier service", Quantity = 1m, UnitPrice = net, TaxAmount = tax }]
		};

		public async Task<FinanceSupplierDocument> CreateAndPostInvoiceAsync(string number, decimal net, decimal tax)
		{
			var creator = CreateService(CreatorUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesSubmit);
			var approver = CreateService(ApproverUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesApprove);
			var poster = CreateService(CreatorUserId, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesPost);
			var draft = await creator.SaveDraftAsync(InvoiceDraft(number, net, tax));
			var submitted = await creator.SubmitAsync(draft.Id, draft.Version);
			var approved = await approver.DecideAsync(submitted.Id, new FinanceSupplierApprovalRequest { ExpectedVersion = submitted.Version, Approve = true });
			return await poster.PostAsync(approved.Id, new FinanceSupplierPostingRequest { OperationId = Guid.NewGuid(), ExpectedVersion = approved.Version });
		}

		public async Task<long> CreatePurchaseOrderLineAsync(int quantity, decimal unitPrice)
		{
			var itemId = await new ItemRepository(Database).CreateAsync(new Item { PartNumber = $"AP-{Guid.NewGuid():N}", Description = "AP matching item", IsActive = true }, CancellationToken.None);
			var poId = Database.Insert("INSERT INTO PurchaseOrders (OrderNumber,SupplierId,OrderDate,Status,CreatedByUserId) VALUES ($Number,$SupplierId,$Date,$Status,$UserId);", new DatabaseParameter("$Number", $"PO-{Guid.NewGuid():N}"), new DatabaseParameter("$SupplierId", SupplierId), new DatabaseParameter("$Date", PostingDate.ToString("yyyy-MM-dd")), new DatabaseParameter("$Status", (int)PurchaseOrderStatus.Ordered), new DatabaseParameter("$UserId", CreatorUserId));
			return Database.Insert("INSERT INTO PurchaseOrderLines (PurchaseOrderId,LineNumber,ItemId,Quantity,UnitPrice) VALUES ($PurchaseOrderId,1,$ItemId,$Quantity,$UnitPrice);", new DatabaseParameter("$PurchaseOrderId", poId), new DatabaseParameter("$ItemId", itemId), new DatabaseParameter("$Quantity", quantity), new DatabaseParameter("$UnitPrice", unitPrice));
		}

		public long Scalar(string sql, params DatabaseParameter[] parameters) => Convert.ToInt64(Database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), CultureInfo.InvariantCulture);
		public decimal DecimalScalar(string sql, params DatabaseParameter[] parameters) => Convert.ToDecimal(Database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), CultureInfo.InvariantCulture);

		private void Seed()
		{
			LegalEntityId = Guid.NewGuid(); CalendarId = Guid.NewGuid(); PeriodId = Guid.NewGuid(); var chartId = Guid.NewGuid(); ApAccountId = Guid.NewGuid(); ExpenseAccountId = Guid.NewGuid(); TaxAccountId = Guid.NewGuid(); BankAccountId = Guid.NewGuid(); BookId = Guid.NewGuid(); JournalId = Guid.NewGuid(); var sequenceId = Guid.NewGuid();
			CreatorUserId = Database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ('finance-ap-creator@depot.test','Finance AP Creator','test',0,0,0,1,'2026-08-28T00:00:00.0000000Z');");
			ApproverUserId = Database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ('finance-ap-approver@depot.test','Finance AP Approver','test',0,0,0,1,'2026-08-28T00:00:00.0000000Z');");
			SupplierId = Database.Insert("INSERT INTO Suppliers (SupplierNumber,AccountNumber,Name,Loyalty,Quality,IsActive) VALUES ('SUP-AP',90001,'AP Supplier',100,100,1);");
			Database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('USD','US Dollar',2,1);");
			Database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,'TEST','Test Entity','US','USD',1);", new DatabaseParameter("$Id", LegalEntityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$LegalEntityId,'CAL','Calendar',1);", new DatabaseParameter("$Id", CalendarId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$CalendarId,'2026-08','2026-08-01','2026-08-31',0);", new DatabaseParameter("$Id", PeriodId.ToString("D")), new DatabaseParameter("$CalendarId", CalendarId.ToString("D")));
			Database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,'COA','Chart',1);", new DatabaseParameter("$Id", chartId.ToString("D")));
			InsertAccount(ApAccountId, chartId, "2000", "Accounts Payable", FinanceAccountType.Liability); InsertAccount(ExpenseAccountId, chartId, "6000", "Expense", FinanceAccountType.Expense); InsertAccount(TaxAccountId, chartId, "1400", "Input Tax", FinanceAccountType.Asset); InsertAccount(BankAccountId, chartId, "1000", "Bank", FinanceAccountType.Asset);
			Database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$LegalEntityId,$ChartId,'PRIMARY','Primary book','USD','TEST',1,1);", new DatabaseParameter("$Id", BookId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$ChartId", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$BookId,'AP','Accounts Payable',1);", new DatabaseParameter("$Id", JournalId.ToString("D")), new DatabaseParameter("$BookId", BookId.ToString("D")));
			Database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$LegalEntityId,'GL',$DocumentType,'GL-',6,1,1);", new DatabaseParameter("$Id", sequenceId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$DocumentType", FinanceNumberSequenceDocumentTypes.GeneralLedger));
			InvoiceProfileId = InsertProfile("AP-INVOICE", FinancePayableSourceTypes.SupplierInvoice, (ExpenseAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Net), (TaxAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Tax), (ApAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Gross));
			CreditProfileId = InsertProfile("AP-CREDIT", FinancePayableSourceTypes.SupplierCreditNote, (ApAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Gross), (ExpenseAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Net), (TaxAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Tax));
			PaymentProfileId = InsertProfile("AP-PAYMENT", FinancePayableSourceTypes.SupplierPayment, (ApAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Payment), (BankAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Payment));
			Database.Execute("INSERT INTO FinancePayablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,IsActive) VALUES (1,$LegalEntityId,$CalendarId,$Invoice,$Credit,$Payment,1);", new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$CalendarId", CalendarId.ToString("D")), new DatabaseParameter("$Invoice", InvoiceProfileId), new DatabaseParameter("$Credit", CreditProfileId), new DatabaseParameter("$Payment", PaymentProfileId));
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
