// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ProcurementSourcingViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly ProcurementSourcingService _sourcing;
	private readonly SupplierService _suppliers;
	private readonly ItemService _items;
	private readonly Action _markPurchasingStale;
	private PurchaseRequisition? _selectedRequisition;
	private PurchaseRequisition _requisitionDraft = NewRequisitionDraft();
	private PurchaseRequisitionLine? _selectedRequisitionLine;
	private Item? _selectedItem;
	private int _lineQuantity = 1;
	private string? _lineNotes;
	private RequestForQuotation? _selectedRfq;
	private RequestForQuotationSupplier? _selectedQuoteSupplier;
	private SupplierQuoteResponse? _selectedQuoteResponse;
	private SupplierQuoteComparisonRow? _selectedComparisonRow;
	private string _searchText = string.Empty;
	private DateTime? _responseDueDate;
	private string _quoteCurrency = "EUR";
	private string? _supplierReference;
	private DateTime? _quoteValidUntil;
	private string? _decisionComment;
	private long? _lastConvertedPurchaseOrderId;
	private bool _disposed;

	public ProcurementSourcingViewModel(ProcurementSourcingService sourcing, SupplierService suppliers, ItemService items, Action markPurchasingStale)
	{
		_sourcing = sourcing ?? throw new ArgumentNullException(nameof(sourcing));
		_suppliers = suppliers ?? throw new ArgumentNullException(nameof(suppliers));
		_items = items ?? throw new ArgumentNullException(nameof(items));
		_markPurchasingStale = markPurchasingStale ?? throw new ArgumentNullException(nameof(markPurchasingStale));

		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewRequisitionCommand = new RelayCommand(NewRequisition, () => _sourcing.CanManageRequisitions);
		SaveRequisitionCommand = new AsyncRelayCommand(SaveRequisitionAsync, () => CanEditRequisition);
		SubmitRequisitionCommand = new AsyncRelayCommand(SubmitRequisitionAsync, () => CanSubmitRequisition);
		ApproveRequisitionCommand = new AsyncRelayCommand(ApproveRequisitionAsync, () => CanApproveRequisition);
		ReturnRequisitionCommand = new AsyncRelayCommand(ReturnRequisitionAsync, () => CanApproveRequisition);
		RejectRequisitionCommand = new AsyncRelayCommand(RejectRequisitionAsync, () => CanApproveRequisition);
		CancelRequisitionCommand = new AsyncRelayCommand(CancelRequisitionAsync, () => CanCancelRequisition);
		AddLineCommand = new RelayCommand(AddLine, () => CanEditRequisition && SelectedItem is not null && LineQuantity > 0);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => CanEditRequisition && SelectedRequisitionLine is not null);
		CreateRfqCommand = new AsyncRelayCommand(CreateRfqAsync, () => CanCreateRfq);
		CancelRfqCommand = new AsyncRelayCommand(CancelRfqAsync, () => CanCancelRfq);
		CaptureQuoteCommand = new AsyncRelayCommand(CaptureQuoteAsync, () => CanCaptureQuote);
		RefreshComparisonCommand = new AsyncRelayCommand(RefreshComparisonAsync, () => SelectedRfq is not null);
		SelectQuoteCommand = new AsyncRelayCommand(SelectQuoteAsync, () => CanSelectQuote);
		ConvertToPurchaseOrderCommand = new AsyncRelayCommand(ConvertToPurchaseOrderAsync, () => CanConvertToPurchaseOrder);
	}

	public ObservableCollection<PurchaseRequisition> Requisitions { get; } = [];
	public ObservableCollection<PurchaseRequisitionLine> RequisitionLines { get; } = [];
	public ObservableCollection<RequestForQuotation> Rfqs { get; } = [];
	public ObservableCollection<SupplierSelectionViewModel> RfqSupplierOptions { get; } = [];
	public ObservableCollection<RequestForQuotationSupplier> RfqRecipientOptions { get; } = [];
	public ObservableCollection<SupplierQuoteResponse> QuoteResponses { get; } = [];
	public ObservableCollection<QuoteLineDraftViewModel> QuoteLines { get; } = [];
	public ObservableCollection<SupplierQuoteComparisonRow> ComparisonRows { get; } = [];
	public ObservableCollection<Supplier> Suppliers { get; } = [];
	public ObservableCollection<Item> Items { get; } = [];

	public AsyncRelayCommand RefreshCommand { get; }
	public RelayCommand NewRequisitionCommand { get; }
	public AsyncRelayCommand SaveRequisitionCommand { get; }
	public AsyncRelayCommand SubmitRequisitionCommand { get; }
	public AsyncRelayCommand ApproveRequisitionCommand { get; }
	public AsyncRelayCommand ReturnRequisitionCommand { get; }
	public AsyncRelayCommand RejectRequisitionCommand { get; }
	public AsyncRelayCommand CancelRequisitionCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand CreateRfqCommand { get; }
	public AsyncRelayCommand CancelRfqCommand { get; }
	public AsyncRelayCommand CaptureQuoteCommand { get; }
	public AsyncRelayCommand RefreshComparisonCommand { get; }
	public AsyncRelayCommand SelectQuoteCommand { get; }
	public AsyncRelayCommand ConvertToPurchaseOrderCommand { get; }

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value ?? string.Empty; OnPropertyChanged(); } }
	public PurchaseRequisition RequisitionDraft { get => _requisitionDraft; private set { _requisitionDraft = value; OnPropertyChanged(); RaiseState(); } }
	public PurchaseRequisition? SelectedRequisition
	{
		get => _selectedRequisition;
		set
		{
			if (ReferenceEquals(_selectedRequisition, value)) return;
			_selectedRequisition = value;
			OnPropertyChanged();
			if (value is null) ApplyRequisition(NewRequisitionDraft()); else _ = OpenRequisitionAsync(value.Id);
			RaiseState();
		}
	}
	public PurchaseRequisitionLine? SelectedRequisitionLine { get => _selectedRequisitionLine; set { if (_selectedRequisitionLine == value) return; _selectedRequisitionLine = value; OnPropertyChanged(); RaiseState(); } }
	public Item? SelectedItem { get => _selectedItem; set { if (_selectedItem == value) return; _selectedItem = value; OnPropertyChanged(); RaiseState(); } }
	public int LineQuantity { get => _lineQuantity; set { if (_lineQuantity == value) return; _lineQuantity = value; OnPropertyChanged(); RaiseState(); } }
	public string? LineNotes { get => _lineNotes; set { if (_lineNotes == value) return; _lineNotes = value; OnPropertyChanged(); } }
	public RequestForQuotation? SelectedRfq
	{
		get => _selectedRfq;
		set
		{
			if (ReferenceEquals(_selectedRfq, value)) return;
			_selectedRfq = value;
			OnPropertyChanged();
			if (value is null) ClearRfqDetail(); else _ = OpenRfqAsync(value.Id);
			RaiseState();
		}
	}
	public RequestForQuotationSupplier? SelectedQuoteSupplier { get => _selectedQuoteSupplier; set { if (_selectedQuoteSupplier == value) return; _selectedQuoteSupplier = value; OnPropertyChanged(); RaiseState(); } }
	public SupplierQuoteResponse? SelectedQuoteResponse { get => _selectedQuoteResponse; set { if (_selectedQuoteResponse == value) return; _selectedQuoteResponse = value; OnPropertyChanged(); RaiseState(); } }
	public SupplierQuoteComparisonRow? SelectedComparisonRow { get => _selectedComparisonRow; set { if (_selectedComparisonRow == value) return; _selectedComparisonRow = value; OnPropertyChanged(); if (value is not null) SelectedQuoteResponse = QuoteResponses.FirstOrDefault(response => response.Id == value.SupplierQuoteResponseId); } }
	public DateTime? ResponseDueDate { get => _responseDueDate; set { if (_responseDueDate == value) return; _responseDueDate = value; OnPropertyChanged(); } }
	public string QuoteCurrency { get => _quoteCurrency; set { if (_quoteCurrency == value) return; _quoteCurrency = value ?? string.Empty; OnPropertyChanged(); } }
	public string? SupplierReference { get => _supplierReference; set { if (_supplierReference == value) return; _supplierReference = value; OnPropertyChanged(); } }
	public DateTime? QuoteValidUntil { get => _quoteValidUntil; set { if (_quoteValidUntil == value) return; _quoteValidUntil = value; OnPropertyChanged(); } }
	public string? DecisionComment { get => _decisionComment; set { if (_decisionComment == value) return; _decisionComment = value; OnPropertyChanged(); } }
	public long? LastConvertedPurchaseOrderId { get => _lastConvertedPurchaseOrderId; private set { if (_lastConvertedPurchaseOrderId == value) return; _lastConvertedPurchaseOrderId = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConversionStatus)); } }
	public string ConversionStatus => LastConvertedPurchaseOrderId is { } id ? $"Purchase order #{id} created from the selected quote." : string.Empty;

	public bool CanEditRequisition => _sourcing.CanManageRequisitions && RequisitionDraft.Status is PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Returned;
	public bool CanSubmitRequisition => _sourcing.CanManageRequisitions && RequisitionDraft.Id > 0 && RequisitionDraft.Status is PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Returned;
	public bool CanApproveRequisition => _sourcing.CanApproveRequisitions && RequisitionDraft.Id > 0 && RequisitionDraft.Status == PurchaseRequisitionStatus.Submitted;
	public bool CanCancelRequisition => _sourcing.CanManageRequisitions && RequisitionDraft.Id > 0 && RequisitionDraft.Status is PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Returned;
	public bool CanCreateRfq => _sourcing.CanManageSourcing && RequisitionDraft.Id > 0 && RequisitionDraft.Status == PurchaseRequisitionStatus.Approved && RfqSupplierOptions.Any(option => option.IsSelected);
	public bool CanCancelRfq => _sourcing.CanManageSourcing && SelectedRfq?.Status == RequestForQuotationStatus.Open;
	public bool CanCaptureQuote => _sourcing.CanManageSourcing && SelectedRfq?.Status == RequestForQuotationStatus.Open && SelectedQuoteSupplier is not null && QuoteLines.Count > 0;
	public bool CanSelectQuote => _sourcing.CanManageSourcing && SelectedRfq?.Status == RequestForQuotationStatus.Open && SelectedQuoteResponse is { Status: SupplierQuoteResponseStatus.Active } response && (response.ValidUntil is null || response.ValidUntil.Value.Date >= DateTime.Today);
	public bool CanConvertToPurchaseOrder => _sourcing.CanConvertSourcing && SelectedRfq?.Status == RequestForQuotationStatus.Awarded;

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading procurement sourcing");
		try
		{
			var requisitionsTask = _sourcing.CanViewRequisitions ? _sourcing.SearchRequisitionsAsync(SearchText, null, 1, PageSize, cancellationToken) : Task.FromResult(new PageResult<PurchaseRequisition>([], 1, PageSize, 0));
			var rfqsTask = _sourcing.CanViewSourcing ? _sourcing.SearchRfqsAsync(null, 1, PageSize, cancellationToken) : Task.FromResult(new PageResult<RequestForQuotation>([], 1, PageSize, 0));
			var suppliersTask = _suppliers.GetActiveAsync(cancellationToken);
			var itemsTask = _items.SearchItemsAsync(string.Empty, true, 1, 500, cancellationToken);
			await Task.WhenAll(requisitionsTask, rfqsTask, suppliersTask, itemsTask);
			Replace(Requisitions, (await requisitionsTask).Items);
			Replace(Rfqs, (await rfqsTask).Items);
			Replace(Suppliers, await suppliersTask);
			Replace(Items, (await itemsTask).Items);
			RefreshSupplierSelections();
			CompleteOperation(Requisitions.Count == 0 && Rfqs.Count == 0, "Procurement sourcing loaded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception ex) { FailOperation(ex, "Procurement sourcing could not be loaded"); }
		RaiseState();
	}

	public async Task OpenRequisitionAsync(long id, CancellationToken cancellationToken = default)
	{
		if (!_sourcing.CanViewRequisitions) return;
		try
		{
			var value = await _sourcing.GetRequisitionAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase requisition was not found.");
			_selectedRequisition = value;
			OnPropertyChanged(nameof(SelectedRequisition));
			ApplyRequisition(value);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception ex) { FailOperation(ex, "Purchase requisition could not be opened"); }
	}

	public async Task OpenRfqAsync(long id, CancellationToken cancellationToken = default)
	{
		if (!_sourcing.CanViewSourcing) return;
		try
		{
			var value = await _sourcing.GetRfqAsync(id, cancellationToken) ?? throw new InvalidOperationException("RFQ was not found.");
			_selectedRfq = value;
			OnPropertyChanged(nameof(SelectedRfq));
			Replace(RfqRecipientOptions, value.Suppliers);
			SelectedQuoteSupplier = RfqRecipientOptions.FirstOrDefault();
			QuoteLines.Clear();
			foreach (var line in value.Lines.OrderBy(line => line.Id)) QuoteLines.Add(new QuoteLineDraftViewModel(line));
			Replace(QuoteResponses, await _sourcing.ListQuoteResponsesAsync(id, cancellationToken));
			SelectedQuoteResponse = value.SelectedQuoteResponseId is { } selectedId ? QuoteResponses.FirstOrDefault(response => response.Id == selectedId) : QuoteResponses.FirstOrDefault();
			await RefreshComparisonAsync(cancellationToken);
			RaiseState();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception ex) { FailOperation(ex, "RFQ could not be opened"); }
	}

	private void NewRequisition()
	{
		_selectedRequisition = null;
		OnPropertyChanged(nameof(SelectedRequisition));
		ApplyRequisition(NewRequisitionDraft());
		LastConvertedPurchaseOrderId = null;
		RequestEditorFocus();
	}

	private void ApplyRequisition(PurchaseRequisition value)
	{
		RequisitionDraft = value;
		Replace(RequisitionLines, value.Lines);
		SelectedRequisitionLine = null;
		DecisionComment = value.ApprovalComment;
		RefreshSupplierSelections();
		RaiseState();
	}

	private void AddLine()
	{
		if (SelectedItem is null || LineQuantity <= 0) return;
		if (RequisitionLines.Any(line => line.ItemId == SelectedItem.Id))
		{
			FailOperation(new InvalidOperationException("The selected item is already present in this requisition."), "Line could not be added");
			return;
		}
		RequisitionLines.Add(new PurchaseRequisitionLine { LineNumber = RequisitionLines.Count + 1, ItemId = SelectedItem.Id, ItemPartNumber = SelectedItem.PartNumber, ItemDescription = SelectedItem.Description, Quantity = LineQuantity, Notes = LineNotes });
		SelectedItem = null;
		LineQuantity = 1;
		LineNotes = null;
		RaiseState();
	}

	private void RemoveLine()
	{
		if (SelectedRequisitionLine is null) return;
		RequisitionLines.Remove(SelectedRequisitionLine);
		for (var index = 0; index < RequisitionLines.Count; index++) RequisitionLines[index].LineNumber = index + 1;
		SelectedRequisitionLine = null;
		RaiseState();
	}

	private async Task SaveRequisitionAsync(CancellationToken cancellationToken)
	{
		await MutateAsync(async token =>
		{
			RequisitionDraft.Lines = RequisitionLines.ToArray();
			var saved = await _sourcing.SaveRequisitionAsync(RequisitionDraft, token);
			await ReloadListsAndOpenRequisitionAsync(saved.Id, token);
		}, "Saving purchase requisition", cancellationToken);
	}

	private Task SubmitRequisitionAsync(CancellationToken cancellationToken) => RequisitionMutationAsync((id, version, token) => _sourcing.SubmitAsync(id, version, token), "Submitting purchase requisition", cancellationToken);
	private Task ApproveRequisitionAsync(CancellationToken cancellationToken) => RequisitionMutationAsync((id, version, token) => _sourcing.ApproveAsync(id, version, DecisionComment, token), "Approving purchase requisition", cancellationToken);
	private Task ReturnRequisitionAsync(CancellationToken cancellationToken) => RequisitionMutationAsync((id, version, token) => _sourcing.ReturnAsync(id, version, DecisionComment, token), "Returning purchase requisition", cancellationToken);
	private Task RejectRequisitionAsync(CancellationToken cancellationToken) => RequisitionMutationAsync((id, version, token) => _sourcing.RejectAsync(id, version, DecisionComment, token), "Rejecting purchase requisition", cancellationToken);
	private Task CancelRequisitionAsync(CancellationToken cancellationToken) => RequisitionMutationAsync((id, version, token) => _sourcing.CancelRequisitionAsync(id, version, token), "Cancelling purchase requisition", cancellationToken);

	private async Task RequisitionMutationAsync(Func<long, long, CancellationToken, Task<PurchaseRequisition>> action, string status, CancellationToken cancellationToken)
	{
		await MutateAsync(async token =>
		{
			var result = await action(RequisitionDraft.Id, RequisitionDraft.Version, token);
			await ReloadListsAndOpenRequisitionAsync(result.Id, token);
		}, status, cancellationToken);
	}

	private async Task CreateRfqAsync(CancellationToken cancellationToken)
	{
		await MutateAsync(async token =>
		{
			var suppliers = RfqSupplierOptions.Where(option => option.IsSelected).Select(option => option.Supplier.Id).ToArray();
			var rfq = await _sourcing.CreateRfqAsync(RequisitionDraft.Id, suppliers, ResponseDueDate, token);
			await ReloadListsAsync(token);
			await OpenRfqAsync(rfq.Id, token);
		}, "Creating request for quotation", cancellationToken);
	}

	private async Task CancelRfqAsync(CancellationToken cancellationToken)
	{
		if (SelectedRfq is null) return;
		var id = SelectedRfq.Id;
		await MutateAsync(async token =>
		{
			await _sourcing.CancelRfqAsync(id, SelectedRfq.Version, token);
			await ReloadListsAsync(token);
			await OpenRfqAsync(id, token);
		}, "Cancelling request for quotation", cancellationToken);
	}

	private async Task CaptureQuoteAsync(CancellationToken cancellationToken)
	{
		if (SelectedRfq is null || SelectedQuoteSupplier is null) return;
		var rfqId = SelectedRfq.Id;
		await MutateAsync(async token =>
		{
			var response = new SupplierQuoteResponse
			{
				RequestForQuotationId = rfqId,
				SupplierId = SelectedQuoteSupplier.SupplierId,
				SupplierReference = SupplierReference,
				Currency = QuoteCurrency,
				ValidUntil = QuoteValidUntil,
				Lines = QuoteLines.Select(line => line.ToModel()).ToArray()
			};
			await _sourcing.CaptureQuoteResponseAsync(response, token);
			SupplierReference = null;
			QuoteValidUntil = null;
			await OpenRfqAsync(rfqId, token);
		}, "Capturing supplier quote", cancellationToken);
	}

	private async Task RefreshComparisonAsync(CancellationToken cancellationToken)
	{
		if (SelectedRfq is null) { ComparisonRows.Clear(); return; }
		try { Replace(ComparisonRows, await _sourcing.CompareAsync(SelectedRfq.Id, cancellationToken)); }
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception ex) { FailOperation(ex, "Quote comparison could not be loaded"); }
	}

	private async Task SelectQuoteAsync(CancellationToken cancellationToken)
	{
		if (SelectedRfq is null || SelectedQuoteResponse is null) return;
		var id = SelectedRfq.Id;
		await MutateAsync(async token =>
		{
			await _sourcing.SelectQuoteAsync(id, SelectedRfq.Version, SelectedQuoteResponse.Id, token);
			await ReloadListsAsync(token);
			await OpenRfqAsync(id, token);
		}, "Selecting supplier quote", cancellationToken);
	}

	private async Task ConvertToPurchaseOrderAsync(CancellationToken cancellationToken)
	{
		if (SelectedRfq is null) return;
		var id = SelectedRfq.Id;
		await MutateAsync(async token =>
		{
			LastConvertedPurchaseOrderId = await _sourcing.ConvertSelectedQuoteToPurchaseOrderAsync(id, token);
			await ReloadListsAsync(token);
			await OpenRfqAsync(id, token);
		}, "Creating purchase order draft", cancellationToken);
	}

	private async Task ReloadListsAndOpenRequisitionAsync(long id, CancellationToken cancellationToken)
	{
		await ReloadListsAsync(cancellationToken);
		await OpenRequisitionAsync(id, cancellationToken);
	}

	private async Task ReloadListsAsync(CancellationToken cancellationToken)
	{
		if (_sourcing.CanViewRequisitions) Replace(Requisitions, (await _sourcing.SearchRequisitionsAsync(SearchText, null, 1, PageSize, cancellationToken)).Items);
		if (_sourcing.CanViewSourcing) Replace(Rfqs, (await _sourcing.SearchRfqsAsync(null, 1, PageSize, cancellationToken)).Items);
		_markPurchasingStale();
	}

	private async Task MutateAsync(Func<CancellationToken, Task> operation, string status, CancellationToken cancellationToken)
	{
		BeginOperation(status);
		try
		{
			await operation(cancellationToken);
			CompleteOperation(false, status.Replace("ing ", "ed ", StringComparison.OrdinalIgnoreCase));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception ex) { FailOperation(ex, $"{status} failed"); }
		RaiseState();
	}

	private void RefreshSupplierSelections()
	{
		var selected = RfqSupplierOptions.Where(option => option.IsSelected).Select(option => option.Supplier.Id).ToHashSet();
		RfqSupplierOptions.Clear();
		foreach (var supplier in Suppliers.OrderBy(value => value.Name))
		{
			var option = new SupplierSelectionViewModel(supplier, () => RaiseState())
			{
				IsSelected = selected.Contains(supplier.Id) || RequisitionDraft.PreferredSupplierId == supplier.Id
			};
			RfqSupplierOptions.Add(option);
		}
		RaiseState();
	}

	private void ClearRfqDetail()
	{
		RfqRecipientOptions.Clear();
		QuoteResponses.Clear();
		QuoteLines.Clear();
		ComparisonRows.Clear();
		SelectedQuoteSupplier = null;
		SelectedQuoteResponse = null;
		SelectedComparisonRow = null;
		RaiseState();
	}

	private void RaiseState()
	{
		OnPropertyChanged(nameof(CanEditRequisition));
		OnPropertyChanged(nameof(CanSubmitRequisition));
		OnPropertyChanged(nameof(CanApproveRequisition));
		OnPropertyChanged(nameof(CanCancelRequisition));
		OnPropertyChanged(nameof(CanCreateRfq));
		OnPropertyChanged(nameof(CanCancelRfq));
		OnPropertyChanged(nameof(CanCaptureQuote));
		OnPropertyChanged(nameof(CanSelectQuote));
		OnPropertyChanged(nameof(CanConvertToPurchaseOrder));
		SaveRequisitionCommand.RaiseCanExecuteChanged();
		SubmitRequisitionCommand.RaiseCanExecuteChanged();
		ApproveRequisitionCommand.RaiseCanExecuteChanged();
		ReturnRequisitionCommand.RaiseCanExecuteChanged();
		RejectRequisitionCommand.RaiseCanExecuteChanged();
		CancelRequisitionCommand.RaiseCanExecuteChanged();
		AddLineCommand.RaiseCanExecuteChanged();
		RemoveLineCommand.RaiseCanExecuteChanged();
		CreateRfqCommand.RaiseCanExecuteChanged();
		CancelRfqCommand.RaiseCanExecuteChanged();
		CaptureQuoteCommand.RaiseCanExecuteChanged();
		RefreshComparisonCommand.RaiseCanExecuteChanged();
		SelectQuoteCommand.RaiseCanExecuteChanged();
		ConvertToPurchaseOrderCommand.RaiseCanExecuteChanged();
	}

	private static PurchaseRequisition NewRequisitionDraft() => new() { Status = PurchaseRequisitionStatus.Draft, RequiredByDate = DateTime.Today.AddDays(7) };

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
	{
		target.Clear();
		foreach (var value in source) target.Add(value);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		RefreshCommand.Dispose();
		SaveRequisitionCommand.Dispose();
		SubmitRequisitionCommand.Dispose();
		ApproveRequisitionCommand.Dispose();
		ReturnRequisitionCommand.Dispose();
		RejectRequisitionCommand.Dispose();
		CancelRequisitionCommand.Dispose();
		CreateRfqCommand.Dispose();
		CancelRfqCommand.Dispose();
		CaptureQuoteCommand.Dispose();
		RefreshComparisonCommand.Dispose();
		SelectQuoteCommand.Dispose();
		ConvertToPurchaseOrderCommand.Dispose();
	}
}

