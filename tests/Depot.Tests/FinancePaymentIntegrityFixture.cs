// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;

namespace Depot.Tests;

internal sealed class FinancePaymentIntegrityFixture : IDisposable
{
	private readonly string? _databasePath;
	private readonly FinancePayablesConfiguration? _previousConfiguration;
	private readonly long _configurationId;
	private readonly string _suffix = Guid.NewGuid().ToString("N");
	private readonly FinanceAccountsPayableService _approverPayables;
	private readonly FinanceBankingService _approverBanking;
	private readonly Guid _chartId = Guid.NewGuid();
	private readonly Guid _calendarId = Guid.NewGuid();
	private readonly Guid _periodId = Guid.NewGuid();
	private readonly Guid _journalId = Guid.NewGuid();
	private readonly Guid _payableAccountId = Guid.NewGuid();
	private readonly Guid _expenseAccountId = Guid.NewGuid();
	private readonly Guid _taxAccountId = Guid.NewGuid();

	private FinancePaymentIntegrityFixture(IDatabaseConnectionFactory factory, string? databasePath)
	{
		_databasePath = databasePath;
		DatabaseProvisioningService.Initialize(factory);
		Factory = new ObservedProviderConnectionFactory(factory);
		Database = new DatabaseAccess(Factory);
		Transactions = new ProviderTransactionGate(new DatabaseTransactionRunner(Database));
		_previousConfiguration = Database.Query("SELECT Id,Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,IsActive FROM FinancePayablesConfigurations;", reader => new FinancePayablesConfiguration
		{
			Id=reader.GetInt64(0),Version=reader.GetInt64(1),LegalEntityId=Guid.Parse(reader.GetString(2)),FiscalCalendarId=Guid.Parse(reader.GetString(3)),InvoicePostingProfileId=reader.GetInt64(4),CreditNotePostingProfileId=reader.GetInt64(5),PaymentPostingProfileId=reader.GetInt64(6),IsActive=Convert.ToInt32(reader.GetValue(7),CultureInfo.InvariantCulture)!=0
		}).SingleOrDefault();
		CreatorId = CreateUser("creator");
		var approverId = CreateUser("approver");
		SupplierId = Database.Insert("INSERT INTO Suppliers (SupplierNumber,AccountNumber,Name,Loyalty,Quality,IsActive) VALUES ($Number,$Account,$Name,100,100,1);", P("$Number", $"FI-{_suffix}"), P("$Account", -DateTime.UtcNow.Ticks), P("$Name", $"Financial integrity {_suffix}"));
		Database.Execute("UPDATE Suppliers SET AccountNumber=Id WHERE Id=$Id;", P("$Id", SupplierId));
		if (Scalar("SELECT COUNT(*) FROM FinanceCurrencies WHERE Code='EUR';") == 0) Database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('EUR','Euro',2,1);");
		Database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,'Financial integrity','DE','EUR',1);", P("$Id", EntityId.ToString("D")), P("$Code", $"LE-{_suffix[..8]}"));
		Database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,$Code,'Financial integrity',1);", P("$Id", _calendarId.ToString("D")), P("$Entity", EntityId.ToString("D")), P("$Code", $"CAL-{_suffix[..8]}"));
		Database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,$Code,$Start,$End,0);", P("$Id", _periodId.ToString("D")), P("$Calendar", _calendarId.ToString("D")), P("$Code", $"PER-{_suffix[..8]}"), P("$Start", Date(PostingDate.AddDays(-7))), P("$End", Date(PostingDate.AddDays(90))));
		Database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,$Code,'Financial integrity',1);", P("$Id", _chartId.ToString("D")), P("$Code", $"COA-{_suffix[..8]}"));
		CreateAccount(_payableAccountId, "2000", FinanceAccountType.Liability);
		CreateAccount(_expenseAccountId, "6000", FinanceAccountType.Expense);
		CreateAccount(_taxAccountId, "1400", FinanceAccountType.Asset);
		CreateAccount(BankGlA, "1000", FinanceAccountType.Asset);
		CreateAccount(BankGlB, "1010", FinanceAccountType.Asset);
		Database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,$Code,'Financial integrity','EUR','TEST',1,1);", P("$Id", BookId.ToString("D")), P("$Entity", EntityId.ToString("D")), P("$Chart", _chartId.ToString("D")), P("$Code", $"BOOK-{_suffix[..8]}"));
		Database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,$Code,'Financial integrity',1);", P("$Id", _journalId.ToString("D")), P("$Book", BookId.ToString("D")), P("$Code", $"J-{_suffix[..8]}"));
		Database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$Entity,'GL',$Type,$Prefix,6,1,1);", P("$Id", Guid.NewGuid().ToString("D")), P("$Entity", EntityId.ToString("D")), P("$Type", FinanceNumberSequenceDocumentTypes.GeneralLedger), P("$Prefix", $"FI-{_suffix[..8]}-"));
		var invoiceProfile = CreateProfile("INVOICE", FinancePayableSourceTypes.SupplierInvoice, (_expenseAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Net), (_taxAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Tax), (_payableAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Gross));
		var creditProfile = CreateProfile("CREDIT", FinancePayableSourceTypes.SupplierCreditNote, (_payableAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Gross), (_expenseAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Net), (_taxAccountId, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Tax));
		PaymentProfileA = CreateProfile("PAY-A", FinancePayableSourceTypes.SupplierPayment, (_payableAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Payment), (BankGlA, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Payment));
		var configuration = new FinancePayablesConfiguration { Id=_previousConfiguration?.Id ?? 0,Version=1,LegalEntityId=EntityId,FiscalCalendarId=_calendarId,InvoicePostingProfileId=invoiceProfile,CreditNotePostingProfileId=creditProfile,PaymentPostingProfileId=PaymentProfileA,IsActive=true };
		_configurationId = SaveConfiguration(configuration);
		var services = CreateServices(CreatorId);
		Payables = services.Payables;
		Banking = services.Banking;
		Sepa = services.Sepa;
		Ledger = services.Ledger;
		var approver = CreateServices(approverId);
		_approverPayables = approver.Payables;
		_approverBanking = approver.Banking;
	}

	public DatabaseAccess Database { get; }
	public ObservedProviderConnectionFactory Factory { get; }
	public ProviderTransactionGate Transactions { get; }
	public FinanceAccountsPayableService Payables { get; }
	public FinanceBankingService Banking { get; }
	public FinanceSepaPaymentExportService Sepa { get; }
	public FinanceGeneralLedgerService Ledger { get; }
	public Guid EntityId { get; } = Guid.NewGuid();
	public Guid BookId { get; } = Guid.NewGuid();
	public Guid BankGlA { get; } = Guid.NewGuid();
	public Guid BankGlB { get; } = Guid.NewGuid();
	public long CreatorId { get; }
	public long SupplierId { get; }
	public long PaymentProfileA { get; }
	public DateOnly PostingDate { get; } = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);

	public static FinancePaymentIntegrityFixture CreateSqlite()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-payment-integrity-{Guid.NewGuid():N}.db");
		return new FinancePaymentIntegrityFixture(new SqliteConnectionFactory(path), path);
	}

	public static FinancePaymentIntegrityFixture CreateProvider(IDatabaseConnectionFactory factory) => new(factory, null);

	public async Task<FinanceBankAccount> CreateBankAsync(bool second = false)
	{
		var bank = await Banking.SaveBankAccountAsync(new FinanceBankAccount
		{
			LegalEntityId=EntityId,AccountingBookId=BookId,GeneralLedgerAccountId=second ? BankGlB : BankGlA,Currency=new CurrencyCode("EUR"),
			Name=$"Bank {(second ? "B" : "A")} {_suffix[..8]}",Iban=second ? "DE12500105170648489890" : "DE89370400440532013000",Bic="COBADEFFXXX",IsActive=true
		});
		await Sepa.SaveDebtorProfileAsync(new FinanceSepaDebtorProfile { BankAccountId=bank.Id,Name="Financial integrity",StreetName="Teststrasse",BuildingNumber="1",PostalCode="53111",TownName="Bonn",CountryCode="DE" });
		await Sepa.SaveCreditorProfileAsync(new FinanceSepaCreditorProfile { SupplierId=SupplierId,Name="Fixture supplier",Iban="FR1420041010050500013M02606",Bic="BNPAFRPP",StreetName="Rue de Test",BuildingNumber="2",PostalCode="75001",TownName="Paris",CountryCode="FR",IsActive=true,Version=(await Sepa.GetCreditorProfileAsync(SupplierId))?.Version ?? 0 });
		return bank;
	}

	public long CreateBankBProfile() => CreateProfile("PAY-B", FinancePayableSourceTypes.SupplierPayment, (_payableAccountId, FinancePostingDirection.Debit, FinancePayablePostingAmountKeys.Payment), (BankGlB, FinancePostingDirection.Credit, FinancePayablePostingAmountKeys.Payment));

	public FinanceSupplierDocumentDraft Draft(params FinanceSupplierDocumentLineDraft[] lines) => new()
	{
		Kind=FinancePayableDocumentKind.Invoice,SupplierId=SupplierId,SupplierDocumentNumber=$"INV-{Guid.NewGuid():N}",DocumentDate=PostingDate,DueDate=PostingDate.AddDays(30),Currency=new CurrencyCode("EUR"),Lines=lines
	};

	public async Task<FinanceSupplierDocument> ApproveAsync(FinanceSupplierDocument document)
	{
		var submitted = await Payables.SubmitAsync(document.Id, document.Version);
		return await ApproveSubmittedAsync(submitted);
	}

	public Task<FinanceSupplierDocument> ApproveSubmittedAsync(FinanceSupplierDocument document) =>
		_approverPayables.DecideAsync(document.Id, new FinanceSupplierApprovalRequest { ExpectedVersion=document.Version,Approve=true });

	public async Task<FinanceSupplierDocument> CreateInvoiceAsync(decimal amount = 100m)
	{
		var draft = await Payables.SaveDraftAsync(Draft(new FinanceSupplierDocumentLineDraft { Description="Supplier service",Quantity=1m,UnitPrice=amount }));
		var approved = await ApproveAsync(draft);
		return await Payables.PostAsync(approved.Id, new FinanceSupplierPostingRequest { OperationId=Guid.NewGuid(),ExpectedVersion=approved.Version });
	}

	public async Task<FinancePaymentRun> CreateRunAsync(long bankAccountId, long openItemId, decimal amount = 100m)
	{
		var run = await Banking.CreatePaymentRunAsync(new FinancePaymentRunRequest { OperationId=Guid.NewGuid(),BankAccountId=bankAccountId,PaymentDate=PostingDate,Currency=new CurrencyCode("EUR"),Description="Financial integrity payment",Lines=[new FinancePaymentRunLineRequest(openItemId, amount, "Fixture invoice")] });
		return await _approverBanking.ApprovePaymentRunAsync(run.Id, run.Version, "Reviewed financial integrity payment");
	}

	public Task<FinancePayablePayment> PayAsync(long openItemId, decimal amount = 100m, long? bankAccountId = null) => Payables.PostPaymentAsync(new FinancePayablePaymentRequest
	{
		OperationId=Guid.NewGuid(),BankAccountId=bankAccountId,SupplierId=SupplierId,PaymentDate=PostingDate,Currency=new CurrencyCode("EUR"),Amount=amount,Description="Financial integrity payment",Allocations=[new FinancePayableAllocationRequest(openItemId, amount)]
	});

	public Task<FinancePayablePayment> PostLegacyRunPaymentAsync(FinancePaymentRun run)
	{
		var line = run.Lines.Single();
		return Payables.PostPaymentAsync(new FinancePayablePaymentRequest
		{
			OperationId=line.ExecutionOperationId,SupplierId=line.SupplierId,Currency=run.Currency,PaymentDate=run.PaymentDate,Amount=line.Amount,Reference=line.Reference,Description=$"Payment run {run.Id}: {run.Description}",Allocations=[new FinancePayableAllocationRequest(line.PayableOpenItemId,line.Amount)]
		});
	}

	public Task<FinanceBankStatement> ImportAsync(long bankAccountId, params decimal[] amounts) => Banking.ImportStatementAsync(new FinanceBankStatementImportRequest
	{
		OperationId=Guid.NewGuid(),BankAccountId=bankAccountId,Format=FinanceBankStatementFormat.Csv,StatementReference=$"STMT-{Guid.NewGuid():N}",OpeningBalance=0m,ClosingBalance=amounts.Sum(),
		Content="BookingDate,Amount,Currency,Reference\n"+string.Join("\n",amounts.Select((amount,index)=>$"{Date(PostingDate)},{amount.ToString(CultureInfo.InvariantCulture)},EUR,LINE-{index}"))
	});

	public async Task<(long LineId, long[] ReceiptIds)> CreateReceiptsAsync(params int[] quantities)
	{
		var itemId = await new ItemRepository(Database).CreateAsync(new Item { PartNumber=$"MATCH-{Guid.NewGuid():N}",Description="Matching fixture",IsActive=true }, CancellationToken.None);
		var inventoryId = Database.Insert("INSERT INTO Inventories (ItemId,PurposeId,StorageLocationId,IsActive) VALUES ($Item,$Purpose,$Location,1);", P("$Item", itemId), P("$Purpose", Scalar("SELECT MIN(Id) FROM Purposes WHERE Name='Stock';")), P("$Location", Scalar("SELECT MIN(Id) FROM StorageLocations;")));
		var orderId = Database.Insert("INSERT INTO PurchaseOrders (OrderNumber,SupplierId,OrderDate,Status,CreatedByUserId) VALUES ($Number,$Supplier,$Date,$Status,$User);", P("$Number", $"PO-{Guid.NewGuid():N}"), P("$Supplier", SupplierId), P("$Date", Date(PostingDate)), P("$Status", (int)PurchaseOrderStatus.Ordered), P("$User", CreatorId));
		var lineId = Database.Insert("INSERT INTO PurchaseOrderLines (PurchaseOrderId,LineNumber,ItemId,Quantity,ReceivedQuantity,UnitPrice) VALUES ($Order,1,$Item,$Quantity,$Quantity,10);", P("$Order", orderId), P("$Item", itemId), P("$Quantity", quantities.Sum()));
		var receiptIds = new List<long>();
		foreach (var quantity in quantities)
		{
			var receiptId = Database.Insert("INSERT INTO GoodsReceipts (ReceiptNumber,PurchaseOrderId,ReceiptDate,SupplierDeliveryNoteNumber,ReceivedByUserId) VALUES ($Number,$Order,$Date,$DeliveryNote,$User);", P("$Number", $"GR-{Guid.NewGuid():N}"), P("$Order", orderId), P("$Date", Date(PostingDate)), P("$DeliveryNote", $"DN-{Guid.NewGuid():N}"), P("$User", CreatorId));
			receiptIds.Add(Database.Insert("INSERT INTO GoodsReceiptLines (GoodsReceiptId,PurchaseOrderLineId,InventoryId,Quantity) VALUES ($Receipt,$Line,$Inventory,$Quantity);", P("$Receipt", receiptId), P("$Line", lineId), P("$Inventory", inventoryId), P("$Quantity", quantity)));
		}
		return (lineId, receiptIds.ToArray());
	}

	public Task<FinanceJournalEntry> TransferAsync(decimal amount) => Ledger.PostAsync(new FinancePostingRequest
	{
		OperationId=Guid.NewGuid(),AccountingBookId=BookId,JournalId=_journalId,AccountingPeriodId=_periodId,NumberSequenceCode="GL",PostingDate=PostingDate,Description="Bank transfer",SourceType="BankTransfer",SourceId=Guid.NewGuid().ToString("D"),SourceEvent="Posted",TransactionCurrency=new CurrencyCode("EUR"),
		Lines=[new FinancePostingLine { AccountId=BankGlA,Credit=amount },new FinancePostingLine { AccountId=BankGlB,Debit=amount }]
	});

	public Task<FinanceJournalEntry> SplitBankReceiptAsync() => Ledger.PostAsync(new FinancePostingRequest
	{
		OperationId=Guid.NewGuid(),AccountingBookId=BookId,JournalId=_journalId,AccountingPeriodId=_periodId,NumberSequenceCode="GL",PostingDate=PostingDate,Description="Split bank receipt",SourceType="CustomerReceiptFixture",SourceId=Guid.NewGuid().ToString("D"),SourceEvent="Posted",TransactionCurrency=new CurrencyCode("EUR"),
		Lines=[new FinancePostingLine { AccountId=BankGlA,Debit=20m },new FinancePostingLine { AccountId=BankGlB,Debit=80m },new FinancePostingLine { AccountId=_payableAccountId,Credit=100m }]
	});

	public long CreateReceivableEvidence(long journalId, decimal amount)
	{
		var customerId = Database.Insert("INSERT INTO Customers (CustomerNumber,Name,IsActive) VALUES ($Number,'Financial integrity customer',1);", P("$Number", $"FI-{Guid.NewGuid():N}"));
		var operationId = Guid.NewGuid().ToString("D");
		var openItemId = Database.Insert("INSERT INTO FinanceReceivableOpenItems (Version,LegalEntityId,AccountingBookId,CustomerId,Kind,SourceType,SourceId,DocumentDate,DueDate,CurrencyCode,OriginalAmount,RemainingAmount,JournalEntryId,OperationId,IsVoided,CreatedAtUtc,CreatedByUserId) VALUES (1,$Entity,$Book,$Customer,3,'CustomerPayment',$Operation,$Date,$Date,'EUR',$Amount,$Amount,$Journal,$Operation,0,$At,$User);", P("$Entity", EntityId.ToString("D")), P("$Book", BookId.ToString("D")), P("$Customer", customerId), P("$Operation", operationId), P("$Date", Date(PostingDate)), P("$Amount", amount), P("$Journal", journalId), P("$At", DateTime.UtcNow), P("$User", CreatorId));
		return Database.Insert("INSERT INTO FinanceReceivablePayments (Version,OperationId,RequestHash,CustomerId,CurrencyCode,PaymentDate,Amount,Description,OpenItemId,JournalEntryId,CreatedAtUtc,CreatedByUserId,IsReversed) VALUES (1,$Operation,$Hash,$Customer,'EUR',$Date,$Amount,'Financial integrity receipt',$OpenItem,$Journal,$At,$User,0);", P("$Operation", operationId), P("$Hash", new string('0', 64)), P("$Customer", customerId), P("$Date", Date(PostingDate)), P("$Amount", amount), P("$OpenItem", openItemId), P("$Journal", journalId), P("$At", DateTime.UtcNow), P("$User", CreatorId));
	}

	public long Scalar(string sql, params DatabaseParameter[] parameters) => Database.Query(sql, reader => Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), parameters).Single();
	public decimal Amount(string sql, params DatabaseParameter[] parameters) => Database.Query(sql, reader => Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture), parameters).Single();
	public static DatabaseParameter P(string name, object? value) => new(name, value);
	private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private long CreateUser(string role) => Database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ($Email,'Financial integrity','test',0,0,0,1,$At);", P("$Email", $"financial-{role}-{_suffix}@depot.test"), P("$At", DateTime.UtcNow));
	private long SaveConfiguration(FinancePayablesConfiguration value)
	{
		DatabaseParameter[] parameters = [P("$Version",value.Version),P("$Entity",value.LegalEntityId.ToString("D")),P("$Calendar",value.FiscalCalendarId.ToString("D")),P("$Invoice",value.InvoicePostingProfileId),P("$Credit",value.CreditNotePostingProfileId),P("$Payment",value.PaymentPostingProfileId),P("$Active",value.IsActive)];
		if (value.Id == 0) return Database.Insert("INSERT INTO FinancePayablesConfigurations (Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,IsActive) VALUES ($Version,$Entity,$Calendar,$Invoice,$Credit,$Payment,$Active);",parameters);
		Database.Execute("UPDATE FinancePayablesConfigurations SET Version=$Version,LegalEntityId=$Entity,FiscalCalendarId=$Calendar,InvoicePostingProfileId=$Invoice,CreditNotePostingProfileId=$Credit,PaymentPostingProfileId=$Payment,IsActive=$Active WHERE Id=$Id;",parameters.Append(P("$Id",value.Id)).ToArray());
		return value.Id;
	}
	private void CreateAccount(Guid id, string number, FinanceAccountType type) => Database.Execute("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,$Number,$Name,$Type,1,1);", P("$Id", id.ToString("D")), P("$Chart", _chartId.ToString("D")), P("$Number", number), P("$Name", number), P("$Type", (int)type));
	private long CreateProfile(string name, string sourceType, params (Guid AccountId, FinancePostingDirection Direction, string AmountKey)[] lines)
	{
		var id = Database.Insert("INSERT INTO FinancePostingProfiles (Version,LegalEntityId,AccountingBookId,JournalId,Code,Name,SourceType,SourceEvent,NumberSequenceCode,IsActive) VALUES (1,$Entity,$Book,$Journal,$Code,$Code,$Source,'Posted','GL',1);", P("$Entity", EntityId.ToString("D")), P("$Book", BookId.ToString("D")), P("$Journal", _journalId.ToString("D")), P("$Code", $"{name}-{_suffix[..8]}"), P("$Source", sourceType));
		for (var index=0;index<lines.Length;index++) Database.Execute("INSERT INTO FinancePostingProfileLines (PostingProfileId,LineNumber,AccountId,Direction,AmountKey,Multiplier) VALUES ($Profile,$Line,$Account,$Direction,$Key,1);", P("$Profile", id), P("$Line", index+1), P("$Account", lines[index].AccountId.ToString("D")), P("$Direction", (int)lines[index].Direction), P("$Key", lines[index].AmountKey));
		return id;
	}

	private (FinanceAccountsPayableService Payables, FinanceBankingService Banking, FinanceSepaPaymentExportService Sepa, FinanceGeneralLedgerService Ledger) CreateServices(long userId)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id=userId,Email=$"financial-{userId}@depot.test",DisplayName="Financial integrity",IsActive=true }, PermissionCatalog.All);
		var auditRepository = new AuditRepository(Database);
		var audit = new AuditService(auditRepository, authorization);
		var ledger = new FinanceGeneralLedgerService(Transactions, new FinanceGeneralLedgerRepository(Database), new FinancePostingProfileRepository(Database), auditRepository, audit, authorization);
		var payables = new FinanceAccountsPayableService(Transactions, new FinanceAccountsPayableRepository(Database), ledger, auditRepository, audit, authorization);
		var bankingRepository = new FinanceBankingRepository(Database);
		var banking = new FinanceBankingService(Transactions, bankingRepository, payables, auditRepository, audit, authorization);
		var sepa = new FinanceSepaPaymentExportService(Transactions, bankingRepository, new FinanceSepaPaymentExportRepository(Database), banking, auditRepository, audit, authorization);
		return (payables, banking, sepa, ledger);
	}

	public void Dispose()
	{
		if (_previousConfiguration is null) Database.Execute("DELETE FROM FinancePayablesConfigurations WHERE Id=$Id;",P("$Id",_configurationId));
		else SaveConfiguration(_previousConfiguration);
		if (_databasePath is not null)
		{
			SqliteConnection.ClearAllPools();
			File.Delete(_databasePath);
		}
	}
}
