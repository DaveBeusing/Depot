// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Full")]
public sealed class FinanceProviderAcceptanceTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerFinanceContract() => VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbFinanceContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlFinanceContract() => VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var context = FinanceContext.Create(factory);
		await context.VerifyGeneralLedgerAsync();
		await context.VerifyReceivablesAsync();
		await context.VerifyPayablesAsync();
		await context.VerifyInventoryValuationAsync();
	}

	private sealed class FinanceContext
	{
		private readonly DatabaseAccess _database;
		private readonly DatabaseTransactionRunner _transactions;
		private readonly string _suffix = Guid.NewGuid().ToString("N");
		private readonly DateOnly _postingDate = new(2026, 9, 7);
		private long _userId;
		private long _approverId;
		private long _customerId;
		private long _supplierId;
		private Guid _legalEntityId;
		private Guid _calendarId;
		private Guid _periodId;
		private Guid _chartId;
		private Guid _bookId;
		private Guid _journalId;
		private Guid _arAccountId;
		private Guid _apAccountId;
		private Guid _revenueAccountId;
		private Guid _expenseAccountId;
		private Guid _outputTaxAccountId;
		private Guid _inputTaxAccountId;
		private Guid _bankAccountId;
		private Guid _writeOffAccountId;
		private string _sequenceCode = string.Empty;

		private FinanceContext(IDatabaseConnectionFactory factory)
		{
			_database = new DatabaseAccess(factory);
			_transactions = new DatabaseTransactionRunner(_database);
			Seed();
		}

		public static FinanceContext Create(IDatabaseConnectionFactory factory) => new(factory);

		public async Task VerifyGeneralLedgerAsync()
		{
			var service = CreateGeneralLedgerService(_userId,
				ApplicationPermission.FinanceGeneralLedgerView,
				ApplicationPermission.FinanceGeneralLedgerPost,
				ApplicationPermission.FinanceManualJournalsPost,
				ApplicationPermission.FinanceGeneralLedgerReverse);
			var first = await service.PostAsync(PostingRequest(Guid.NewGuid(), $"GL-A-{_suffix}", 123.12m, 123.12m));
			Assert.Equal(123.12m, first.Lines.Sum(line => line.TransactionDebit));
			Assert.Equal(first.Lines.Sum(line => line.TransactionDebit), first.Lines.Sum(line => line.TransactionCredit));
			var idempotent = await service.PostAsync(PostingRequest(first.OperationId, $"GL-A-{_suffix}", 123.12m, 123.12m));
			Assert.Equal(first.Id, idempotent.Id);

			var beforeCount = Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;");
			await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(PostingRequest(Guid.NewGuid(), $"GL-BAD-{_suffix}", 10m, 9.99m)));
			Assert.Equal(beforeCount, Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));

			var operations = Enumerable.Range(0, 4).Select(index => service.PostAsync(PostingRequest(Guid.NewGuid(), $"GL-C-{index}-{_suffix}", 1m + index, 1m + index)));
			var posted = await Task.WhenAll(operations);
			Assert.Equal(4, posted.Select(value => value.EntryNumber).Distinct(StringComparer.Ordinal).Count());

			var reversal = await service.ReverseAsync(first.Id, Guid.NewGuid(), _periodId, _postingDate, _sequenceCode, "Provider acceptance reversal");
			Assert.Equal(first.Id, reversal.ReversalOfEntryId);
			Assert.Equal(first.Lines[0].TransactionDebit, reversal.Lines[0].TransactionCredit);
		}

		public async Task VerifyReceivablesAsync()
		{
			var service = CreateReceivablesService(_userId,
				ApplicationPermission.FinanceReceivablesView,
				ApplicationPermission.FinanceReceivablePaymentsPost,
				ApplicationPermission.FinanceReceivablePaymentsReverse);
			var invoice = new SalesInvoice
			{
				Id = 100001,
				InvoiceNumber = $"AR-{_suffix}",
				CustomerId = _customerId,
				CustomerName = "Provider AR Customer",
				InvoiceDate = new DateTime(2026, 9, 1),
				DueDate = new DateTime(2026, 9, 30),
				Currency = "USD",
				Status = SalesInvoiceStatus.Posted,
				Lines = [new SalesInvoiceLine { LineNumber = 1, Quantity = 1, UnitPrice = 100m, TaxRate = 20m }]
			};
			var openItem = await PostInvoiceSourceAsync(service, invoice) ?? throw new InvalidOperationException("AR source posting returned no open item.");
			Assert.Equal(120m, openItem.OriginalAmount);
			Assert.Equal(120m, openItem.RemainingAmount);
			Assert.Equal(120m, DecimalScalar("SELECT SUM(TransactionDebit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", openItem.JournalEntryId)));
			Assert.Equal(120m, DecimalScalar("SELECT SUM(TransactionCredit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", openItem.JournalEntryId)));

			var paymentRequest = new FinanceReceivablePaymentRequest
			{
				OperationId = Guid.NewGuid(), CustomerId = _customerId, Currency = new CurrencyCode("USD"), PaymentDate = _postingDate,
				Amount = 120m, Reference = $"BANK-AR-{_suffix}", Description = "Provider AR payment",
				Allocations = [new FinanceReceivableAllocationRequest(openItem.Id, 120m)]
			};
			var payment = await service.PostPaymentAsync(paymentRequest);
			Assert.Equal(0m, (await service.GetOpenItemAsync(openItem.Id))!.RemainingAmount);
			var retry = await service.PostPaymentAsync(paymentRequest);
			Assert.Equal(payment.Id, retry.Id);
			var reversed = await service.ReversePaymentAsync(payment.Id, new FinanceReceivableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = _postingDate, Reason = "Provider reversal" });
			Assert.True(reversed.IsReversed);
			Assert.Equal(120m, (await service.GetOpenItemAsync(openItem.Id))!.RemainingAmount);
		}

		public async Task VerifyPayablesAsync()
		{
			var creator = CreatePayablesService(_userId,
				ApplicationPermission.FinancePayablesView,
				ApplicationPermission.FinanceSupplierInvoicesCreate,
				ApplicationPermission.FinanceSupplierInvoicesSubmit);
			var approver = CreatePayablesService(_approverId,
				ApplicationPermission.FinancePayablesView,
				ApplicationPermission.FinanceSupplierInvoicesApprove,
				ApplicationPermission.FinanceSupplierMatchExceptionsApprove);
			var poster = CreatePayablesService(_userId,
				ApplicationPermission.FinancePayablesView,
				ApplicationPermission.FinanceSupplierInvoicesPost,
				ApplicationPermission.FinancePayablePaymentsPost,
				ApplicationPermission.FinancePayablePaymentsReverse);

			var draft = await creator.SaveDraftAsync(new FinanceSupplierDocumentDraft
			{
				Kind = FinancePayableDocumentKind.Invoice,
				SupplierId = _supplierId,
				SupplierDocumentNumber = $"AP-{_suffix}",
				DocumentDate = _postingDate,
				DueDate = _postingDate.AddDays(30),
				Currency = new CurrencyCode("USD"),
				Lines = [new FinanceSupplierDocumentLineDraft { Description = "Provider supplier service", Quantity = 1m, UnitPrice = 100m, TaxAmount = 20m }]
			});
			var submitted = await creator.SubmitAsync(draft.Id, draft.Version);
			var approved = await approver.DecideAsync(submitted.Id, new FinanceSupplierApprovalRequest { ExpectedVersion = submitted.Version, Approve = true, Comment = "Provider approval" });
			var posted = await poster.PostAsync(approved.Id, new FinanceSupplierPostingRequest { OperationId = Guid.NewGuid(), ExpectedVersion = approved.Version });
			Assert.Equal(FinancePayableDocumentStatus.Posted, posted.Status);
			Assert.NotNull(posted.OpenItemId);
			Assert.NotNull(posted.JournalEntryId);
			Assert.Equal(120m, DecimalScalar("SELECT SUM(TransactionDebit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", posted.JournalEntryId!.Value)));
			Assert.Equal(120m, DecimalScalar("SELECT SUM(TransactionCredit) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;", new DatabaseParameter("$Id", posted.JournalEntryId!.Value)));

			var payment = await poster.PostPaymentAsync(new FinancePayablePaymentRequest
			{
				OperationId = Guid.NewGuid(), SupplierId = _supplierId, Currency = new CurrencyCode("USD"), PaymentDate = _postingDate,
				Amount = 120m, Reference = $"BANK-AP-{_suffix}", Description = "Provider AP payment",
				Allocations = [new FinancePayableAllocationRequest(posted.OpenItemId!.Value, 120m)]
			});
			Assert.Equal(0m, (await poster.GetOpenItemAsync(posted.OpenItemId.Value))!.RemainingAmount);
			var reversed = await poster.ReversePaymentAsync(payment.Id, new FinancePayableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = _postingDate, Reason = "Provider reversal" });
			Assert.True(reversed.IsReversed);
			Assert.Equal(120m, (await poster.GetOpenItemAsync(posted.OpenItemId.Value))!.RemainingAmount);

			var poLineId = await CreatePurchaseOrderLineAsync();
			var matchDraft = await creator.SaveDraftAsync(new FinanceSupplierDocumentDraft
			{
				Kind = FinancePayableDocumentKind.Invoice, SupplierId = _supplierId, SupplierDocumentNumber = $"MATCH-{_suffix}",
				DocumentDate = _postingDate, DueDate = _postingDate.AddDays(30), Currency = new CurrencyCode("USD"),
				Lines = [new FinanceSupplierDocumentLineDraft { PurchaseOrderLineId = poLineId, Description = "Unreceived PO item", Quantity = 2m, UnitPrice = 50m, TaxAmount = 0m }]
			});
			Assert.Equal(FinancePayableMatchStatus.Exception, Assert.Single(matchDraft.Lines).MatchStatus);
			var matchSubmitted = await creator.SubmitAsync(matchDraft.Id, matchDraft.Version);
			var matchApproved = await approver.DecideAsync(matchSubmitted.Id, new FinanceSupplierApprovalRequest
			{
				ExpectedVersion = matchSubmitted.Version, Approve = true, ApproveMatchException = true, MatchExceptionReason = "Provider acceptance documented exception"
			});
			Assert.True(matchApproved.MatchExceptionApproved);
		}

		public async Task VerifyInventoryValuationAsync()
		{
			var itemId = await new ItemRepository(_database).CreateAsync(new Item { PartNumber = $"FIFO-{_suffix}", Description = "FIFO provider item", IsActive = true }, CancellationToken.None);
			var journal = await CreateGeneralLedgerService(_userId, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceManualJournalsPost)
				.PostAsync(PostingRequest(Guid.NewGuid(), $"FIFO-J-{_suffix}", 50m, 50m));
			var layerId = _database.Insert(
				"INSERT INTO FinanceInventoryValuationLayers (AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId) VALUES ($Book,$Item,100,$Date,'USD',10,2,5,$At,$UserId);",
				new DatabaseParameter("$Book", _bookId.ToString("D")), new DatabaseParameter("$Item", itemId), new DatabaseParameter("$Date", "2026-01-10"),
				new DatabaseParameter("$At", "2026-01-10T10:00:00.0000000Z"), new DatabaseParameter("$UserId", _userId));
			_database.Execute(
				"INSERT INTO FinanceInventoryAccountingEvents (MovementId,Kind,AccountingBookId,ItemId,Quantity,CurrencyCode,Amount,JournalEntryId,OperationId,ReversalOfMovementId,CreatedAtUtc,CreatedByUserId) VALUES (100,1,$Book,$Item,10,'USD',50,$Journal,$Operation,NULL,$At,$UserId);",
				new DatabaseParameter("$Book", _bookId.ToString("D")), new DatabaseParameter("$Item", itemId), new DatabaseParameter("$Journal", journal.Id),
				new DatabaseParameter("$Operation", Guid.NewGuid().ToString("D")), new DatabaseParameter("$At", "2026-01-10T10:00:00.0000000Z"), new DatabaseParameter("$UserId", _userId));
			_database.Execute(
				"INSERT INTO FinanceInventoryValuationConsumptions (MovementId,LayerId,Quantity,UnitCost,Amount,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId) VALUES (200,$Layer,8,5,40,$Created,$UserId,$Reversed,$UserId);",
				new DatabaseParameter("$Layer", layerId), new DatabaseParameter("$Created", "2026-03-01T10:00:00.0000000Z"), new DatabaseParameter("$Reversed", "2026-04-01T10:00:00.0000000Z"), new DatabaseParameter("$UserId", _userId));
			var repository = new FinanceInventoryCostingRepository(_database);
			var before = await _transactions.ExecuteAsync((transaction, token) => repository.GetValuationReportingRowsAsync(transaction, _bookId, new DateOnly(2026, 2, 28), token));
			var consumed = await _transactions.ExecuteAsync((transaction, token) => repository.GetValuationReportingRowsAsync(transaction, _bookId, new DateOnly(2026, 3, 15), token));
			var reversed = await _transactions.ExecuteAsync((transaction, token) => repository.GetValuationReportingRowsAsync(transaction, _bookId, new DateOnly(2026, 4, 15), token));
			Assert.Equal(10, Assert.Single(before, value => value.ItemId == itemId).Quantity);
			Assert.Equal(2, Assert.Single(consumed, value => value.ItemId == itemId).Quantity);
			Assert.Equal(10, Assert.Single(reversed, value => value.ItemId == itemId).Quantity);
		}

		private FinanceGeneralLedgerService CreateGeneralLedgerService(long userId, params ApplicationPermission[] permissions)
		{
			var authorization = Authorization(userId, permissions);
			var auditRepository = new AuditRepository(_database);
			return new FinanceGeneralLedgerService(_transactions, new FinanceGeneralLedgerRepository(_database), new FinancePostingProfileRepository(_database), auditRepository, new AuditService(auditRepository, authorization), authorization);
		}

		private FinanceAccountsReceivableService CreateReceivablesService(long userId, params ApplicationPermission[] permissions)
		{
			var authorization = Authorization(userId, permissions);
			var auditRepository = new AuditRepository(_database);
			var audit = new AuditService(auditRepository, authorization);
			var generalLedger = new FinanceGeneralLedgerService(_transactions, new FinanceGeneralLedgerRepository(_database), new FinancePostingProfileRepository(_database), auditRepository, audit, authorization);
			return new FinanceAccountsReceivableService(_transactions, new FinanceAccountsReceivableRepository(_database), generalLedger, auditRepository, audit, authorization);
		}

		private FinanceAccountsPayableService CreatePayablesService(long userId, params ApplicationPermission[] permissions)
		{
			var authorization = Authorization(userId, permissions);
			var auditRepository = new AuditRepository(_database);
			var audit = new AuditService(auditRepository, authorization);
			var generalLedger = new FinanceGeneralLedgerService(_transactions, new FinanceGeneralLedgerRepository(_database), new FinancePostingProfileRepository(_database), auditRepository, audit, authorization);
			return new FinanceAccountsPayableService(_transactions, new FinanceAccountsPayableRepository(_database), generalLedger, auditRepository, audit, authorization);
		}

		private AuthorizationService Authorization(long userId, IReadOnlyCollection<ApplicationPermission> permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = userId, Email = $"finance-provider-{userId}@depot.test", DisplayName = "Finance provider", IsActive = true }, permissions);
			return authorization;
		}

		private FinancePostingRequest PostingRequest(Guid operationId, string sourceId, decimal debit, decimal credit) => new()
		{
			OperationId = operationId, AccountingBookId = _bookId, JournalId = _journalId, AccountingPeriodId = _periodId, NumberSequenceCode = _sequenceCode,
			PostingDate = _postingDate, Description = "Provider finance acceptance", SourceType = "ProviderAcceptance", SourceId = sourceId, SourceEvent = "Posted",
			TransactionCurrency = new CurrencyCode("USD"), EntryKind = FinanceJournalEntryKind.Manual,
			Lines = [new FinancePostingLine { AccountId = _expenseAccountId, Debit = debit }, new FinancePostingLine { AccountId = _apAccountId, Credit = credit }]
		};

		private Task<FinanceReceivableOpenItem?> PostInvoiceSourceAsync(FinanceAccountsReceivableService service, SalesInvoice invoice) => _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var method = typeof(FinanceAccountsReceivableService).GetMethod("TryPostSalesInvoiceAsync", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException(nameof(FinanceAccountsReceivableService), "TryPostSalesInvoiceAsync");
			var task = (Task<FinanceReceivableOpenItem?>)(method.Invoke(service, [transaction, invoice, _userId, token]) ?? throw new InvalidOperationException("AR source posting did not return a task."));
			return await task;
		});

		private async Task<long> CreatePurchaseOrderLineAsync()
		{
			var itemId = await new ItemRepository(_database).CreateAsync(new Item { PartNumber = $"AP-MATCH-{_suffix}", Description = "AP matching item", IsActive = true }, CancellationToken.None);
			var poId = _database.Insert("INSERT INTO PurchaseOrders (OrderNumber,SupplierId,OrderDate,Status,CreatedByUserId) VALUES ($Number,$SupplierId,$Date,$Status,$UserId);",
				new DatabaseParameter("$Number", $"PO-{_suffix}"), new DatabaseParameter("$SupplierId", _supplierId), new DatabaseParameter("$Date", _postingDate.ToString("yyyy-MM-dd")),
				new DatabaseParameter("$Status", (int)PurchaseOrderStatus.Ordered), new DatabaseParameter("$UserId", _userId));
			return _database.Insert("INSERT INTO PurchaseOrderLines (PurchaseOrderId,LineNumber,ItemId,Quantity,UnitPrice) VALUES ($PurchaseOrderId,1,$ItemId,10,50);",
				new DatabaseParameter("$PurchaseOrderId", poId), new DatabaseParameter("$ItemId", itemId));
		}

		private void Seed()
		{
			_legalEntityId = Guid.NewGuid(); _calendarId = Guid.NewGuid(); _periodId = Guid.NewGuid(); _chartId = Guid.NewGuid(); _bookId = Guid.NewGuid(); _journalId = Guid.NewGuid();
			_arAccountId = Guid.NewGuid(); _apAccountId = Guid.NewGuid(); _revenueAccountId = Guid.NewGuid(); _expenseAccountId = Guid.NewGuid(); _outputTaxAccountId = Guid.NewGuid();
			_inputTaxAccountId = Guid.NewGuid(); _bankAccountId = Guid.NewGuid(); _writeOffAccountId = Guid.NewGuid(); _sequenceCode = $"G{_suffix[..10]}";
			_userId = _database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ($Email,'Finance Provider','test',0,0,0,1,$Created);",
				new DatabaseParameter("$Email", $"finance-{_suffix}@depot.test"), new DatabaseParameter("$Created", "2026-09-07T00:00:00.0000000Z"));
			_approverId = _database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ($Email,'Finance Approver','test',0,1,3,1,$Created);",
				new DatabaseParameter("$Email", $"approver-{_suffix}@depot.test"), new DatabaseParameter("$Created", "2026-09-07T00:00:00.0000000Z"));
			_customerId = _database.Insert("INSERT INTO Customers (CustomerNumber,Name,PaymentTermsDays,Currency,IsActive,Version) VALUES ($Number,'Provider AR Customer',30,'USD',1,1);", new DatabaseParameter("$Number", $"CU-{_suffix[..12]}"));
			_supplierId = _database.Insert("INSERT INTO Suppliers (SupplierNumber,AccountNumber,Name,Loyalty,Quality,IsActive) VALUES ($Number,$Account,'Provider AP Supplier',100,100,1);",
				new DatabaseParameter("$Number", $"SU-{_suffix[..12]}"), new DatabaseParameter("$Account", Math.Abs(DateTime.UtcNow.Ticks % 1000000000L)));
			_database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('USD','US Dollar',2,1);");
			_database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,'Provider Entity','US','USD',1);",
				new DatabaseParameter("$Id", _legalEntityId.ToString("D")), new DatabaseParameter("$Code", $"LE-{_suffix[..8]}"));
			_database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$LegalEntityId,$Code,'Provider Calendar',1);",
				new DatabaseParameter("$Id", _calendarId.ToString("D")), new DatabaseParameter("$LegalEntityId", _legalEntityId.ToString("D")), new DatabaseParameter("$Code", $"CAL-{_suffix[..8]}"));
			_database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$CalendarId,'2026-09','2026-09-01','2026-09-30',0);",
				new DatabaseParameter("$Id", _periodId.ToString("D")), new DatabaseParameter("$CalendarId", _calendarId.ToString("D")));
			_database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,$Code,'Provider Chart',1);", new DatabaseParameter("$Id", _chartId.ToString("D")), new DatabaseParameter("$Code", $"COA-{_suffix[..8]}"));
			InsertAccount(_arAccountId, "1100", "Accounts Receivable", FinanceAccountType.Asset); InsertAccount(_apAccountId, "2000", "Accounts Payable", FinanceAccountType.Liability);
			InsertAccount(_revenueAccountId, "4000", "Revenue", FinanceAccountType.Revenue); InsertAccount(_expenseAccountId, "6000", "Expense", FinanceAccountType.Expense);
			InsertAccount(_outputTaxAccountId, "2100", "Output Tax", FinanceAccountType.Liability); InsertAccount(_inputTaxAccountId, "1400", "Input Tax", FinanceAccountType.Asset);
			InsertAccount(_bankAccountId, "1000", "Bank", FinanceAccountType.Asset); InsertAccount(_writeOffAccountId, "6900", "Write-off", FinanceAccountType.Expense);
			_database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$LegalEntityId,$ChartId,$Code,'Provider Book','USD','TEST',1,1);",
				new DatabaseParameter("$Id", _bookId.ToString("D")), new DatabaseParameter("$LegalEntityId", _legalEntityId.ToString("D")), new DatabaseParameter("$ChartId", _chartId.ToString("D")), new DatabaseParameter("$Code", $"BOOK-{_suffix[..8]}"));
			_database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$BookId,$Code,'Provider Journal',1);",
				new DatabaseParameter("$Id", _journalId.ToString("D")), new DatabaseParameter("$BookId", _bookId.ToString("D")), new DatabaseParameter("$Code", $"J-{_suffix[..8]}"));
			_database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$LegalEntityId,$Code,$DocumentType,'GL-',8,1,1);",
				new DatabaseParameter("$Id", Guid.NewGuid().ToString("D")), new DatabaseParameter("$LegalEntityId", _legalEntityId.ToString("D")), new DatabaseParameter("$Code", _sequenceCode), new DatabaseParameter("$DocumentType", FinanceNumberSequenceDocumentTypes.GeneralLedger));

			var arInvoice = InsertProfile($"AR-I-{_suffix[..8]}", FinanceReceivableSourceTypes.SalesInvoice, (_arAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Gross), (_revenueAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Net), (_outputTaxAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Tax));
			var arCredit = InsertProfile($"AR-C-{_suffix[..8]}", FinanceReceivableSourceTypes.SalesCreditNote, (_revenueAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Net), (_outputTaxAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Tax), (_arAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Gross));
			var arPayment = InsertProfile($"AR-P-{_suffix[..8]}", FinanceReceivableSourceTypes.Payment, (_bankAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.Payment), (_arAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.Payment));
			var arWriteOff = InsertProfile($"AR-W-{_suffix[..8]}", FinanceReceivableSourceTypes.WriteOff, (_writeOffAccountId, FinancePostingDirection.Debit, FinanceReceivablePostingAmountKeys.WriteOff), (_arAccountId, FinancePostingDirection.Credit, FinanceReceivablePostingAmountKeys.WriteOff));
			_database.Execute("INSERT INTO FinanceReceivablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,WriteOffPostingProfileId,IsActive) VALUES (1,$LegalEntityId,$CalendarId,$Invoice,$Credit,$Payment,$WriteOff,1);",
				new DatabaseParameter("$LegalEntityId", _legalEntityId.ToString("D")), new DatabaseParameter("$CalendarId", _calendarId.ToString("D")), new DatabaseParameter("$Invoice", arInvoice), new DatabaseParameter("$Credit", arCredit), new DatabaseParameter("$Payment", arPayment), new DatabaseParameter("$WriteOff", arWriteOff));

			var apInvoice = InsertProfile($"AP-I-{_suffix[..8]}", FinancePayableSourceTypes.SupplierInvoice, (_expenseAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Net), (_inputTaxAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Tax), (_apAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Gross));
			var apCredit = InsertProfile($"AP-C-{_suffix[..8]}", FinancePayableSourceTypes.SupplierCreditNote, (_apAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Gross), (_expenseAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Net), (_inputTaxAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Tax));
			var apPayment = InsertProfile($"AP-P-{_suffix[..8]}", FinancePayableSourceTypes.SupplierPayment, (_apAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Payment), (_bankAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Payment));
			_database.Execute("INSERT INTO FinancePayablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,IsActive) VALUES (1,$LegalEntityId,$CalendarId,$Invoice,$Credit,$Payment,1);",
				new DatabaseParameter("$LegalEntityId", _legalEntityId.ToString("D")), new DatabaseParameter("$CalendarId", _calendarId.ToString("D")), new DatabaseParameter("$Invoice", apInvoice), new DatabaseParameter("$Credit", apCredit), new DatabaseParameter("$Payment", apPayment));
		}

		private void InsertAccount(Guid id, string number, string name, FinanceAccountType type) => _database.Execute(
			"INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$ChartId,$Number,$Name,$Type,1,1);",
			new DatabaseParameter("$Id", id.ToString("D")), new DatabaseParameter("$ChartId", _chartId.ToString("D")), new DatabaseParameter("$Number", number), new DatabaseParameter("$Name", name), new DatabaseParameter("$Type", (int)type));

		private long InsertProfile(string code, string sourceType, params (Guid AccountId, FinancePostingDirection Direction, string AmountKey)[] lines)
		{
			var id = _database.Insert("INSERT INTO FinancePostingProfiles (Version,LegalEntityId,AccountingBookId,JournalId,Code,Name,SourceType,SourceEvent,NumberSequenceCode,IsActive) VALUES (1,$LegalEntityId,$BookId,$JournalId,$Code,$Name,$SourceType,'Posted',$Sequence,1);",
				new DatabaseParameter("$LegalEntityId", _legalEntityId.ToString("D")), new DatabaseParameter("$BookId", _bookId.ToString("D")), new DatabaseParameter("$JournalId", _journalId.ToString("D")),
				new DatabaseParameter("$Code", code), new DatabaseParameter("$Name", code), new DatabaseParameter("$SourceType", sourceType), new DatabaseParameter("$Sequence", _sequenceCode));
			for (var index = 0; index < lines.Length; index++)
			{
				var line = lines[index];
				_database.Execute("INSERT INTO FinancePostingProfileLines (PostingProfileId,LineNumber,AccountId,Direction,AmountKey,Multiplier,Description) VALUES ($ProfileId,$LineNumber,$AccountId,$Direction,$AmountKey,1,NULL);",
					new DatabaseParameter("$ProfileId", id), new DatabaseParameter("$LineNumber", index + 1), new DatabaseParameter("$AccountId", line.AccountId.ToString("D")), new DatabaseParameter("$Direction", (int)line.Direction), new DatabaseParameter("$AmountKey", line.AmountKey));
			}
			return id;
		}

		private long Scalar(string sql, params DatabaseParameter[] parameters) => Convert.ToInt64(_database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);
		private decimal DecimalScalar(string sql, params DatabaseParameter[] parameters) => Convert.ToDecimal(_database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);
	}
}