public sealed class SupplierSelectionViewModel : BaseViewModel
{
	private readonly Action _changed;
	private bool _isSelected;

	public SupplierSelectionViewModel(Supplier supplier, Action changed)
	{
		Supplier = supplier;
		_changed = changed;
	}

	public Supplier Supplier { get; }
	public string Name => Supplier.Name;
	public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); _changed(); } }
}

public sealed class QuoteLineDraftViewModel : BaseViewModel
{
	private decimal _unitPrice;
	private int? _minimumOrderQuantity;
	private int? _leadTimeDays;

	public QuoteLineDraftViewModel(RequestForQuotationLine source) => Source = source;

	public RequestForQuotationLine Source { get; }
	public string PartNumber => Source.ItemPartNumber;
	public string Description => Source.ItemDescription;
	public int Quantity => Source.Quantity;
	public decimal UnitPrice { get => _unitPrice; set { if (_unitPrice == value) return; _unitPrice = value; OnPropertyChanged(); } }
	public int? MinimumOrderQuantity { get => _minimumOrderQuantity; set { if (_minimumOrderQuantity == value) return; _minimumOrderQuantity = value; OnPropertyChanged(); } }
	public int? LeadTimeDays { get => _leadTimeDays; set { if (_leadTimeDays == value) return; _leadTimeDays = value; OnPropertyChanged(); } }

	public SupplierQuoteResponseLine ToModel() => new()
	{
		RequestForQuotationLineId = Source.Id,
		ItemId = Source.ItemId,
		Quantity = Source.Quantity,
		UnitPrice = UnitPrice,
		MinimumOrderQuantity = MinimumOrderQuantity,
		LeadTimeDays = LeadTimeDays
	};
}
