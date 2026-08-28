// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceBankingViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceBankingService _banking;
	private FinanceBankAccount? _selectedBankAccount;
	private FinanceBankStatement? _selectedStatement;
	private FinanceBankStatementLine? _selectedStatementLine;
	private FinancePaymentRun? _selectedPaymentRun;
	private FinancePaymentRunLine? _selectedPaymentRunLine;
	private string _accountName=string.Empty,_bankName=string.Empty,_iban=string.Empty,_bic=string.Empty,_localAccount=string.Empty,_legalEntityId=string.Empty,_bookId=string.Empty,_glAccountId=string.Empty,_currency="EUR";
	private bool _accountActive=true;
	private FinanceBankStatementFormat _statementFormat=FinanceBankStatementFormat.Csv;
	private string _statementContent=string.Empty,_statementReference=string.Empty,_statementFileName=string.Empty;
	private string _payableOpenItemId=string.Empty,_paymentAmount=string.Empty,_paymentDescription="Supplier payment proposal",_paymentReference=string.Empty,_approvalComment=string.Empty,_executionReference=string.Empty;
	private DateTime _paymentDate=DateTime.Today;
	private FinanceBankReconciliationTargetKind _targetKind=FinanceBankReconciliationTargetKind.PayablePayment;
	private string _targetId=string.Empty,_reversalReason=string.Empty;
	private bool _disposed;

	public FinanceBankingViewModel(FinanceBankingService banking)
	{
		_banking=banking;
		RefreshCommand=new AsyncRelayCommand(LoadAsync);
		SaveBankAccountCommand=new AsyncRelayCommand(SaveBankAccountAsync);
		NewBankAccountCommand=new AsyncRelayCommand(_=>{ClearBankAccount(); return Task.CompletedTask;});
		ImportStatementCommand=new AsyncRelayCommand(ImportStatementAsync);
		LoadStatementCommand=new AsyncRelayCommand(LoadStatementAsync);
		ReconcileCommand=new AsyncRelayCommand(ReconcileAsync);
		ReverseReconciliationCommand=new AsyncRelayCommand(ReverseReconciliationAsync);
		CreatePaymentRunCommand=new AsyncRelayCommand(CreatePaymentRunAsync);
		ApprovePaymentRunCommand=new AsyncRelayCommand(ApprovePaymentRunAsync);
		ExecutePaymentRunLineCommand=new AsyncRelayCommand(ExecutePaymentRunLineAsync);
	}

	public ObservableCollection<FinanceBankAccount> BankAccounts { get; }=[];
	public ObservableCollection<FinanceBankStatement> Statements { get; }=[];
	public ObservableCollection<FinanceBankStatementLine> StatementLines { get; }=[];
	public ObservableCollection<FinanceBankStatementLine> UnreconciledLines { get; }=[];
	public ObservableCollection<FinancePaymentRun> PaymentRuns { get; }=[];
	public ObservableCollection<FinanceCashPosition> CashPositions { get; }=[];
	public IReadOnlyList<FinanceBankStatementFormat> StatementFormats { get; }=Enum.GetValues<FinanceBankStatementFormat>();
	public IReadOnlyList<FinanceBankReconciliationTargetKind> ReconciliationTargets { get; }=Enum.GetValues<FinanceBankReconciliationTargetKind>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand SaveBankAccountCommand { get; }
	public AsyncRelayCommand NewBankAccountCommand { get; }
	public AsyncRelayCommand ImportStatementCommand { get; }
	public AsyncRelayCommand LoadStatementCommand { get; }
	public AsyncRelayCommand ReconcileCommand { get; }
	public AsyncRelayCommand ReverseReconciliationCommand { get; }
	public AsyncRelayCommand CreatePaymentRunCommand { get; }
	public AsyncRelayCommand ApprovePaymentRunCommand { get; }
	public AsyncRelayCommand ExecutePaymentRunLineCommand { get; }

	public bool CanManage=>_banking.CanManage;
	public bool CanImportStatements=>_banking.CanImportStatements;
	public bool CanReconcile=>_banking.CanReconcile;
	public bool CanCreatePaymentRuns=>_banking.CanCreatePaymentRuns;
	public bool CanApprovePaymentRuns=>_banking.CanApprovePaymentRuns;
	public bool CanExecutePaymentRuns=>_banking.CanExecutePaymentRuns;

	public FinanceBankAccount? SelectedBankAccount { get=>_selectedBankAccount; set { if(ReferenceEquals(_selectedBankAccount,value))return; _selectedBankAccount=value; OnPropertyChanged(); if(value is not null) Apply(value); } }
	public FinanceBankStatement? SelectedStatement { get=>_selectedStatement; set { if(ReferenceEquals(_selectedStatement,value))return; _selectedStatement=value; OnPropertyChanged(); } }
	public FinanceBankStatementLine? SelectedStatementLine { get=>_selectedStatementLine; set { if(ReferenceEquals(_selectedStatementLine,value))return; _selectedStatementLine=value; OnPropertyChanged(); } }
	public FinancePaymentRun? SelectedPaymentRun { get=>_selectedPaymentRun; set { if(ReferenceEquals(_selectedPaymentRun,value))return; _selectedPaymentRun=value; OnPropertyChanged(); Replace(PaymentRunLines,value?.Lines??[]); } }
	public ObservableCollection<FinancePaymentRunLine> PaymentRunLines { get; }=[];
	public FinancePaymentRunLine? SelectedPaymentRunLine { get=>_selectedPaymentRunLine; set { if(ReferenceEquals(_selectedPaymentRunLine,value))return; _selectedPaymentRunLine=value; OnPropertyChanged(); } }

	public string AccountName { get=>_accountName; set=>Set(ref _accountName,value); }
	public string BankName { get=>_bankName; set=>Set(ref _bankName,value); }
	public string Iban { get=>_iban; set=>Set(ref _iban,value); }
	public string Bic { get=>_bic; set=>Set(ref _bic,value); }
	public string LocalAccountNumber { get=>_localAccount; set=>Set(ref _localAccount,value); }
	public string LegalEntityId { get=>_legalEntityId; set=>Set(ref _legalEntityId,value); }
	public string AccountingBookId { get=>_bookId; set=>Set(ref _bookId,value); }
	public string GeneralLedgerAccountId { get=>_glAccountId; set=>Set(ref _glAccountId,value); }
	public string Currency { get=>_currency; set=>Set(ref _currency,value); }
	public bool AccountActive { get=>_accountActive; set { if(_accountActive==value)return; _accountActive=value; OnPropertyChanged(); } }
	public FinanceBankStatementFormat StatementFormat { get=>_statementFormat; set { if(_statementFormat==value)return; _statementFormat=value; OnPropertyChanged(); } }
	public string StatementContent { get=>_statementContent; set=>Set(ref _statementContent,value); }
	public string StatementReference { get=>_statementReference; set=>Set(ref _statementReference,value); }
	public string StatementFileName { get=>_statementFileName; set=>Set(ref _statementFileName,value); }
	public string PayableOpenItemId { get=>_payableOpenItemId; set=>Set(ref _payableOpenItemId,value); }
	public string PaymentAmount { get=>_paymentAmount; set=>Set(ref _paymentAmount,value); }
	public string PaymentDescription { get=>_paymentDescription; set=>Set(ref _paymentDescription,value); }
	public string PaymentReference { get=>_paymentReference; set=>Set(ref _paymentReference,value); }
	public DateTime PaymentDate { get=>_paymentDate; set { if(_paymentDate==value)return; _paymentDate=value; OnPropertyChanged(); } }
	public string ApprovalComment { get=>_approvalComment; set=>Set(ref _approvalComment,value); }
	public string ExecutionReference { get=>_executionReference; set=>Set(ref _executionReference,value); }
	public FinanceBankReconciliationTargetKind TargetKind { get=>_targetKind; set { if(_targetKind==value)return; _targetKind=value; OnPropertyChanged(); } }
	public string TargetId { get=>_targetId; set=>Set(ref _targetId,value); }
	public string ReversalReason { get=>_reversalReason; set=>Set(ref _reversalReason,value); }

	public async Task LoadAsync(CancellationToken cancellationToken=default)
	{
		BeginOperation("Loading Banking...");
		try
		{
			Replace(BankAccounts,await _banking.GetBankAccountsAsync(cancellationToken));
			Replace(Statements,await _banking.GetStatementsAsync(cancellationToken:cancellationToken));
			Replace(UnreconciledLines,await _banking.GetUnreconciledLinesAsync(cancellationToken:cancellationToken));
			Replace(PaymentRuns,await _banking.GetPaymentRunsAsync(cancellationToken));
			Replace(CashPositions,await _banking.GetCashPositionAsync(cancellationToken));
			CompleteOperation(BankAccounts.Count==0,"Banking loaded.");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Banking could not be loaded.");}
	}

	private async Task SaveBankAccountAsync(CancellationToken token)
	{
		BeginOperation("Saving bank account...");
		try
		{
			var current=SelectedBankAccount;
			var value=new FinanceBankAccount{Id=current?.Id??0,Version=current?.Version??1,LegalEntityId=ParseGuid(LegalEntityId,"legal entity"),AccountingBookId=ParseGuid(AccountingBookId,"accounting book"),GeneralLedgerAccountId=ParseGuid(GeneralLedgerAccountId,"bank GL account"),Currency=new CurrencyCode(Currency),Name=AccountName,BankName=BankName,Iban=Iban,Bic=Bic,LocalAccountNumber=LocalAccountNumber,IsActive=AccountActive};
			SelectedBankAccount=await _banking.SaveBankAccountAsync(value,token);
			await LoadAsync(token);
			CompleteOperation(false,"Bank account saved.");
		}
		catch(Exception exception){FailOperation(exception,"Bank account could not be saved.");}
	}

	private async Task ImportStatementAsync(CancellationToken token)
	{
		BeginOperation("Importing bank statement...");
		try
		{
			var bank=SelectedBankAccount??throw new InvalidOperationException("Select a bank account first.");
			await _banking.ImportStatementAsync(new FinanceBankStatementImportRequest{OperationId=Guid.NewGuid(),BankAccountId=bank.Id,Format=StatementFormat,Content=StatementContent,SourceFileName=StatementFileName,StatementReference=string.IsNullOrWhiteSpace(StatementReference)?null:StatementReference},token);
			StatementContent=string.Empty;
			await LoadAsync(token);
			CompleteOperation(false,"Bank statement imported.");
		}
		catch(Exception exception){FailOperation(exception,"Bank statement import failed.");}
	}

	private async Task LoadStatementAsync(CancellationToken token)
	{
		try
		{
			if(SelectedStatement is null)return;
			var statement=await _banking.GetStatementAsync(SelectedStatement.Id,token);
			Replace(StatementLines,statement?.Lines??[]);
		}
		catch(Exception exception){FailOperation(exception,"Statement lines could not be loaded.");}
	}

	private async Task ReconcileAsync(CancellationToken token)
	{
		BeginOperation("Reconciling bank statement line...");
		try
		{
			var line=SelectedStatementLine??throw new InvalidOperationException("Select a statement line.");
			await _banking.ReconcileAsync(new FinanceBankReconciliationRequest{OperationId=Guid.NewGuid(),StatementLineId=line.Id,TargetKind=TargetKind,TargetId=ParseLong(TargetId,"target")},token);
			await LoadAsync(token); await LoadStatementAsync(token);
			CompleteOperation(false,"Bank statement line reconciled.");
		}
		catch(Exception exception){FailOperation(exception,"Bank reconciliation failed.");}
	}

	private async Task ReverseReconciliationAsync(CancellationToken token)
	{
		BeginOperation("Reversing bank reconciliation...");
		try
		{
			var line=SelectedStatementLine??throw new InvalidOperationException("Select a reconciled statement line.");
			if(!line.ReconciliationId.HasValue)throw new InvalidOperationException("Selected line has no active reconciliation.");
			await _banking.ReverseReconciliationAsync(line.ReconciliationId.Value,Guid.NewGuid(),ReversalReason,token);
			await LoadAsync(token); await LoadStatementAsync(token);
			CompleteOperation(false,"Bank reconciliation reversed.");
		}
		catch(Exception exception){FailOperation(exception,"Bank reconciliation reversal failed.");}
	}

	private async Task CreatePaymentRunAsync(CancellationToken token)
	{
		BeginOperation("Creating payment proposal...");
		try
		{
			var bank=SelectedBankAccount??throw new InvalidOperationException("Select a bank account first.");
			var run=await _banking.CreatePaymentRunAsync(new FinancePaymentRunRequest{OperationId=Guid.NewGuid(),BankAccountId=bank.Id,PaymentDate=DateOnly.FromDateTime(PaymentDate),Currency=bank.Currency,Description=PaymentDescription,Lines=[new FinancePaymentRunLineRequest(ParseLong(PayableOpenItemId,"payable open item"),ParseDecimal(PaymentAmount,"payment amount"),PaymentReference)]},token);
			SelectedPaymentRun=run; await LoadAsync(token); CompleteOperation(false,"Payment proposal created.");
		}
		catch(Exception exception){FailOperation(exception,"Payment proposal could not be created.");}
	}

	private async Task ApprovePaymentRunAsync(CancellationToken token)
	{
		BeginOperation("Approving payment proposal...");
		try
		{
			var run=SelectedPaymentRun??throw new InvalidOperationException("Select a payment run.");
			SelectedPaymentRun=await _banking.ApprovePaymentRunAsync(run.Id,run.Version,ApprovalComment,token);
			await LoadAsync(token); CompleteOperation(false,"Payment proposal approved.");
		}
		catch(Exception exception){FailOperation(exception,"Payment proposal approval failed.");}
	}

	private async Task ExecutePaymentRunLineAsync(CancellationToken token)
	{
		BeginOperation("Executing supplier payment...");
		try
		{
			var run=SelectedPaymentRun??throw new InvalidOperationException("Select a payment run.");
			var line=SelectedPaymentRunLine??throw new InvalidOperationException("Select a payment line.");
			SelectedPaymentRun=await _banking.ExecutePaymentRunLineAsync(run.Id,line.Id,ExecutionReference,token);
			await LoadAsync(token); CompleteOperation(false,"Supplier payment executed and linked to AP.");
		}
		catch(Exception exception){FailOperation(exception,"Payment execution failed.");}
	}

	private void Apply(FinanceBankAccount value){AccountName=value.Name;BankName=value.BankName??string.Empty;Iban=value.Iban??string.Empty;Bic=value.Bic??string.Empty;LocalAccountNumber=value.LocalAccountNumber??string.Empty;LegalEntityId=value.LegalEntityId.ToString("D");AccountingBookId=value.AccountingBookId.ToString("D");GeneralLedgerAccountId=value.GeneralLedgerAccountId.ToString("D");Currency=value.Currency.Value;AccountActive=value.IsActive;}
	private void ClearBankAccount(){SelectedBankAccount=null;AccountName=BankName=Iban=Bic=LocalAccountNumber=LegalEntityId=AccountingBookId=GeneralLedgerAccountId=string.Empty;Currency="EUR";AccountActive=true;}
	private void Set(ref string field,string value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;OnPropertyChanged(name);}
	private static Guid ParseGuid(string value,string field)=>Guid.TryParse(value,out var result)?result:throw new ArgumentException($"A valid {field} ID is required.");
	private static long ParseLong(string value,string field)=>long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var result)&&result>0?result:throw new ArgumentException($"A valid {field} ID is required.");
	private static decimal ParseDecimal(string value,string field)=>decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var result)&&result>0m?result:throw new ArgumentException($"A positive {field} is required.");
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var value in values)target.Add(value);}
	public void Dispose(){if(_disposed)return;_disposed=true;RefreshCommand.Dispose();SaveBankAccountCommand.Dispose();NewBankAccountCommand.Dispose();ImportStatementCommand.Dispose();LoadStatementCommand.Dispose();ReconcileCommand.Dispose();ReverseReconciliationCommand.Dispose();CreatePaymentRunCommand.Dispose();ApprovePaymentRunCommand.Dispose();ExecutePaymentRunLineCommand.Dispose();}
}
