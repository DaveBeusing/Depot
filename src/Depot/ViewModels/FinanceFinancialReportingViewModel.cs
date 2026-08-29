// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceFinancialReportingViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceFinancialReportingService _reporting;
	private readonly IFileDialogService _fileDialogs;
	private FinanceReportKind _reportKind=FinanceReportKind.TrialBalance;
	private string _accountingBookId=string.Empty,_dimensionId=string.Empty,_dimensionValueId=string.Empty,_warningText=string.Empty;
	private DateTime _fromDate=new(DateTime.Today.Year,1,1),_toDate=DateTime.Today,_asOfDate=DateTime.Today;
	private bool _includeZeroBalances;
	private FinanceReportResult? _currentReport;
	private FinanceReportingAccountMapping? _selectedMapping;
	private FinanceReportingAccountRecord? _selectedAccount;
	private FinanceStatementSection _statementSection;
	private FinanceCashFlowCategory _cashFlowCategory;
	private FinanceTaxReportCategory _taxCategory;
	private bool _isCashAccount,_isCostOfGoodsSold,_mappingActive=true;
	private string _sortOrder="0";
	private bool _disposed;

	public FinanceFinancialReportingViewModel(FinanceFinancialReportingService reporting,IFileDialogService fileDialogs)
	{
		_reporting=reporting; _fileDialogs=fileDialogs;
		RefreshCommand=new AsyncRelayCommand(LoadAsync);
		GenerateCommand=new AsyncRelayCommand(GenerateAsync);
		SaveMappingCommand=new AsyncRelayCommand(SaveMappingAsync);
		NewMappingCommand=new AsyncRelayCommand(_=>{ClearMapping();return Task.CompletedTask;});
		CreateSnapshotCommand=new AsyncRelayCommand(CreateSnapshotAsync);
		ExportCsvCommand=new AsyncRelayCommand(ExportCsvAsync);
	}

	public ObservableCollection<FinanceReportRow> Rows { get; }=[];
	public ObservableCollection<FinanceReportingAccountMapping> Mappings { get; }=[];
	public ObservableCollection<FinanceReportingAccountRecord> Accounts { get; }=[];
	public ObservableCollection<FinanceReportSnapshot> Snapshots { get; }=[];
	public IReadOnlyList<FinanceReportKind> ReportKinds { get; }=Enum.GetValues<FinanceReportKind>();
	public IReadOnlyList<FinanceStatementSection> StatementSections { get; }=Enum.GetValues<FinanceStatementSection>();
	public IReadOnlyList<FinanceCashFlowCategory> CashFlowCategories { get; }=Enum.GetValues<FinanceCashFlowCategory>();
	public IReadOnlyList<FinanceTaxReportCategory> TaxCategories { get; }=Enum.GetValues<FinanceTaxReportCategory>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand GenerateCommand { get; }
	public AsyncRelayCommand SaveMappingCommand { get; }
	public AsyncRelayCommand NewMappingCommand { get; }
	public AsyncRelayCommand CreateSnapshotCommand { get; }
	public AsyncRelayCommand ExportCsvCommand { get; }
	public bool CanManage=>_reporting.CanManage;
	public bool CanExport=>_reporting.CanExport;
	public bool CanCreateSnapshots=>_reporting.CanCreateSnapshots;

	public FinanceReportKind ReportKind { get=>_reportKind; set{if(_reportKind==value)return;_reportKind=value;OnPropertyChanged();} }
	public string AccountingBookId { get=>_accountingBookId; set=>Set(ref _accountingBookId,value); }
	public DateTime FromDate { get=>_fromDate; set=>SetDate(ref _fromDate,value); }
	public DateTime ToDate { get=>_toDate; set=>SetDate(ref _toDate,value); }
	public DateTime AsOfDate { get=>_asOfDate; set=>SetDate(ref _asOfDate,value); }
	public string DimensionId { get=>_dimensionId; set=>Set(ref _dimensionId,value); }
	public string DimensionValueId { get=>_dimensionValueId; set=>Set(ref _dimensionValueId,value); }
	public bool IncludeZeroBalances { get=>_includeZeroBalances; set{if(_includeZeroBalances==value)return;_includeZeroBalances=value;OnPropertyChanged();} }
	public string WarningText { get=>_warningText; private set=>Set(ref _warningText,value); }
	public FinanceReportResult? CurrentReport { get=>_currentReport; private set{if(ReferenceEquals(_currentReport,value))return;_currentReport=value;OnPropertyChanged();OnPropertyChanged(nameof(ReportCurrency));OnPropertyChanged(nameof(RowCount));} }
	public string ReportCurrency=>CurrentReport?.ReportingCurrency.Value??string.Empty;
	public int RowCount=>Rows.Count;

	public FinanceReportingAccountMapping? SelectedMapping { get=>_selectedMapping; set{if(ReferenceEquals(_selectedMapping,value))return;_selectedMapping=value;OnPropertyChanged();if(value is not null)Apply(value);} }
	public FinanceReportingAccountRecord? SelectedAccount { get=>_selectedAccount; set{if(ReferenceEquals(_selectedAccount,value))return;_selectedAccount=value;OnPropertyChanged();} }
	public FinanceStatementSection StatementSection { get=>_statementSection; set{if(_statementSection==value)return;_statementSection=value;OnPropertyChanged();} }
	public FinanceCashFlowCategory CashFlowCategory { get=>_cashFlowCategory; set{if(_cashFlowCategory==value)return;_cashFlowCategory=value;OnPropertyChanged();} }
	public FinanceTaxReportCategory TaxCategory { get=>_taxCategory; set{if(_taxCategory==value)return;_taxCategory=value;OnPropertyChanged();} }
	public bool IsCashAccount { get=>_isCashAccount; set{if(_isCashAccount==value)return;_isCashAccount=value;OnPropertyChanged();} }
	public bool IsCostOfGoodsSold { get=>_isCostOfGoodsSold; set{if(_isCostOfGoodsSold==value)return;_isCostOfGoodsSold=value;OnPropertyChanged();} }
	public bool MappingActive { get=>_mappingActive; set{if(_mappingActive==value)return;_mappingActive=value;OnPropertyChanged();} }
	public string SortOrder { get=>_sortOrder; set=>Set(ref _sortOrder,value); }

	public async Task LoadAsync(CancellationToken cancellationToken=default)
	{
		BeginOperation("Loading Financial Reporting...");
		try
		{
			if(Guid.TryParse(AccountingBookId,out var book))
			{
				Replace(Mappings,await _reporting.GetMappingsAsync(book,cancellationToken));
				if(CanManage) Replace(Accounts,await _reporting.GetAccountsAsync(book,cancellationToken));
				Replace(Snapshots,await _reporting.GetRecentSnapshotsAsync(book,cancellationToken));
			}
			else { Mappings.Clear(); Accounts.Clear(); Replace(Snapshots,await _reporting.GetRecentSnapshotsAsync(null,cancellationToken)); }
			CompleteOperation(false,"Financial Reporting loaded.");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Financial Reporting could not be loaded.");}
	}

	private async Task GenerateAsync(CancellationToken token)
	{
		BeginOperation("Generating financial report...");
		try
		{
			var parameters=new FinanceReportParameters{Kind=ReportKind,AccountingBookId=ParseGuid(AccountingBookId,"accounting book"),FromDate=DateOnly.FromDateTime(FromDate),ToDate=DateOnly.FromDateTime(ToDate),AsOfDate=DateOnly.FromDateTime(AsOfDate),DimensionId=OptionalGuid(DimensionId,"dimension"),DimensionValueId=OptionalGuid(DimensionValueId,"dimension value"),IncludeZeroBalances=IncludeZeroBalances};
			CurrentReport=await _reporting.GenerateAsync(parameters,token);
			Replace(Rows,CurrentReport.Rows); OnPropertyChanged(nameof(RowCount));
			WarningText=string.Join(Environment.NewLine,CurrentReport.Warnings);
			CompleteOperation(Rows.Count==0,$"{ReportKind} generated ({Rows.Count} row(s)).");
		}
		catch(Exception exception){FailOperation(exception,"Financial report generation failed.");}
	}

	private async Task SaveMappingAsync(CancellationToken token)
	{
		BeginOperation("Saving reporting mapping...");
		try
		{
			var account=SelectedAccount??(SelectedMapping is not null?Accounts.FirstOrDefault(value=>value.Id==SelectedMapping.AccountId):null)??throw new InvalidOperationException("Select an account.");
			var current=SelectedMapping;
			var value=new FinanceReportingAccountMapping{Id=current?.Id??0,Version=current?.Version??1,AccountingBookId=ParseGuid(AccountingBookId,"accounting book"),AccountId=account.Id,StatementSection=StatementSection,CashFlowCategory=CashFlowCategory,TaxCategory=TaxCategory,IsCashAccount=IsCashAccount,IsCostOfGoodsSold=IsCostOfGoodsSold,SortOrder=ParseInt(SortOrder,"sort order"),IsActive=MappingActive};
			SelectedMapping=await _reporting.SaveMappingAsync(value,token); await LoadAsync(token); CompleteOperation(false,"Reporting mapping saved.");
		}
		catch(Exception exception){FailOperation(exception,"Reporting mapping could not be saved.");}
	}

	private async Task CreateSnapshotAsync(CancellationToken token)
	{
		BeginOperation("Creating report snapshot...");
		try
		{
			var report=CurrentReport??throw new InvalidOperationException("Generate a report first.");
			var snapshot=await _reporting.CreateSnapshotAsync(Guid.NewGuid(),report,token);
			CurrentReport=report with { SnapshotId=snapshot.Id,ContentHash=snapshot.ContentHash };
			await LoadAsync(token); CompleteOperation(false,$"Report snapshot {snapshot.Id} created.");
		}
		catch(Exception exception){FailOperation(exception,"Report snapshot could not be created.");}
	}

	private async Task ExportCsvAsync(CancellationToken token)
	{
		BeginOperation("Exporting report...");
		try
		{
			var report=CurrentReport??throw new InvalidOperationException("Generate a report first.");
			var path=_fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Export financial report","CSV files (*.csv)|*.csv",".csv",$"finance-{ReportKind}-{DateTime.Now:yyyyMMdd-HHmmss}.csv"));
			if(string.IsNullOrWhiteSpace(path)){CompleteOperation(false,"Export cancelled.");return;}
			await File.WriteAllTextAsync(path,_reporting.ExportCsv(report),new UTF8Encoding(true),token); CompleteOperation(false,"Financial report exported.");
		}
		catch(Exception exception){FailOperation(exception,"Financial report export failed.");}
	}

	private void Apply(FinanceReportingAccountMapping value){SelectedAccount=Accounts.FirstOrDefault(account=>account.Id==value.AccountId);StatementSection=value.StatementSection;CashFlowCategory=value.CashFlowCategory;TaxCategory=value.TaxCategory;IsCashAccount=value.IsCashAccount;IsCostOfGoodsSold=value.IsCostOfGoodsSold;SortOrder=value.SortOrder.ToString(CultureInfo.InvariantCulture);MappingActive=value.IsActive;}
	private void ClearMapping(){SelectedMapping=null;SelectedAccount=null;StatementSection=FinanceStatementSection.Unclassified;CashFlowCategory=FinanceCashFlowCategory.None;TaxCategory=FinanceTaxReportCategory.None;IsCashAccount=false;IsCostOfGoodsSold=false;SortOrder="0";MappingActive=true;}
	private void Set(ref string field,string value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;OnPropertyChanged(name);}
	private void SetDate(ref DateTime field,DateTime value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;OnPropertyChanged(name);}
	private static Guid ParseGuid(string value,string field)=>Guid.TryParse(value,out var result)?result:throw new ArgumentException($"A valid {field} ID is required.");
	private static Guid? OptionalGuid(string value,string field)=>string.IsNullOrWhiteSpace(value)?null:ParseGuid(value,field);
	private static int ParseInt(string value,string field)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var result)?result:throw new ArgumentException($"A valid {field} is required.");
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var value in values)target.Add(value);}
	public void Dispose(){if(_disposed)return;_disposed=true;RefreshCommand.Dispose();GenerateCommand.Dispose();SaveMappingCommand.Dispose();NewMappingCommand.Dispose();CreateSnapshotCommand.Dispose();ExportCsvCommand.Dispose();}
}
