// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceReceivablesViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly FinanceAccountsReceivableService _receivables;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _loadRequest = new();
	private string _searchText = string.Empty;
	private bool _includeSettled;
	private int _pageNumber = 1;
	private long _totalCount;
	private FinanceReceivableOpenItem? _selectedOpenItem;
	private FinanceDunningPolicy? _selectedDunningPolicy;
	private decimal _totalReceivable;
	private decimal _unappliedCredits;
	private decimal _netExposure;
	private string _configurationLegalEntityId = string.Empty;
	private string _configurationFiscalCalendarId = string.Empty;
	private string _invoiceProfileId = string.Empty;
	private string _creditNoteProfileId = string.Empty;
	private string _paymentProfileId = string.Empty;
	private string _writeOffProfileId = string.Empty;
	private bool _configurationIsActive = true;
	private long _configurationId;
	private long _configurationVersion;
	private string _paymentCustomerId = string.Empty;
	private string _paymentCurrency = string.Empty;
	private DateTime _paymentDate = DateTime.Today;
	private string _paymentAmount = string.Empty;
	private string _paymentAllocationAmount = string.Empty;
	private string _paymentReference = string.Empty;
	private string _paymentDescription = "Customer payment";
	private string _paymentReversalId = string.Empty;
	private DateTime _paymentReversalDate = DateTime.Today;
	private string _paymentReversalReason = string.Empty;
	private string _allocationCreditOpenItemId = string.Empty;
	private string _allocationDebitOpenItemId = string.Empty;
	private string _allocationAmount = string.Empty;
	private DateTime _allocationDate = DateTime.Today;
	private string _writeOffOpenItemId = string.Empty;
	private string _writeOffAmount = string.Empty;
	private DateTime _writeOffDate = DateTime.Today;
	private string _writeOffReason = string.Empty;
	private string _writeOffReversalId = string.Empty;
	private DateTime _writeOffReversalDate = DateTime.Today;
	private string _writeOffReversalReason = string.Empty;
	private DateTime _agingDate = DateTime.Today;
	private string _statementCustomerId = string.Empty;
	private string _statementCurrency = string.Empty;
	private DateTime _statementFromDate = DateTime.Today.AddMonths(-3);
	private DateTime _statementToDate = DateTime.Today;
	private string _dunningPolicyCode = string.Empty;
	private string _dunningPolicyName = string.Empty;
	private string _dunningLevel1Days = "0";
	private string _dunningLevel2Days = "30";
	private string _dunningLevel3Days = "60";
	private DateTime _dunningAsOfDate = DateTime.Today;

	public FinanceReceivablesViewModel(FinanceAccountsReceivableService receivables)
	{
		_receivables = receivables;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync, () => CanManage);
		PostPaymentCommand = new AsyncRelayCommand(PostPaymentAsync, () => CanPostPayments);
		ReversePaymentCommand = new AsyncRelayCommand(ReversePaymentAsync, () => CanReversePayments);
		AllocateCreditCommand = new AsyncRelayCommand(AllocateCreditAsync, () => CanPostPayments);
		PostWriteOffCommand = new AsyncRelayCommand(PostWriteOffAsync, () => CanPostWriteOffs);
		ReverseWriteOffCommand = new AsyncRelayCommand(ReverseWriteOffAsync, () => CanReverseWriteOffs);
		LoadStatementCommand = new AsyncRelayCommand(LoadStatementAsync);
		SaveDunningPolicyCommand = new AsyncRelayCommand(SaveDunningPolicyAsync, () => CanManageDunning);
		RunDunningCommand = new AsyncRelayCommand(RunDunningAsync, () => CanManageDunning && SelectedDunningPolicy is not null);
	}

	public ObservableCollection<FinanceReceivableOpenItem> OpenItems { get; } = [];
	public ObservableCollection<FinanceReceivableAgingSummary> Aging { get; } = [];
	public ObservableCollection<FinanceCustomerStatementRow> StatementRows { get; } = [];
	public ObservableCollection<FinanceDunningPolicy> DunningPolicies { get; } = [];
	public ObservableCollection<FinanceDunningRunLine> DunningRunLines { get; } = [];

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public AsyncRelayCommand SaveConfigurationCommand { get; }
	public AsyncRelayCommand PostPaymentCommand { get; }
	public AsyncRelayCommand ReversePaymentCommand { get; }
	public AsyncRelayCommand AllocateCreditCommand { get; }
	public AsyncRelayCommand PostWriteOffCommand { get; }
	public AsyncRelayCommand ReverseWriteOffCommand { get; }
	public AsyncRelayCommand LoadStatementCommand { get; }
	public AsyncRelayCommand SaveDunningPolicyCommand { get; }
	public AsyncRelayCommand RunDunningCommand { get; }

	public bool CanManage => _receivables.CanManage;
	public bool CanPostPayments => _receivables.CanPostPayments;
	public bool CanReversePayments => _receivables.CanReversePayments;
	public bool CanPostWriteOffs => _receivables.CanPostWriteOffs;
	public bool CanReverseWriteOffs => _receivables.CanReverseWriteOffs;
	public bool CanViewDunning => _receivables.CanViewDunning;
	public bool CanManageDunning => _receivables.CanManageDunning;
	public bool HasConfiguration => _configurationId > 0;

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			_pageNumber = 1;
			OnPropertyChanged();
			_ = _searchDebouncer.DebounceAsync(LoadOpenItemsAsync);
		}
	}

	public bool IncludeSettled
	{
		get => _includeSettled;
		set
		{
			if (_includeSettled == value) return;
			_includeSettled = value;
			_pageNumber = 1;
			OnPropertyChanged();
			_ = LoadOpenItemsAsync();
		}
	}

	public int PageNumber => _pageNumber;
	public long TotalCount => _totalCount;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public bool ShowPaging => TotalCount > PageSize;
	public string PageDisplay => $"Page {PageNumber} - {TotalCount:N0} open-item rows";
	public decimal TotalReceivable { get => _totalReceivable; private set => SetField(ref _totalReceivable, value); }
	public decimal UnappliedCredits { get => _unappliedCredits; private set => SetField(ref _unappliedCredits, value); }
	public decimal NetExposure { get => _netExposure; private set => SetField(ref _netExposure, value); }
	public DateTime AgingDate { get => _agingDate; set { if (_agingDate == value) return; _agingDate = value; OnPropertyChanged(); } }

	public FinanceReceivableOpenItem? SelectedOpenItem
	{
		get => _selectedOpenItem;
		set
		{
			if (ReferenceEquals(_selectedOpenItem, value)) return;
			_selectedOpenItem = value;
			OnPropertyChanged();
			ApplySelectedOpenItem(value);
		}
	}

	public FinanceDunningPolicy? SelectedDunningPolicy
	{
		get => _selectedDunningPolicy;
		set
		{
			if (ReferenceEquals(_selectedDunningPolicy, value)) return;
			_selectedDunningPolicy = value;
			OnPropertyChanged();
			RunDunningCommand.RaiseCanExecuteChanged();
			if (value is not null) LoadPolicyEditor(value);
		}
	}

	public string ConfigurationLegalEntityId { get => _configurationLegalEntityId; set => SetString(ref _configurationLegalEntityId, value); }
	public string ConfigurationFiscalCalendarId { get => _configurationFiscalCalendarId; set => SetString(ref _configurationFiscalCalendarId, value); }
	public string InvoiceProfileId { get => _invoiceProfileId; set => SetString(ref _invoiceProfileId, value); }
	public string CreditNoteProfileId { get => _creditNoteProfileId; set => SetString(ref _creditNoteProfileId, value); }
	public string PaymentProfileId { get => _paymentProfileId; set => SetString(ref _paymentProfileId, value); }
	public string WriteOffProfileId { get => _writeOffProfileId; set => SetString(ref _writeOffProfileId, value); }
	public bool ConfigurationIsActive { get => _configurationIsActive; set { if (_configurationIsActive == value) return; _configurationIsActive = value; OnPropertyChanged(); } }
	public string PaymentCustomerId { get => _paymentCustomerId; set => SetString(ref _paymentCustomerId, value); }
	public string PaymentCurrency { get => _paymentCurrency; set => SetString(ref _paymentCurrency, value); }
	public DateTime PaymentDate { get => _paymentDate; set { if (_paymentDate == value) return; _paymentDate = value; OnPropertyChanged(); } }
	public string PaymentAmount { get => _paymentAmount; set => SetString(ref _paymentAmount, value); }
	public string PaymentAllocationAmount { get => _paymentAllocationAmount; set => SetString(ref _paymentAllocationAmount, value); }
	public string PaymentReference { get => _paymentReference; set => SetString(ref _paymentReference, value); }
	public string PaymentDescription { get => _paymentDescription; set => SetString(ref _paymentDescription, value); }
	public string PaymentReversalId { get => _paymentReversalId; set => SetString(ref _paymentReversalId, value); }
	public DateTime PaymentReversalDate { get => _paymentReversalDate; set { if (_paymentReversalDate == value) return; _paymentReversalDate = value; OnPropertyChanged(); } }
	public string PaymentReversalReason { get => _paymentReversalReason; set => SetString(ref _paymentReversalReason, value); }
	public string AllocationCreditOpenItemId { get => _allocationCreditOpenItemId; set => SetString(ref _allocationCreditOpenItemId, value); }
	public string AllocationDebitOpenItemId { get => _allocationDebitOpenItemId; set => SetString(ref _allocationDebitOpenItemId, value); }
	public string AllocationAmount { get => _allocationAmount; set => SetString(ref _allocationAmount, value); }
	public DateTime AllocationDate { get => _allocationDate; set { if (_allocationDate == value) return; _allocationDate = value; OnPropertyChanged(); } }
	public string WriteOffOpenItemId { get => _writeOffOpenItemId; set => SetString(ref _writeOffOpenItemId, value); }
	public string WriteOffAmount { get => _writeOffAmount; set => SetString(ref _writeOffAmount, value); }
	public DateTime WriteOffDate { get => _writeOffDate; set { if (_writeOffDate == value) return; _writeOffDate = value; OnPropertyChanged(); } }
	public string WriteOffReason { get => _writeOffReason; set => SetString(ref _writeOffReason, value); }
	public string WriteOffReversalId { get => _writeOffReversalId; set => SetString(ref _writeOffReversalId, value); }
	public DateTime WriteOffReversalDate { get => _writeOffReversalDate; set { if (_writeOffReversalDate == value) return; _writeOffReversalDate = value; OnPropertyChanged(); } }
	public string WriteOffReversalReason { get => _writeOffReversalReason; set => SetString(ref _writeOffReversalReason, value); }
	public string StatementCustomerId { get => _statementCustomerId; set => SetString(ref _statementCustomerId, value); }
	public string StatementCurrency { get => _statementCurrency; set => SetString(ref _statementCurrency, value); }
	public DateTime StatementFromDate { get => _statementFromDate; set { if (_statementFromDate == value) return; _statementFromDate = value; OnPropertyChanged(); } }
	public DateTime StatementToDate { get => _statementToDate; set { if (_statementToDate == value) return; _statementToDate = value; OnPropertyChanged(); } }
	public string DunningPolicyCode { get => _dunningPolicyCode; set => SetString(ref _dunningPolicyCode, value); }
	public string DunningPolicyName { get => _dunningPolicyName; set => SetString(ref _dunningPolicyName, value); }
	public string DunningLevel1Days { get => _dunningLevel1Days; set => SetString(ref _dunningLevel1Days, value); }
	public string DunningLevel2Days { get => _dunningLevel2Days; set => SetString(ref _dunningLevel2Days, value); }
	public string DunningLevel3Days { get => _dunningLevel3Days; set => SetString(ref _dunningLevel3Days, value); }
	public DateTime DunningAsOfDate { get => _dunningAsOfDate; set { if (_dunningAsOfDate == value) return; _dunningAsOfDate = value; OnPropertyChanged(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		BeginOperation("Loading Accounts Receivable...");
		try
		{
			await LoadConfigurationAsync(request.Token);
			await LoadOpenItemsCoreAsync(request.Token);
			await LoadAgingCoreAsync(request.Token);
			if (CanViewDunning) await LoadDunningPoliciesAsync(request.Token);
			if (!request.IsCurrent) return;
			CompleteOperation(OpenItems.Count == 0, "Accounts Receivable loaded.");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Accounts Receivable could not be loaded."); }
	}

	private async Task LoadOpenItemsAsync(CancellationToken cancellationToken = default)
	{
		try { await LoadOpenItemsCoreAsync(cancellationToken); CompleteOperation(OpenItems.Count == 0, "Receivables refreshed."); }
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Receivables could not be refreshed."); }
	}

	private async Task LoadOpenItemsCoreAsync(CancellationToken cancellationToken)
	{
		var page = await _receivables.SearchOpenItemsAsync(SearchText, IncludeSettled, PageNumber, PageSize, cancellationToken);
		Replace(OpenItems, page.Items);
		_pageNumber = page.PageNumber;
		_totalCount = page.TotalCount;
		RaisePaging();
	}

	private async Task LoadAgingCoreAsync(CancellationToken cancellationToken)
	{
		var rows = await _receivables.GetAgingAsync(DateOnly.FromDateTime(AgingDate), cancellationToken);
		Replace(Aging, rows);
		TotalReceivable = rows.Sum(value => value.TotalReceivable);
		UnappliedCredits = rows.Sum(value => value.UnappliedCredits);
		NetExposure = rows.Sum(value => value.NetExposure);
	}

	private async Task LoadConfigurationAsync(CancellationToken cancellationToken)
	{
		var configuration = await _receivables.GetConfigurationAsync(cancellationToken);
		if (configuration is null) return;
		_configurationId = configuration.Id;
		_configurationVersion = configuration.Version;
		ConfigurationLegalEntityId = configuration.LegalEntityId.ToString("D");
		ConfigurationFiscalCalendarId = configuration.FiscalCalendarId.ToString("D");
		InvoiceProfileId = configuration.InvoicePostingProfileId.ToString(CultureInfo.InvariantCulture);
		CreditNoteProfileId = configuration.CreditNotePostingProfileId.ToString(CultureInfo.InvariantCulture);
		PaymentProfileId = configuration.PaymentPostingProfileId.ToString(CultureInfo.InvariantCulture);
		WriteOffProfileId = configuration.WriteOffPostingProfileId.ToString(CultureInfo.InvariantCulture);
		ConfigurationIsActive = configuration.IsActive;
		OnPropertyChanged(nameof(HasConfiguration));
	}

	private async Task LoadDunningPoliciesAsync(CancellationToken cancellationToken)
	{
		Replace(DunningPolicies, await _receivables.GetDunningPoliciesAsync(cancellationToken));
		if (SelectedDunningPolicy is not null) SelectedDunningPolicy = DunningPolicies.FirstOrDefault(value => value.Id == SelectedDunningPolicy.Id);
	}

	private async Task SaveConfigurationAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Saving Accounts Receivable configuration...");
			var saved = await _receivables.SaveConfigurationAsync(new FinanceReceivablesConfiguration
			{
				Id = _configurationId,
				Version = _configurationVersion,
				LegalEntityId = ParseGuid(ConfigurationLegalEntityId, "Legal entity"),
				FiscalCalendarId = ParseGuid(ConfigurationFiscalCalendarId, "Fiscal calendar"),
				InvoicePostingProfileId = ParseLong(InvoiceProfileId, "Invoice posting profile"),
				CreditNotePostingProfileId = ParseLong(CreditNoteProfileId, "Credit-note posting profile"),
				PaymentPostingProfileId = ParseLong(PaymentProfileId, "Payment posting profile"),
				WriteOffPostingProfileId = ParseLong(WriteOffProfileId, "Write-off posting profile"),
				IsActive = ConfigurationIsActive
			}, cancellationToken);
			_configurationId = saved.Id;
			_configurationVersion = saved.Version;
			OnPropertyChanged(nameof(HasConfiguration));
			CompleteOperation(statusText: "Accounts Receivable configuration saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Accounts Receivable configuration could not be saved."); }
	}

	private async Task PostPaymentAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Posting customer payment...");
			var amount = ParseDecimal(PaymentAmount, "Payment amount");
			var allocations = new List<FinanceReceivableAllocationRequest>();
			if (SelectedOpenItem?.Direction == FinanceReceivableDirection.Debit && !string.IsNullOrWhiteSpace(PaymentAllocationAmount))
				allocations.Add(new FinanceReceivableAllocationRequest(SelectedOpenItem.Id, ParseDecimal(PaymentAllocationAmount, "Allocation amount")));
			var payment = await _receivables.PostPaymentAsync(new FinanceReceivablePaymentRequest
			{
				OperationId = Guid.NewGuid(),
				CustomerId = ParseLong(PaymentCustomerId, "Customer"),
				Currency = new CurrencyCode(PaymentCurrency),
				PaymentDate = DateOnly.FromDateTime(PaymentDate),
				Amount = amount,
				Reference = PaymentReference,
				Description = PaymentDescription,
				Allocations = allocations
			}, cancellationToken);
			PaymentReversalId = payment.Id.ToString(CultureInfo.InvariantCulture);
			await RefreshAfterMutationAsync(cancellationToken);
			CompleteOperation(statusText: $"Payment {payment.Id} posted.");
		}
		catch (Exception exception) { FailOperation(exception, "Customer payment could not be posted."); }
	}

	private async Task ReversePaymentAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Reversing customer payment...");
			var payment = await _receivables.ReversePaymentAsync(ParseLong(PaymentReversalId, "Payment ID"), new FinanceReceivableReversalRequest
			{
				OperationId = Guid.NewGuid(),
				PostingDate = DateOnly.FromDateTime(PaymentReversalDate),
				Reason = PaymentReversalReason
			}, cancellationToken);
			await RefreshAfterMutationAsync(cancellationToken);
			CompleteOperation(statusText: $"Payment {payment.Id} reversed.");
		}
		catch (Exception exception) { FailOperation(exception, "Customer payment could not be reversed."); }
	}

	private async Task AllocateCreditAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Allocating customer credit...");
			await _receivables.AllocateCreditAsync(
				Guid.NewGuid(),
				ParseLong(AllocationCreditOpenItemId, "Credit open item"),
				DateOnly.FromDateTime(AllocationDate),
				[new FinanceReceivableAllocationRequest(ParseLong(AllocationDebitOpenItemId, "Debit open item"), ParseDecimal(AllocationAmount, "Allocation amount"))],
				cancellationToken);
			await RefreshAfterMutationAsync(cancellationToken);
			CompleteOperation(statusText: "Customer credit allocated.");
		}
		catch (Exception exception) { FailOperation(exception, "Customer credit could not be allocated."); }
	}

	private async Task PostWriteOffAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Posting receivable write-off...");
			var result = await _receivables.PostWriteOffAsync(new FinanceReceivableWriteOffRequest
			{
				OperationId = Guid.NewGuid(),
				OpenItemId = ParseLong(WriteOffOpenItemId, "Open item"),
				PostingDate = DateOnly.FromDateTime(WriteOffDate),
				Amount = ParseDecimal(WriteOffAmount, "Write-off amount"),
				Reason = WriteOffReason
			}, cancellationToken);
			WriteOffReversalId = result.Id.ToString(CultureInfo.InvariantCulture);
			await RefreshAfterMutationAsync(cancellationToken);
			CompleteOperation(statusText: $"Write-off {result.Id} posted.");
		}
		catch (Exception exception) { FailOperation(exception, "Receivable write-off could not be posted."); }
	}

	private async Task ReverseWriteOffAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Reversing receivable write-off...");
			var result = await _receivables.ReverseWriteOffAsync(ParseLong(WriteOffReversalId, "Write-off ID"), new FinanceReceivableReversalRequest
			{
				OperationId = Guid.NewGuid(),
				PostingDate = DateOnly.FromDateTime(WriteOffReversalDate),
				Reason = WriteOffReversalReason
			}, cancellationToken);
			await RefreshAfterMutationAsync(cancellationToken);
			CompleteOperation(statusText: $"Write-off {result.Id} reversed.");
		}
		catch (Exception exception) { FailOperation(exception, "Receivable write-off could not be reversed."); }
	}

	private async Task LoadStatementAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Loading customer statement...");
			var rows = await _receivables.GetCustomerStatementAsync(
				ParseLong(StatementCustomerId, "Customer"),
				new CurrencyCode(StatementCurrency),
				DateOnly.FromDateTime(StatementFromDate),
				DateOnly.FromDateTime(StatementToDate),
				cancellationToken);
			Replace(StatementRows, rows);
			CompleteOperation(rows.Count == 0, "Customer statement loaded.");
		}
		catch (Exception exception) { FailOperation(exception, "Customer statement could not be loaded."); }
	}

	private async Task SaveDunningPolicyAsync(CancellationToken cancellationToken)
	{
		try
		{
			BeginOperation("Saving dunning policy...");
			var selected = SelectedDunningPolicy;
			var policy = await _receivables.SaveDunningPolicyAsync(new FinanceDunningPolicy
			{
				Id = selected?.Id ?? 0,
				Version = selected?.Version ?? 0,
				LegalEntityId = ParseGuid(ConfigurationLegalEntityId, "Legal entity"),
				Code = DunningPolicyCode,
				Name = DunningPolicyName,
				IsActive = true,
				Levels =
				[
					new FinanceDunningLevel { LevelNumber = 1, MinimumDaysOverdue = ParseInt(DunningLevel1Days, "Dunning level 1 days"), Code = "LEVEL1", Name = "Level 1" },
					new FinanceDunningLevel { LevelNumber = 2, MinimumDaysOverdue = ParseInt(DunningLevel2Days, "Dunning level 2 days"), Code = "LEVEL2", Name = "Level 2" },
					new FinanceDunningLevel { LevelNumber = 3, MinimumDaysOverdue = ParseInt(DunningLevel3Days, "Dunning level 3 days"), Code = "LEVEL3", Name = "Level 3" }
				]
			}, cancellationToken);
			await LoadDunningPoliciesAsync(cancellationToken);
			SelectedDunningPolicy = DunningPolicies.FirstOrDefault(value => value.Id == policy.Id);
			CompleteOperation(statusText: "Dunning policy saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Dunning policy could not be saved."); }
	}

	private async Task RunDunningAsync(CancellationToken cancellationToken)
	{
		try
		{
			var policy = SelectedDunningPolicy ?? throw new InvalidOperationException("Select a dunning policy first.");
			BeginOperation("Running dunning assessment...");
			var run = await _receivables.RunDunningAsync(new FinanceDunningRunRequest { OperationId = Guid.NewGuid(), PolicyId = policy.Id, AsOfDate = DateOnly.FromDateTime(DunningAsOfDate) }, cancellationToken);
			Replace(DunningRunLines, run.Lines);
			CompleteOperation(run.Lines.Count == 0, $"Dunning run {run.Id} completed with {run.Lines.Count:N0} item(s).");
		}
		catch (Exception exception) { FailOperation(exception, "Dunning assessment could not be completed."); }
	}

	private async Task RefreshAfterMutationAsync(CancellationToken cancellationToken)
	{
		await LoadOpenItemsCoreAsync(cancellationToken);
		await LoadAgingCoreAsync(cancellationToken);
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		_pageNumber--;
		await LoadOpenItemsAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		_pageNumber++;
		await LoadOpenItemsAsync(cancellationToken);
	}

	private void ApplySelectedOpenItem(FinanceReceivableOpenItem? value)
	{
		if (value is null) return;
		StatementCustomerId = value.CustomerId.ToString(CultureInfo.InvariantCulture);
		StatementCurrency = value.Currency.Value;
		if (value.Direction == FinanceReceivableDirection.Debit)
		{
			PaymentCustomerId = value.CustomerId.ToString(CultureInfo.InvariantCulture);
			PaymentCurrency = value.Currency.Value;
			PaymentAllocationAmount = Format(value.RemainingAmount);
			AllocationDebitOpenItemId = value.Id.ToString(CultureInfo.InvariantCulture);
			AllocationAmount = Format(value.RemainingAmount);
			WriteOffOpenItemId = value.Id.ToString(CultureInfo.InvariantCulture);
			WriteOffAmount = Format(value.RemainingAmount);
		}
		else
		{
			AllocationCreditOpenItemId = value.Id.ToString(CultureInfo.InvariantCulture);
			AllocationAmount = Format(value.RemainingAmount);
		}
	}

	private void LoadPolicyEditor(FinanceDunningPolicy policy)
	{
		DunningPolicyCode = policy.Code;
		DunningPolicyName = policy.Name;
		var levels = policy.Levels.OrderBy(value => value.LevelNumber).ToArray();
		if (levels.Length > 0) DunningLevel1Days = levels[0].MinimumDaysOverdue.ToString(CultureInfo.InvariantCulture);
		if (levels.Length > 1) DunningLevel2Days = levels[1].MinimumDaysOverdue.ToString(CultureInfo.InvariantCulture);
		if (levels.Length > 2) DunningLevel3Days = levels[2].MinimumDaysOverdue.ToString(CultureInfo.InvariantCulture);
	}

	private void RaisePaging()
	{
		OnPropertyChanged(nameof(PageNumber));
		OnPropertyChanged(nameof(TotalCount));
		OnPropertyChanged(nameof(HasNextPage));
		OnPropertyChanged(nameof(ShowPaging));
		OnPropertyChanged(nameof(PageDisplay));
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	private void SetString(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		value ??= string.Empty;
		if (field == value) return;
		field = value;
		OnPropertyChanged(propertyName);
	}

	private void SetField(ref decimal field, decimal value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (field == value) return;
		field = value;
		OnPropertyChanged(propertyName);
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}

	private static long ParseLong(string value, string name) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0 ? result : throw new InvalidOperationException($"{name} must be a positive integer.");
	private static int ParseInt(string value, string name) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0 ? result : throw new InvalidOperationException($"{name} must be a non-negative integer.");
	private static Guid ParseGuid(string value, string name) => Guid.TryParse(value, out var result) && result != Guid.Empty ? result : throw new InvalidOperationException($"{name} must be a valid GUID.");
	private static decimal ParseDecimal(string value, string name)
	{
		if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var local) && local > 0m) return local;
		if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) && invariant > 0m) return invariant;
		throw new InvalidOperationException($"{name} must be a positive amount.");
	}
	private static string Format(decimal value) => value.ToString("0.00#########", CultureInfo.CurrentCulture);

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		_loadRequest.Dispose();
		RefreshCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
		SaveConfigurationCommand.Dispose();
		PostPaymentCommand.Dispose();
		ReversePaymentCommand.Dispose();
		AllocateCreditCommand.Dispose();
		PostWriteOffCommand.Dispose();
		ReverseWriteOffCommand.Dispose();
		LoadStatementCommand.Dispose();
		SaveDunningPolicyCommand.Dispose();
		RunDunningCommand.Dispose();
	}
}
