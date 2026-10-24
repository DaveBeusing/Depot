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

public sealed class FinanceJournalReversalOwnershipTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task PaymentReversalRemainsOwnedBySubledgerAndAtomicAfterStandaloneAttempt(bool accountsPayable)
	{
		using var context = PaymentContext.Create(accountsPayable);
		var invoiceId = await context.CreateInvoiceAsync();
		var payment = await context.PostPaymentAsync(invoiceId);
		var before = context.AccountingEvidence();

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.GeneralLedger.ReverseAsync(
			payment.JournalEntryId, Guid.NewGuid(), context.PeriodId, context.PostingDate, "GL", "Direct payment reversal"));

		Assert.Contains("source workflow", error.Message);
		Assert.Equal(before, context.AccountingEvidence());
		context.AssertPaymentIsSettled(invoiceId, payment.PaymentId, payment.OpenItemId);

		// Fail after the GL correction and allocation restoration to exercise the complete rollback.
		context.Database.Execute($"CREATE TRIGGER RejectPaymentReversalAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = '{context.PaymentEntityType}' AND NEW.Action = 'Reversed' BEGIN SELECT RAISE(ABORT, 'source reversal audit rejected'); END;");
		var operationId = Guid.NewGuid();
		var auditFailure = await Assert.ThrowsAsync<SqliteException>(() => context.ReversePaymentAsync(payment.PaymentId, operationId));
		Assert.Contains("source reversal audit rejected", auditFailure.Message);
		Assert.Equal(before, context.AccountingEvidence());
		context.AssertPaymentIsSettled(invoiceId, payment.PaymentId, payment.OpenItemId);

		context.Database.Execute("DROP TRIGGER RejectPaymentReversalAudit;");
		var reversalId = await context.ReversePaymentAsync(payment.PaymentId, operationId);
		var retryId = await context.ReversePaymentAsync(payment.PaymentId, operationId);

		Assert.Equal(reversalId, retryId);
		Assert.Equal(100m, context.DecimalScalar($"SELECT RemainingAmount FROM {context.OpenItemTable} WHERE Id=$Id;", invoiceId));
		Assert.Equal(1L, context.Scalar($"SELECT IsReversed FROM {context.PaymentTable} WHERE Id=$Id;", payment.PaymentId));
		Assert.Equal(1L, context.Scalar($"SELECT IsVoided FROM {context.OpenItemTable} WHERE Id=$Id;", payment.OpenItemId));
		Assert.Equal(0m, context.DecimalScalar($"SELECT RemainingAmount FROM {context.OpenItemTable} WHERE Id=$Id;", payment.OpenItemId));
		Assert.Equal(1L, context.Scalar($"SELECT COUNT(*) FROM {context.AllocationTable} WHERE ReversedAtUtc IS NOT NULL;"));
		Assert.Equal(reversalId, context.Scalar("SELECT ReversalEntryId FROM FinanceJournalReversals WHERE OriginalEntryId=$Id;", payment.JournalEntryId));
		Assert.Equal(before.Journals + 1, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(before.NextNumber + 1, context.Scalar("SELECT NextNumber FROM FinanceNumberSequences;"));
		Assert.Equal(before.AuditEntries + 3, context.Scalar("SELECT COUNT(*) FROM AuditEntries;"));
		Assert.Equal(0L, context.Scalar("SELECT COUNT(*) FROM (SELECT AccountId FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id OR JournalEntryId=$OtherId GROUP BY AccountId HAVING SUM(TransactionDebit-TransactionCredit)<>0 OR SUM(ReportingDebit-ReportingCredit)<>0) AS UnreversedAccounts;", payment.JournalEntryId, reversalId));

		var after = context.AccountingEvidence();
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.GeneralLedger.ReverseAsync(
			payment.JournalEntryId, operationId, context.PeriodId, context.PostingDate, "GL", "Payment correction"));
		Assert.Equal(after, context.AccountingEvidence());
	}

	private sealed class PaymentContext : IDisposable
	{
		private readonly string _databasePath;
		private readonly bool _accountsPayable;
		private readonly DatabaseTransactionRunner _transactions;
		private readonly FinanceAccountsPayableService _payables;
		private readonly FinanceAccountsReceivableService _receivables;
		private readonly Guid _entityId = Guid.NewGuid();
		private readonly Guid _calendarId = Guid.NewGuid();
		private readonly Guid _bookId = Guid.NewGuid();
		private readonly Guid _journalId = Guid.NewGuid();
		private readonly Guid _invoiceDebitAccountId = Guid.NewGuid();
		private readonly Guid _invoiceCreditAccountId = Guid.NewGuid();
		private readonly Guid _bankAccountId = Guid.NewGuid();
		private long _userId;
		private long _partnerId;
		private long _invoiceProfileId;

		private PaymentContext(string databasePath, DatabaseAccess database, bool accountsPayable)
		{
			_databasePath = databasePath;
			_accountsPayable = accountsPayable;
			Database = database;
			_transactions = new DatabaseTransactionRunner(database);
			Seed();

			var ledgerAuthorization = Authorize(ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceGeneralLedgerReverse);
			var auditEntries = new AuditRepository(database);
			GeneralLedger = new FinanceGeneralLedgerService(_transactions, new FinanceGeneralLedgerRepository(database), new FinancePostingProfileRepository(database), auditEntries, new AuditService(auditEntries, ledgerAuthorization), ledgerAuthorization);

			var sourceAuthorization = Authorize(ApplicationPermission.FinancePayablesView, ApplicationPermission.FinancePayablePaymentsPost, ApplicationPermission.FinancePayablePaymentsReverse,
				ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceReceivablePaymentsPost, ApplicationPermission.FinanceReceivablePaymentsReverse);
			var sourceAudit = new AuditService(auditEntries, sourceAuthorization);
			var sourceLedger = new FinanceGeneralLedgerService(_transactions, new FinanceGeneralLedgerRepository(database), new FinancePostingProfileRepository(database), auditEntries, sourceAudit, sourceAuthorization);
			Assert.False(sourceLedger.CanReverse);
			_payables = new FinanceAccountsPayableService(_transactions, new FinanceAccountsPayableRepository(database), sourceLedger, auditEntries, sourceAudit, sourceAuthorization);
			_receivables = new FinanceAccountsReceivableService(_transactions, new FinanceAccountsReceivableRepository(database), sourceLedger, auditEntries, sourceAudit, sourceAuthorization);
		}

		public DatabaseAccess Database { get; }
		public FinanceGeneralLedgerService GeneralLedger { get; }
		public Guid PeriodId { get; } = Guid.NewGuid();
		public DateOnly PostingDate { get; } = new(2026, 8, 28);
		public string PaymentEntityType => _accountsPayable ? nameof(FinancePayablePayment) : nameof(FinanceReceivablePayment);
		public string PaymentTable => _accountsPayable ? "FinancePayablePayments" : "FinanceReceivablePayments";
		public string OpenItemTable => _accountsPayable ? "FinancePayableOpenItems" : "FinanceReceivableOpenItems";
		public string AllocationTable => _accountsPayable ? "FinancePayableAllocations" : "FinanceReceivableAllocations";

		public static PaymentContext Create(bool accountsPayable)
		{
			var path = Path.Combine(Path.GetTempPath(), $"depot-journal-ownership-{Guid.NewGuid():N}.db");
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceAccountsPayableSchemaMigration.Migrate(factory);
			return new PaymentContext(path, new DatabaseAccess(factory), accountsPayable);
		}

		public async Task<long> CreateInvoiceAsync()
		{
			var operationId = Guid.NewGuid();
			var journal = await GeneralLedger.PostFromProfileAsync(new FinanceProfilePostingRequest
			{
				OperationId = operationId, PostingProfileId = _invoiceProfileId, AccountingPeriodId = PeriodId,
				PostingDate = PostingDate, Description = "Opening invoice", SourceId = "INVOICE-1", SourceReference = "INVOICE-1",
				TransactionCurrency = new CurrencyCode("USD"), Amounts = new Dictionary<string, decimal> { ["Gross"] = 100m }
			});
			var partnerColumn = _accountsPayable ? "SupplierId" : "CustomerId";
			return Database.Insert($"INSERT INTO {OpenItemTable} (Version,LegalEntityId,AccountingBookId,{partnerColumn},Kind,SourceType,SourceId,SourceReference,DocumentDate,DueDate,CurrencyCode,OriginalAmount,RemainingAmount,JournalEntryId,OperationId,IsVoided,CreatedAtUtc,CreatedByUserId) VALUES (1,$Entity,$Book,$Partner,1,$Source,'INVOICE-1','INVOICE-1','2026-08-28','2026-08-28','USD',100,100,$Journal,$Operation,0,'2026-08-28T00:00:00.0000000Z',$User);",
				new DatabaseParameter("$Entity", _entityId.ToString("D")), new DatabaseParameter("$Book", _bookId.ToString("D")),
				new DatabaseParameter("$Partner", _partnerId), new DatabaseParameter("$Source", journal.SourceType),
				new DatabaseParameter("$Journal", journal.Id), new DatabaseParameter("$Operation", operationId.ToString("D")), new DatabaseParameter("$User", _userId));
		}

		public async Task<(long PaymentId, long OpenItemId, long JournalEntryId)> PostPaymentAsync(long invoiceId)
		{
			if (_accountsPayable)
			{
				var payment = await _payables.PostPaymentAsync(new FinancePayablePaymentRequest
				{
					OperationId = Guid.NewGuid(), SupplierId = _partnerId, Currency = new CurrencyCode("USD"), PaymentDate = PostingDate,
					Amount = 100m, Description = "Invoice payment", Allocations = [new FinancePayableAllocationRequest(invoiceId, 100m)]
				});
				return (payment.Id, payment.OpenItemId, payment.JournalEntryId);
			}

			var receivablePayment = await _receivables.PostPaymentAsync(new FinanceReceivablePaymentRequest
			{
				OperationId = Guid.NewGuid(), CustomerId = _partnerId, Currency = new CurrencyCode("USD"), PaymentDate = PostingDate,
				Amount = 100m, Description = "Invoice payment", Allocations = [new FinanceReceivableAllocationRequest(invoiceId, 100m)]
			});
			return (receivablePayment.Id, receivablePayment.OpenItemId, receivablePayment.JournalEntryId);
		}

		public async Task<long> ReversePaymentAsync(long paymentId, Guid operationId)
		{
			if (_accountsPayable)
			{
				var payment = await _payables.ReversePaymentAsync(paymentId, new FinancePayableReversalRequest { OperationId = operationId, PostingDate = PostingDate, Reason = "Payment correction" });
				return payment.ReversalJournalEntryId ?? throw new InvalidOperationException("The reversed payment has no journal correction.");
			}

			var receivablePayment = await _receivables.ReversePaymentAsync(paymentId, new FinanceReceivableReversalRequest { OperationId = operationId, PostingDate = PostingDate, Reason = "Payment correction" });
			return receivablePayment.ReversalJournalEntryId ?? throw new InvalidOperationException("The reversed payment has no journal correction.");
		}

		public void AssertPaymentIsSettled(long invoiceId, long paymentId, long paymentOpenItemId)
		{
			Assert.Equal(0m, DecimalScalar($"SELECT RemainingAmount FROM {OpenItemTable} WHERE Id=$Id;", invoiceId));
			Assert.Equal(0L, Scalar($"SELECT IsReversed FROM {PaymentTable} WHERE Id=$Id;", paymentId));
			Assert.Equal(0L, Scalar($"SELECT IsVoided FROM {OpenItemTable} WHERE Id=$Id;", paymentOpenItemId));
			Assert.Equal(0m, DecimalScalar($"SELECT RemainingAmount FROM {OpenItemTable} WHERE Id=$Id;", paymentOpenItemId));
			Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM {AllocationTable} WHERE ReversedAtUtc IS NULL;"));
		}

		public (long Journals, long ReversalLinks, long NextNumber, long AuditEntries) AccountingEvidence() => (
			Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"), Scalar("SELECT COUNT(*) FROM FinanceJournalReversals;"),
			Scalar("SELECT NextNumber FROM FinanceNumberSequences;"), Scalar("SELECT COUNT(*) FROM AuditEntries;"));

		public long Scalar(string sql, long? id = null, long? otherId = null) => Convert.ToInt64(Database.ExecuteScalarAsync(sql, CancellationToken.None,
			otherId.HasValue ? [new DatabaseParameter("$Id", id ?? throw new ArgumentException("A primary ID is required.", nameof(id))), new DatabaseParameter("$OtherId", otherId.Value)] : id.HasValue ? [new DatabaseParameter("$Id", id.Value)] : []).GetAwaiter().GetResult(), CultureInfo.InvariantCulture);

		public decimal DecimalScalar(string sql, long id) => Convert.ToDecimal(Database.ExecuteScalarAsync(sql, CancellationToken.None,
			new DatabaseParameter("$Id", id)).GetAwaiter().GetResult(), CultureInfo.InvariantCulture);

		private AuthorizationService Authorize(params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = _userId, Email = "journal-owner@depot.test", DisplayName = "Journal owner", IsActive = true }, permissions);
			return authorization;
		}

		private void Seed()
		{
			_userId = Database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ('journal-owner@depot.test','Journal owner','test',0,0,0,1,'2026-08-28T00:00:00.0000000Z');");
			_partnerId = _accountsPayable
				? Database.Insert("INSERT INTO Suppliers (SupplierNumber,AccountNumber,Name,Loyalty,Quality,IsActive) VALUES ('SUPPLIER',90001,'Supplier',100,100,1);")
				: Database.Insert("INSERT INTO Customers (CustomerNumber,Name,PaymentTermsDays,Currency,IsActive,Version) VALUES ('CUSTOMER','Customer',30,'USD',1,1);");
			Database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('USD','US Dollar',2,1);");
			Database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,'TEST','Test Entity','US','USD',1);", new DatabaseParameter("$Id", _entityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,'CAL','Calendar',1);", new DatabaseParameter("$Id", _calendarId.ToString("D")), new DatabaseParameter("$Entity", _entityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,'2026-08','2026-08-01','2026-08-31',0);", new DatabaseParameter("$Id", PeriodId.ToString("D")), new DatabaseParameter("$Calendar", _calendarId.ToString("D")));
			var chartId = Guid.NewGuid();
			Database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,'COA','Chart',1);", new DatabaseParameter("$Id", chartId.ToString("D")));
			InsertAccount(_invoiceDebitAccountId, chartId, "1100", _accountsPayable ? FinanceAccountType.Expense : FinanceAccountType.Asset);
			InsertAccount(_invoiceCreditAccountId, chartId, "2000", _accountsPayable ? FinanceAccountType.Liability : FinanceAccountType.Revenue);
			InsertAccount(_bankAccountId, chartId, "1000", FinanceAccountType.Asset);
			Database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,'PRIMARY','Primary book','USD','TEST',1,1);", new DatabaseParameter("$Id", _bookId.ToString("D")), new DatabaseParameter("$Entity", _entityId.ToString("D")), new DatabaseParameter("$Chart", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,'GJ','General Journal',1);", new DatabaseParameter("$Id", _journalId.ToString("D")), new DatabaseParameter("$Book", _bookId.ToString("D")));
			Database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$Entity,'GL',$Type,'GL-',6,1,1);", new DatabaseParameter("$Id", Guid.NewGuid().ToString("D")), new DatabaseParameter("$Entity", _entityId.ToString("D")), new DatabaseParameter("$Type", FinanceNumberSequenceDocumentTypes.GeneralLedger));

			_invoiceProfileId = InsertProfile("INVOICE", _accountsPayable ? FinancePayableSourceTypes.SupplierInvoice : FinanceReceivableSourceTypes.SalesInvoice, "Gross", _invoiceDebitAccountId, _invoiceCreditAccountId);
			var creditProfileId = InsertProfile("CREDIT", _accountsPayable ? FinancePayableSourceTypes.SupplierCreditNote : FinanceReceivableSourceTypes.SalesCreditNote, "Gross", _invoiceCreditAccountId, _invoiceDebitAccountId);
			var paymentProfileId = InsertProfile("PAYMENT", _accountsPayable ? FinancePayableSourceTypes.SupplierPayment : FinanceReceivableSourceTypes.Payment, "Payment",
				_accountsPayable ? _invoiceCreditAccountId : _bankAccountId, _accountsPayable ? _bankAccountId : _invoiceDebitAccountId);
			if (_accountsPayable)
				Database.Execute("INSERT INTO FinancePayablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,IsActive) VALUES (1,$Entity,$Calendar,$Invoice,$Credit,$Payment,1);", ConfigurationParameters(creditProfileId, paymentProfileId));
			else
			{
				var writeOffProfileId = InsertProfile("WRITEOFF", FinanceReceivableSourceTypes.WriteOff, "WriteOff", _invoiceCreditAccountId, _invoiceDebitAccountId);
				Database.Execute("INSERT INTO FinanceReceivablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,WriteOffPostingProfileId,IsActive) VALUES (1,$Entity,$Calendar,$Invoice,$Credit,$Payment,$WriteOff,1);", [.. ConfigurationParameters(creditProfileId, paymentProfileId), new DatabaseParameter("$WriteOff", writeOffProfileId)]);
			}
		}

		private DatabaseParameter[] ConfigurationParameters(long creditProfileId, long paymentProfileId) =>
		[
			new("$Entity", _entityId.ToString("D")), new("$Calendar", _calendarId.ToString("D")), new("$Invoice", _invoiceProfileId),
			new("$Credit", creditProfileId), new("$Payment", paymentProfileId)
		];

		private void InsertAccount(Guid accountId, Guid chartId, string number, FinanceAccountType type) => Database.Execute(
			"INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,$Number,$Number,$Type,1,1);",
			new DatabaseParameter("$Id", accountId.ToString("D")), new DatabaseParameter("$Chart", chartId.ToString("D")), new DatabaseParameter("$Number", number), new DatabaseParameter("$Type", (int)type));

		private long InsertProfile(string code, string sourceType, string amountKey, Guid debitAccountId, Guid creditAccountId)
		{
			var id = Database.Insert("INSERT INTO FinancePostingProfiles (Version,LegalEntityId,AccountingBookId,JournalId,Code,Name,SourceType,SourceEvent,NumberSequenceCode,IsActive) VALUES (1,$Entity,$Book,$Journal,$Code,$Code,$Source,'Posted','GL',1);", new DatabaseParameter("$Entity", _entityId.ToString("D")), new DatabaseParameter("$Book", _bookId.ToString("D")), new DatabaseParameter("$Journal", _journalId.ToString("D")), new DatabaseParameter("$Code", code), new DatabaseParameter("$Source", sourceType));
			Database.Execute("INSERT INTO FinancePostingProfileLines (PostingProfileId,LineNumber,AccountId,Direction,AmountKey,Multiplier) VALUES ($Id,1,$Debit,0,$Key,1),($Id,2,$Credit,1,$Key,1);", new DatabaseParameter("$Id", id), new DatabaseParameter("$Debit", debitAccountId.ToString("D")), new DatabaseParameter("$Credit", creditAccountId.ToString("D")), new DatabaseParameter("$Key", amountKey));
			return id;
		}

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(_databasePath)) File.Delete(_databasePath);
		}
	}
}
