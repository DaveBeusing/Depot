// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinancePayablesViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly FinanceAccountsPayableService _payables;
	private readonly LatestRequest _loadRequest = new();
	private string _searchText = string.Empty;
	private bool _includeSettled;
	private int _pageNumber = 1;
	private long _totalCount;
	private FinanceSupplierDocument? _selectedDocument;
	private FinancePayableOpenItem? _selectedOpenItem;
	private FinanceSupplierDocumentLineDraftEditor? _selectedDraftLine;
	private long _configurationId;
	private long _configurationVersion;
	private string _configurationLegalEntityId = string.Empty;
	private string _configurationFiscalCalendarId = string.Empty;
	private string _invoiceProfileId = string.Empty;
	private string _creditNoteProfileId = string.Empty;
	private string _paymentProfileId = string.Empty;
	private bool _configurationIsActive = true;
	private FinancePayableDocumentKind _draftKind = FinancePayableDocumentKind.Invoice;
	private string _draftSupplierId = string.Empty;
	private string _draftDocumentNumber = string.Empty;
	private string _draftInternalReference = string.Empty;
	private DateTime _draftDocumentDate = DateTime.Today;
	private DateTime _draftDueDate = DateTime.Today;
	private string _draftCurrency = string.Empty;
	private string _approvalComment = string.Empty;
	private bool _approveMatchException;
	private string _matchExceptionReason = string.Empty;
	private DateTime _documentReversalDate = DateTime.Today;
	private string _documentReversalReason = string.Empty;
	private string _paymentSupplierId = string.Empty;
	private string _paymentCurrency = string.Empty;
	private DateTime _paymentDate = DateTime.Today;
	private string _paymentAmount = string.Empty;
	private string _paymentAllocationAmount = string.Empty;
	private string _paymentReference = string.Empty;
	private string _paymentDescription = "Supplier payment";
	private string _paymentReversalId = string.Empty;
	private DateTime _paymentReversalDate = DateTime.Today;
	private string _paymentReversalReason = string.Empty;
	private string _allocationDebitOpenItemId = string.Empty;
	private string _allocationCreditOpenItemId = string.Empty;
	private string _allocationAmount = string.Empty;
	private DateTime _allocationDate = DateTime.Today;
	private DateTime _agingDate = DateTime.Today;
	private string _statementSupplierId = string.Empty;
	private string _statementCurrency = string.Empty;
	private DateTime _statementFromDate = DateTime.Today.AddMonths(-3);
	private DateTime _statementToDate = DateTime.Today;
	private decimal _totalPayable;
	private decimal _unappliedDebits;
	private decimal _netExposure;
	private bool _disposed;

	public FinancePayablesViewModel(FinanceAccountsPayableService payables)
	{
		_payables = payables;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync, () => CanManage);
		NewDraftCommand = new RelayCommand(NewDraft, () => CanCreateDocuments);
		AddLineCommand = new RelayCommand(AddLine, () => CanCreateDocuments);
		RemoveLineCommand = new RelayCommand(RemoveSelectedLine, () => CanCreateDocuments && SelectedDraftLine is not null && DraftLines.Count > 1);
		SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => CanCreateDocuments);
		SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => CanSubmitDocuments && SelectedDocument?.Status == FinancePayableDocumentStatus.Draft);
		ApproveCommand = new AsyncRelayCommand(() => DecideAsync(true), () => CanApproveDocuments && SelectedDocument?.Status == FinancePayableDocumentStatus.PendingApproval);
		RejectCommand = new AsyncRelayCommand(() => DecideAsync(false), () => CanApproveDocuments && SelectedDocument?.Status == FinancePayableDocumentStatus.PendingApproval);
		PostCommand = new AsyncRelayCommand(PostAsync, () => CanPostDocuments && SelectedDocument?.Status == FinancePayableDocumentStatus.Approved);
		ReverseDocumentCommand = new AsyncRelayCommand(ReverseDocumentAsync, () => CanReverseDocuments && SelectedDocument?.Status == FinancePayableDocumentStatus.Posted);
		PostPaymentCommand = new AsyncRelayCommand(PostPaymentAsync, () => CanPostPayments);
		ReversePaymentCommand = new AsyncRelayCommand(ReversePaymentAsync, () => CanReversePayments);
		AllocateDebitCommand = new AsyncRelayCommand(AllocateDebitAsync, () => CanPostPayments);
		LoadStatementCommand = new AsyncRelayCommand(LoadStatementAsync);
		NewDraft();
	}

	public ObservableCollection<FinanceSupplierDocument> Documents { get; } = [];
	public ObservableCollection<FinancePayableOpenItem> OpenItems { get; } = [];
	public ObservableCollection<FinancePayableAgingSummary> Aging { get; } = [];
	public ObservableCollection<FinanceSupplierStatementRow> StatementRows { get; } = [];
	public ObservableCollection<FinanceSupplierDocumentLineDraftEditor> DraftLines { get; } = [];

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public AsyncRelayCommand SaveConfigurationCommand { get; }
	public RelayCommand NewDraftCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand SaveDraftCommand { get; }
	public AsyncRelayCommand SubmitCommand { get; }
	public AsyncRelayCommand ApproveCommand { get; }
	public AsyncRelayCommand RejectCommand { get; }
	public AsyncRelayCommand PostCommand { get; }
	public AsyncRelayCommand ReverseDocumentCommand { get; }
	public AsyncRelayCommand PostPaymentCommand { get; }
	public AsyncRelayCommand ReversePaymentCommand { get; }
	public AsyncRelayCommand AllocateDebitCommand { get; }
	public AsyncRelayCommand LoadStatementCommand { get; }

	public bool CanManage => _payables.CanManage;
	public bool CanCreateDocuments => _payables.CanCreateDocuments;
	public bool CanSubmitDocuments => _payables.CanSubmitDocuments;
	public bool CanApproveDocuments => _payables.CanApproveDocuments;
	public bool CanApproveMatchExceptions => _payables.CanApproveMatchExceptions;
	public bool CanPostDocuments => _payables.CanPostDocuments;
	public bool CanReverseDocuments => _payables.CanReverseDocuments;
	public bool CanPostPayments => _payables.CanPostPayments;
	public bool CanReversePayments => _payables.CanReversePayments;
	public bool HasConfiguration => _configurationId > 0;

	public string SearchText { get => _searchText; set => SetString(ref _searchText, value); }
	public bool IncludeSettled { get => _includeSettled; set { if (_includeSettled == value) return; _includeSettled = value; OnPropertyChanged(); } }
	public int PageNumber => _pageNumber;
	public long TotalCount => _totalCount;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public bool ShowPaging => TotalCount > PageSize;
	public string PageDisplay => $"Page {PageNumber} - {TotalCount:N0} supplier documents";
	public decimal TotalPayable { get => _totalPayable; private set => SetDecimal(ref _totalPayable, value); }
	public decimal UnappliedDebits { get => _unappliedDebits; private set => SetDecimal(ref _unappliedDebits, value); }
	public decimal NetExposure { get => _netExposure; private set => SetDecimal(ref _netExposure, value); }

	public FinanceSupplierDocument? SelectedDocument
	{
		get => _selectedDocument;
		set
		{
			if (ReferenceEquals(_selectedDocument, value)) return;
			_selectedDocument = value;
			OnPropertyChanged();
			if (value is not null) LoadDraftEditor(value);
			RaiseDocumentCommands();
		}
	}

	public FinancePayableOpenItem? SelectedOpenItem
	{
		get => _selectedOpenItem;
		set
		{
			if (ReferenceEquals(_selectedOpenItem, value)) return;
			_selectedOpenItem = value;
			OnPropertyChanged();
			if (value is null) return;
			if (value.Direction == FinancePayableDirection.Credit) AllocationCreditOpenItemId = value.Id.ToString(CultureInfo.InvariantCulture);
			else AllocationDebitOpenItemId = value.Id.ToString(CultureInfo.InvariantCulture);
		}
	}

	public FinanceSupplierDocumentLineDraftEditor? SelectedDraftLine
	{
		get => _selectedDraftLine;
		set { if (ReferenceEquals(_selectedDraftLine, value)) return; _selectedDraftLine = value; OnPropertyChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); }
	}

	public string ConfigurationLegalEntityId { get => _configurationLegalEntityId; set => SetString(ref _configurationLegalEntityId, value); }
	public string ConfigurationFiscalCalendarId { get => _configurationFiscalCalendarId; set => SetString(ref _configurationFiscalCalendarId, value); }
	public string InvoiceProfileId { get => _invoiceProfileId; set => SetString(ref _invoiceProfileId, value); }
	public string CreditNoteProfileId { get => _creditNoteProfileId; set => SetString(ref _creditNoteProfileId, value); }
	public string PaymentProfileId { get => _paymentProfileId; set => SetString(ref _paymentProfileId, value); }
	public bool ConfigurationIsActive { get => _configurationIsActive; set { if (_configurationIsActive == value) return; _configurationIsActive = value; OnPropertyChanged(); } }
	public FinancePayableDocumentKind DraftKind { get => _draftKind; set { if (_draftKind == value) return; _draftKind = value; OnPropertyChanged(); } }
	public IReadOnlyList<FinancePayableDocumentKind> DocumentKinds { get; } = Enum.GetValues<FinancePayableDocumentKind>();
	public string DraftSupplierId { get => _draftSupplierId; set => SetString(ref _draftSupplierId, value); }
	public string DraftDocumentNumber { get => _draftDocumentNumber; set => SetString(ref _draftDocumentNumber, value); }
	public string DraftInternalReference { get => _draftInternalReference; set => SetString(ref _draftInternalReference, value); }
	public DateTime DraftDocumentDate { get => _draftDocumentDate; set { if (_draftDocumentDate == value) return; _draftDocumentDate = value; OnPropertyChanged(); } }
	public DateTime DraftDueDate { get => _draftDueDate; set { if (_draftDueDate == value) return; _draftDueDate = value; OnPropertyChanged(); } }
	public string DraftCurrency { get => _draftCurrency; set => SetString(ref _draftCurrency, value); }
	public string ApprovalComment { get => _approvalComment; set => SetString(ref _approvalComment, value); }
	public bool ApproveMatchException { get => _approveMatchException; set { if (_approveMatchException == value) return; _approveMatchException = value; OnPropertyChanged(); } }
	public string MatchExceptionReason { get => _matchExceptionReason; set => SetString(ref _matchExceptionReason, value); }
	public DateTime DocumentReversalDate { get => _documentReversalDate; set { if (_documentReversalDate == value) return; _documentReversalDate = value; OnPropertyChanged(); } }
	public string DocumentReversalReason { get => _documentReversalReason; set => SetString(ref _documentReversalReason, value); }
	public string PaymentSupplierId { get => _paymentSupplierId; set => SetString(ref _paymentSupplierId, value); }
	public string PaymentCurrency { get => _paymentCurrency; set => SetString(ref _paymentCurrency, value); }
	public DateTime PaymentDate { get => _paymentDate; set { if (_paymentDate == value) return; _paymentDate = value; OnPropertyChanged(); } }
	public string PaymentAmount { get => _paymentAmount; set => SetString(ref _paymentAmount, value); }
	public string PaymentAllocationAmount { get => _paymentAllocationAmount; set => SetString(ref _paymentAllocationAmount, value); }
	public string PaymentReference { get => _paymentReference; set => SetString(ref _paymentReference, value); }
	public string PaymentDescription { get => _paymentDescription; set => SetString(ref _paymentDescription, value); }
	public string PaymentReversalId { get => _paymentReversalId; set => SetString(ref _paymentReversalId, value); }
	public DateTime PaymentReversalDate { get => _paymentReversalDate; set { if (_paymentReversalDate == value) return; _paymentReversalDate = value; OnPropertyChanged(); } }
	public string PaymentReversalReason { get => _paymentReversalReason; set => SetString(ref _paymentReversalReason, value); }
	public string AllocationDebitOpenItemId { get => _allocationDebitOpenItemId; set => SetString(ref _allocationDebitOpenItemId, value); }
	public string AllocationCreditOpenItemId { get => _allocationCreditOpenItemId; set => SetString(ref _allocationCreditOpenItemId, value); }
	public string AllocationAmount { get => _allocationAmount; set => SetString(ref _allocationAmount, value); }
	public DateTime AllocationDate { get => _allocationDate; set { if (_allocationDate == value) return; _allocationDate = value; OnPropertyChanged(); } }
	public DateTime AgingDate { get => _agingDate; set { if (_agingDate == value) return; _agingDate = value; OnPropertyChanged(); } }
	public string StatementSupplierId { get => _statementSupplierId; set => SetString(ref _statementSupplierId, value); }
	public string StatementCurrency { get => _statementCurrency; set => SetString(ref _statementCurrency, value); }
	public DateTime StatementFromDate { get => _statementFromDate; set { if (_statementFromDate == value) return; _statementFromDate = value; OnPropertyChanged(); } }
	public DateTime StatementToDate { get => _statementToDate; set { if (_statementToDate == value) return; _statementToDate = value; OnPropertyChanged(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		BeginOperation("Loading Accounts Payable...");
		try
		{
			await LoadConfigurationAsync(request.Token);
			await LoadDocumentsAsync(request.Token);
			await LoadOpenItemsAndAgingAsync(request.Token);
			if (!request.IsCurrent) return;
			CompleteOperation(Documents.Count == 0 && OpenItems.Count == 0, "Accounts Payable loaded.");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Accounts Payable could not be loaded."); }
	}

	private async Task LoadConfigurationAsync(CancellationToken cancellationToken)
	{
		var configuration = await _payables.GetConfigurationAsync(cancellationToken);
		if (configuration is null) return;
		_configurationId = configuration.Id;
		_configurationVersion = configuration.Version;
		ConfigurationLegalEntityId = configuration.LegalEntityId.ToString("D");
		ConfigurationFiscalCalendarId = configuration.FiscalCalendarId.ToString("D");
		InvoiceProfileId = configuration.InvoicePostingProfileId.ToString(CultureInfo.InvariantCulture);
		CreditNoteProfileId = configuration.CreditNotePostingProfileId.ToString(CultureInfo.InvariantCulture);
		PaymentProfileId = configuration.PaymentPostingProfileId.ToString(CultureInfo.InvariantCulture);
		ConfigurationIsActive = configuration.IsActive;
		OnPropertyChanged(nameof(HasConfiguration));
	}

	private async Task LoadDocumentsAsync(CancellationToken cancellationToken)
	{
		var page = await _payables.SearchDocumentsAsync(SearchText, null, PageNumber, PageSize, cancellationToken);
		Replace(Documents, page.Items);
		_pageNumber = page.PageNumber;
		_totalCount = page.TotalCount;
		RaisePaging();
	}

	private async Task LoadOpenItemsAndAgingAsync(CancellationToken cancellationToken)
	{
		var page = await _payables.SearchOpenItemsAsync(SearchText, IncludeSettled, 1, PageSize, cancellationToken);
		Replace(OpenItems, page.Items);
		var aging = await _payables.GetAgingAsync(DateOnly.FromDateTime(AgingDate), cancellationToken);
		Replace(Aging, aging);
		TotalPayable = aging.Sum(value => value.TotalPayable);
		UnappliedDebits = aging.Sum(value => value.UnappliedDebits);
		NetExposure = aging.Sum(value => value.NetExposure);
	}

	private async Task SaveConfigurationAsync()
	{
		try
		{
			BeginOperation("Saving Accounts Payable configuration...");
			var saved = await _payables.SaveConfigurationAsync(new FinancePayablesConfiguration
			{
				Id = _configurationId,
				Version = _configurationVersion,
				LegalEntityId = Guid.Parse(ConfigurationLegalEntityId),
				FiscalCalendarId = Guid.Parse(ConfigurationFiscalCalendarId),
				InvoicePostingProfileId = ParseLong(InvoiceProfileId, "invoice posting profile"),
				CreditNotePostingProfileId = ParseLong(CreditNoteProfileId, "credit-note posting profile"),
				PaymentPostingProfileId = ParseLong(PaymentProfileId, "payment posting profile"),
				IsActive = ConfigurationIsActive
			});
			_configurationId = saved.Id;
			_configurationVersion = saved.Version;
			OnPropertyChanged(nameof(HasConfiguration));
			CompleteOperation(false, "Accounts Payable configuration saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Configuration could not be saved."); }
	}

	private void NewDraft()
	{
		SelectedDocument = null;
		DraftKind = FinancePayableDocumentKind.Invoice;
		DraftSupplierId = string.Empty;
		DraftDocumentNumber = string.Empty;
		DraftInternalReference = string.Empty;
		DraftDocumentDate = DateTime.Today;
		DraftDueDate = DateTime.Today;
		DraftCurrency = string.Empty;
		DraftLines.Clear();
		AddLine();
		CompleteOperation(false, "New supplier document draft.");
	}

	private void AddLine()
	{
		var line = new FinanceSupplierDocumentLineDraftEditor();
		DraftLines.Add(line);
		SelectedDraftLine = line;
		RemoveLineCommand.RaiseCanExecuteChanged();
	}

	private void RemoveSelectedLine()
	{
		if (SelectedDraftLine is null || DraftLines.Count <= 1) return;
		var index = DraftLines.IndexOf(SelectedDraftLine);
		DraftLines.Remove(SelectedDraftLine);
		SelectedDraftLine = DraftLines[Math.Clamp(index - 1, 0, DraftLines.Count - 1)];
		RemoveLineCommand.RaiseCanExecuteChanged();
	}

	private async Task SaveDraftAsync()
	{
		try
		{
			BeginOperation("Saving supplier document...");
			var current = SelectedDocument;
			var saved = await _payables.SaveDraftAsync(new FinanceSupplierDocumentDraft
			{
				Id = current?.Status == FinancePayableDocumentStatus.Draft ? current.Id : 0,
				Version = current?.Status == FinancePayableDocumentStatus.Draft ? current.Version : 0,
				Kind = DraftKind,
				SupplierId = ParseLong(DraftSupplierId, "supplier"),
				SupplierDocumentNumber = DraftDocumentNumber,
				InternalReference = DraftInternalReference,
				DocumentDate = DateOnly.FromDateTime(DraftDocumentDate),
				DueDate = DateOnly.FromDateTime(DraftDueDate),
				Currency = new CurrencyCode(DraftCurrency),
				Lines = DraftLines.Select(value => value.ToModel()).ToArray()
			});
			SelectedDocument = saved;
			await LoadDocumentsAsync(CancellationToken.None);
			CompleteOperation(false, "Supplier document saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Supplier document could not be saved."); }
	}

	private async Task SubmitAsync()
	{
		if (SelectedDocument is null) return;
		try { BeginOperation("Submitting supplier document..."); SelectedDocument = await _payables.SubmitAsync(SelectedDocument.Id, SelectedDocument.Version); await LoadDocumentsAsync(CancellationToken.None); CompleteOperation(false, "Supplier document submitted for approval."); }
		catch (Exception exception) { FailOperation(exception, "Supplier document could not be submitted."); }
	}

	private async Task DecideAsync(bool approve)
	{
		if (SelectedDocument is null) return;
		try
		{
			BeginOperation(approve ? "Approving supplier document..." : "Rejecting supplier document...");
			SelectedDocument = await _payables.DecideAsync(SelectedDocument.Id, new FinanceSupplierApprovalRequest { ExpectedVersion = SelectedDocument.Version, Approve = approve, Comment = ApprovalComment, ApproveMatchException = ApproveMatchException, MatchExceptionReason = MatchExceptionReason });
			await LoadDocumentsAsync(CancellationToken.None);
			CompleteOperation(false, approve ? "Supplier document approved." : "Supplier document rejected.");
		}
		catch (Exception exception) { FailOperation(exception, "Supplier document decision failed."); }
	}

	private async Task PostAsync()
	{
		if (SelectedDocument is null) return;
		try { BeginOperation("Posting supplier document..."); SelectedDocument = await _payables.PostAsync(SelectedDocument.Id, new FinanceSupplierPostingRequest { OperationId = Guid.NewGuid(), ExpectedVersion = SelectedDocument.Version }); await LoadAsync(); CompleteOperation(false, "Supplier document posted to Accounts Payable and General Ledger."); }
		catch (Exception exception) { FailOperation(exception, "Supplier document could not be posted."); }
	}

	private async Task ReverseDocumentAsync()
	{
		if (SelectedDocument is null) return;
		try { BeginOperation("Reversing supplier document..."); SelectedDocument = await _payables.ReverseDocumentAsync(SelectedDocument.Id, new FinancePayableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = DateOnly.FromDateTime(DocumentReversalDate), Reason = DocumentReversalReason }); await LoadAsync(); CompleteOperation(false, "Supplier document reversed."); }
		catch (Exception exception) { FailOperation(exception, "Supplier document could not be reversed."); }
	}

	private async Task PostPaymentAsync()
	{
		try
		{
			BeginOperation("Posting supplier payment...");
			var allocations = new List<FinancePayableAllocationRequest>();
			if (SelectedOpenItem?.Direction == FinancePayableDirection.Credit && TryParseDecimal(PaymentAllocationAmount, out var allocation) && allocation > 0m) allocations.Add(new FinancePayableAllocationRequest(SelectedOpenItem.Id, allocation));
			var payment = await _payables.PostPaymentAsync(new FinancePayablePaymentRequest { OperationId = Guid.NewGuid(), SupplierId = ParseLong(PaymentSupplierId, "supplier"), Currency = new CurrencyCode(PaymentCurrency), PaymentDate = DateOnly.FromDateTime(PaymentDate), Amount = ParseDecimal(PaymentAmount, "payment amount"), Reference = PaymentReference, Description = PaymentDescription, Allocations = allocations });
			PaymentReversalId = payment.Id.ToString(CultureInfo.InvariantCulture);
			await LoadOpenItemsAndAgingAsync(CancellationToken.None);
			CompleteOperation(false, "Supplier payment posted.");
		}
		catch (Exception exception) { FailOperation(exception, "Supplier payment could not be posted."); }
	}

	private async Task ReversePaymentAsync()
	{
		try { BeginOperation("Reversing supplier payment..."); await _payables.ReversePaymentAsync(ParseLong(PaymentReversalId, "payment"), new FinancePayableReversalRequest { OperationId = Guid.NewGuid(), PostingDate = DateOnly.FromDateTime(PaymentReversalDate), Reason = PaymentReversalReason }); await LoadOpenItemsAndAgingAsync(CancellationToken.None); CompleteOperation(false, "Supplier payment reversed."); }
		catch (Exception exception) { FailOperation(exception, "Supplier payment could not be reversed."); }
	}

	private async Task AllocateDebitAsync()
	{
		try { BeginOperation("Allocating supplier debit..."); await _payables.AllocateDebitAsync(Guid.NewGuid(), ParseLong(AllocationDebitOpenItemId, "debit open item"), DateOnly.FromDateTime(AllocationDate), [new FinancePayableAllocationRequest(ParseLong(AllocationCreditOpenItemId, "credit open item"), ParseDecimal(AllocationAmount, "allocation amount"))]); await LoadOpenItemsAndAgingAsync(CancellationToken.None); CompleteOperation(false, "Supplier balance allocated."); }
		catch (Exception exception) { FailOperation(exception, "Supplier balance could not be allocated."); }
	}

	private async Task LoadStatementAsync()
	{
		try { BeginOperation("Loading supplier statement..."); var rows = await _payables.GetSupplierStatementAsync(ParseLong(StatementSupplierId, "supplier"), new CurrencyCode(StatementCurrency), DateOnly.FromDateTime(StatementFromDate), DateOnly.FromDateTime(StatementToDate)); Replace(StatementRows, rows); CompleteOperation(rows.Count == 0, "Supplier statement loaded."); }
		catch (Exception exception) { FailOperation(exception, "Supplier statement could not be loaded."); }
	}

	private async Task PreviousPageAsync() { if (_pageNumber <= 1) return; _pageNumber--; await LoadDocumentsAsync(CancellationToken.None); RaisePaging(); }
	private async Task NextPageAsync() { if (!HasNextPage) return; _pageNumber++; await LoadDocumentsAsync(CancellationToken.None); RaisePaging(); }

	private void LoadDraftEditor(FinanceSupplierDocument value)
	{
		DraftKind = value.Kind;
		DraftSupplierId = value.SupplierId.ToString(CultureInfo.InvariantCulture);
		DraftDocumentNumber = value.SupplierDocumentNumber;
		DraftInternalReference = value.InternalReference ?? string.Empty;
		DraftDocumentDate = value.DocumentDate.ToDateTime(TimeOnly.MinValue);
		DraftDueDate = value.DueDate.ToDateTime(TimeOnly.MinValue);
		DraftCurrency = value.Currency.Value;
		DraftLines.Clear();
		foreach (var line in value.Lines) DraftLines.Add(new FinanceSupplierDocumentLineDraftEditor(line));
		if (DraftLines.Count == 0) AddLine(); else SelectedDraftLine = DraftLines[0];
		ApprovalComment = value.ApprovalComment ?? string.Empty;
		ApproveMatchException = value.MatchExceptionApproved;
		MatchExceptionReason = value.MatchExceptionReason ?? string.Empty;
	}

	private void RaiseDocumentCommands()
	{
		SubmitCommand.RaiseCanExecuteChanged(); ApproveCommand.RaiseCanExecuteChanged(); RejectCommand.RaiseCanExecuteChanged(); PostCommand.RaiseCanExecuteChanged(); ReverseDocumentCommand.RaiseCanExecuteChanged();
	}
	private void RaisePaging() { OnPropertyChanged(nameof(PageNumber)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(HasNextPage)); OnPropertyChanged(nameof(ShowPaging)); OnPropertyChanged(nameof(PageDisplay)); PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	private void SetString(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) { value ??= string.Empty; if (field == value) return; field = value; OnPropertyChanged(propertyName); }
	private void SetDecimal(ref decimal field, decimal value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) { if (field == value) return; field = value; OnPropertyChanged(propertyName); }
	private static long ParseLong(string value, string name) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : throw new InvalidOperationException($"A valid {name} ID is required.");
	private static decimal ParseDecimal(string value, string name) => TryParseDecimal(value, out var parsed) && parsed > 0m ? parsed : throw new InvalidOperationException($"A positive {name} is required.");
	private static bool TryParseDecimal(string value, out decimal parsed) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

	public void Dispose() { if (_disposed) return; _disposed = true; _loadRequest.Dispose(); }
}

public sealed class FinanceSupplierDocumentLineDraftEditor : BaseViewModel
{
	private string _purchaseOrderLineId = string.Empty;
	private string _goodsReceiptLineId = string.Empty;
	private string _description = string.Empty;
	private string _quantity = "1";
	private string _unitPrice = "0";
	private string _taxAmount = "0";

	public FinanceSupplierDocumentLineDraftEditor() { }
	public FinanceSupplierDocumentLineDraftEditor(FinanceSupplierDocumentLine value)
	{
		_purchaseOrderLineId = value.PurchaseOrderLineId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
		_goodsReceiptLineId = value.GoodsReceiptLineId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
		_description = value.Description;
		_quantity = value.Quantity.ToString(CultureInfo.CurrentCulture);
		_unitPrice = value.UnitPrice.ToString(CultureInfo.CurrentCulture);
		_taxAmount = value.TaxAmount.ToString(CultureInfo.CurrentCulture);
	}

	public string PurchaseOrderLineId { get => _purchaseOrderLineId; set => Set(ref _purchaseOrderLineId, value); }
	public string GoodsReceiptLineId { get => _goodsReceiptLineId; set => Set(ref _goodsReceiptLineId, value); }
	public string Description { get => _description; set => Set(ref _description, value); }
	public string Quantity { get => _quantity; set => Set(ref _quantity, value); }
	public string UnitPrice { get => _unitPrice; set => Set(ref _unitPrice, value); }
	public string TaxAmount { get => _taxAmount; set => Set(ref _taxAmount, value); }

	public FinanceSupplierDocumentLineDraft ToModel() => new()
	{
		PurchaseOrderLineId = ParseOptionalId(PurchaseOrderLineId, "purchase-order line"),
		GoodsReceiptLineId = ParseOptionalId(GoodsReceiptLineId, "goods-receipt line"),
		Description = Description,
		Quantity = ParseDecimal(Quantity, "quantity"),
		UnitPrice = ParseNonNegative(UnitPrice, "unit price"),
		TaxAmount = ParseNonNegative(TaxAmount, "tax amount")
	};

	private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) { value ??= string.Empty; if (field == value) return; field = value; OnPropertyChanged(propertyName); }
	private static long? ParseOptionalId(string value, string name) { if (string.IsNullOrWhiteSpace(value)) return null; return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : throw new InvalidOperationException($"A valid {name} ID is required."); }
	private static decimal ParseDecimal(string value, string name) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) && parsed > 0m ? parsed : throw new InvalidOperationException($"A positive {name} is required.");
	private static decimal ParseNonNegative(string value, string name) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) && parsed >= 0m ? parsed : throw new InvalidOperationException($"A non-negative {name} is required.");
}
