// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ProcurementViewModel : BaseViewModel, IDisposable
{
	private ProcurementSection _section;
	private const int PageSize = 50;
	private readonly PurchaseOrderService _orders;
	private readonly PurchaseOrderHistoryService _history;
	private readonly GoodsReceiptService _receipts;
	private readonly SupplierService _suppliers;
	private readonly ItemService _items;
	private readonly IFileDialogService _fileDialogs;
	private readonly ReasonCodeService _reasonCodes;
	private readonly Action? _purchaseOrderChanged;
	private readonly Action? _inventoryChanged;
	private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _supplierSearch = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _itemSearch = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _orderRequest = new();
	private readonly LatestRequest _selectionRequest = new();
	private readonly LatestRequest _supplierOptionRequest = new();
	private readonly LatestRequest _itemOptionRequest = new();
	private readonly LatestRequest _receiptMovementRequest = new();
	private PurchaseOrder? _selectedOrder;
	private PurchaseOrder _draft = NewOrderDraft();
	private PurchaseOrderLine? _selectedLine;
	private Item? _selectedItem;
	private string _searchText = string.Empty;
	private PurchaseOrderStatusFilter _selectedStatusFilter;
	private int _lineQuantity = 1;
	private decimal _lineUnitPrice;
	private string _supplierDeliveryNoteNumber = string.Empty;
	private DateTime _receiptDate = DateTime.Today;
	private string? _receiptNotes;
	private GoodsReceipt? _selectedReceipt;
	private ReasonCode? _selectedReversalReasonCode;
	private string _reversalReason = string.Empty;
	private string _supplierSearchText = string.Empty;
	private string _itemSearchText = string.Empty;
	private string _closeReason = string.Empty;
	private int _pageNumber = 1;
	private long _totalCount;
	private bool _isLoadingOrderDetails;

	public ProcurementViewModel(
		PurchaseOrderService orders,
		PurchaseOrderHistoryService history,
		GoodsReceiptService receipts,
		SupplierService suppliers,
		ItemService items,
		IFileDialogService fileDialogs,
		ReasonCodeService reasonCodes,
		Action? purchaseOrderChanged = null,
		Action? inventoryChanged = null)
	{
		_orders = orders; _history = history; _receipts = receipts; _suppliers = suppliers; _items = items; _fileDialogs = fileDialogs; _reasonCodes = reasonCodes;
		_purchaseOrderChanged = purchaseOrderChanged;
		_inventoryChanged = inventoryChanged;
		StatusFilters = [new("All statuses", null), .. Enum.GetValues<PurchaseOrderStatus>().Select(status => new PurchaseOrderStatusFilter(StatusLabel(status), status))];
		_selectedStatusFilter = StatusFilters[0];
		NewOrderCommand = new RelayCommand(NewOrder, () => CanCreateOrders);
		SaveOrderCommand = new AsyncRelayCommand(SaveOrderAsync, () => IsDraft);
		SubmitForApprovalCommand = new AsyncRelayCommand(SubmitForApprovalAsync, () => CanSubmitOrders && Draft.Id > 0 && Draft.Status == PurchaseOrderStatus.Draft);
		ReopenRejectedCommand = new AsyncRelayCommand(ReopenRejectedAsync, () => CanEditOrders && Draft.Status == PurchaseOrderStatus.Rejected);
		PlaceOrderCommand = new AsyncRelayCommand(PlaceOrderAsync, () => CanOrderPurchaseOrders && Draft.Status == PurchaseOrderStatus.Approved);
		CloseOrderCommand = new AsyncRelayCommand(CloseOrderAsync, () => CanClose && !string.IsNullOrWhiteSpace(CloseReason));
		CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync, () => CanEditOrders && Draft.Status is (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered));
		AddLineCommand = new RelayCommand(AddOrUpdateLine, () => IsDraft && SelectedItem is not null);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null);
		PostReceiptCommand = new AsyncRelayCommand(PostReceiptAsync, () => CanReceive);
		ReverseReceiptCommand = new AsyncRelayCommand(ReverseReceiptAsync, () => CanReverseReceipt && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason));
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<PurchaseOrder> Orders { get; } = new();
	public ObservableCollection<Supplier> Suppliers { get; } = new();
	public ObservableCollection<Item> Items { get; } = new();
	public ObservableCollection<PurchaseOrderLine> Lines { get; } = new();
	public ObservableCollection<PurchaseOrderHistoryItem> StatusHistory { get; } = new();
	public ObservableCollection<GoodsReceiptLineEditor> ReceiptLines { get; } = new();
	public ObservableCollection<GoodsReceipt> GoodsReceipts { get; } = new();
	public ObservableCollection<MovementOverviewItem> ReceiptMovements { get; } = new();
	public ObservableCollection<ReasonCode> ReversalReasonCodes { get; } = new();
	public IReadOnlyList<PurchaseOrderStatusFilter> StatusFilters { get; }
	public RelayCommand NewOrderCommand { get; }
	public AsyncRelayCommand SaveOrderCommand { get; }
	public AsyncRelayCommand PlaceOrderCommand { get; }
	public AsyncRelayCommand SubmitForApprovalCommand { get; }
	public AsyncRelayCommand ReopenRejectedCommand { get; }
	public AsyncRelayCommand CloseOrderCommand { get; }
	public AsyncRelayCommand CancelOrderCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand PostReceiptCommand { get; }
	public AsyncRelayCommand ReverseReceiptCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public PurchaseOrder Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsOrderReadOnly)); OnPropertyChanged(nameof(CanClose)); OnPropertyChanged(nameof(HasApprovalHistory)); RaiseCommands(); } }
	public bool CanCreateOrders => _orders.CanCurrentUserCreate;
	public bool CanEditOrders => _orders.CanCurrentUserEdit;
	public bool CanSubmitOrders => _orders.CanCurrentUserSubmit;
	public bool CanOrderPurchaseOrders => _orders.CanCurrentUserOrder;
	public bool CanCloseOrders => _orders.CanCurrentUserClose;
	public bool IsDraft => Draft.Status == PurchaseOrderStatus.Draft && (Draft.Id == 0 ? CanCreateOrders : CanEditOrders);
	public bool IsOrderReadOnly => !IsDraft;
	public bool CanClose => CanCloseOrders && Draft.Status is (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived);
	public bool CanSubmitCurrent => CanSubmitOrders && Draft.Id > 0 && Draft.Status == PurchaseOrderStatus.Draft;
	public bool CanReopenCurrent => CanEditOrders && Draft.Status == PurchaseOrderStatus.Rejected;
	public bool CanPlaceCurrent => CanOrderPurchaseOrders && Draft.Status == PurchaseOrderStatus.Approved;
	public bool CanCancelCurrent => CanEditOrders && Draft.Id > 0 && Draft.Status is (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered);
	public bool CanReceive => _receipts.CanPost && SelectedOrder?.Status is (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived);
	public string EditorTitle => Draft.Id == 0 ? "New Purchase Order" : Draft.OrderNumber;
	public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); PageNumber = 1; _ = _search.DebounceAsync(LoadOrdersAsync); } }
	public string SupplierSearchText { get => _supplierSearchText; set { if (_supplierSearchText == value) return; _supplierSearchText = value; OnPropertyChanged(); _ = _supplierSearch.DebounceAsync(LoadSupplierOptionsAsync); } }
	public string ItemSearchText { get => _itemSearchText; set { if (_itemSearchText == value) return; _itemSearchText = value; OnPropertyChanged(); _ = _itemSearch.DebounceAsync(LoadItemOptionsAsync); } }
	public PurchaseOrderStatusFilter SelectedStatusFilter { get => _selectedStatusFilter; set { if (_selectedStatusFilter == value) return; _selectedStatusFilter = value; OnPropertyChanged(); PageNumber = 1; _ = LoadOrdersAsync(); } }
	public int PageNumber { get => _pageNumber; private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaisePagingCommands(); } }
	public long TotalCount { get => _totalCount; private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaisePagingCommands(); } }
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} orders";
	public bool HasSelectedOrder => SelectedOrder is not null;
	public bool HasNoSelectedOrder => !HasSelectedOrder;
	public bool IsLoadingOrderDetails { get => _isLoadingOrderDetails; private set { if (_isLoadingOrderDetails == value) return; _isLoadingOrderDetails = value; OnPropertyChanged(); } }

	public PurchaseOrder? SelectedOrder
	{
		get => _selectedOrder;
		set
		{
			if (_selectedOrder == value) return;
			_selectedOrder = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasSelectedOrder));
			OnPropertyChanged(nameof(HasNoSelectedOrder));
			OnPropertyChanged(nameof(CanReceive));
			OnPropertyChanged(nameof(ShowReceiptEntry));
			_ = SelectOrderAsync(value);
		}
	}
	public PurchaseOrderLine? SelectedLine
	{
		get => _selectedLine;
		set
		{
			if (_selectedLine == value) return; _selectedLine = value; OnPropertyChanged();
			SelectedItem = value is null ? null : Items.FirstOrDefault(item => item.Id == value.ItemId);
			LineQuantity = value?.Quantity ?? 1; LineUnitPrice = value?.UnitPrice ?? 0; OnPropertyChanged(nameof(SaveLineText)); RaiseCommands();
		}
	}
	public Item? SelectedItem { get => _selectedItem; set { if (_selectedItem == value) return; _selectedItem = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public int LineQuantity { get => _lineQuantity; set { if (_lineQuantity == value) return; _lineQuantity = value; OnPropertyChanged(); } }
	public decimal LineUnitPrice { get => _lineUnitPrice; set { if (_lineUnitPrice == value) return; _lineUnitPrice = value; OnPropertyChanged(); } }
	public string SupplierDeliveryNoteNumber { get => _supplierDeliveryNoteNumber; set { if (_supplierDeliveryNoteNumber == value) return; _supplierDeliveryNoteNumber = value; OnPropertyChanged(); } }
	public DateTime ReceiptDate { get => _receiptDate; set { if (_receiptDate == value) return; _receiptDate = value; OnPropertyChanged(); } }
	public string? ReceiptNotes { get => _receiptNotes; set { if (_receiptNotes == value) return; _receiptNotes = value; OnPropertyChanged(); } }
	public GoodsReceipt? SelectedReceipt { get => _selectedReceipt; set { if (_selectedReceipt == value) return; _selectedReceipt = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanReverseReceipt)); ReverseReceiptCommand.RaiseCanExecuteChanged(); _ = LoadReceiptMovementsAsync(value); } }
	public bool CanReverseReceipt => _receipts.CanReverse && SelectedReceipt is { IsReversed: false };
	public ProcurementSection Section
	{
		get => _section;
		set
		{
			if (_section == value) return;
			_section = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsPurchaseOrdersSection));
			OnPropertyChanged(nameof(IsGoodsReceiptsSection));
			OnPropertyChanged(nameof(ShowReceiptEntry));
			OnPropertyChanged(nameof(ShowNewOrderButton));
		}
	}
	public bool IsPurchaseOrdersSection => Section == ProcurementSection.PurchaseOrders;
	public bool IsGoodsReceiptsSection => Section == ProcurementSection.GoodsReceipts;
	public bool ShowReceiptEntry => IsGoodsReceiptsSection && CanReceive;
	public bool ShowNewOrderButton => IsPurchaseOrdersSection && CanCreateOrders;
	public ReasonCode? SelectedReversalReasonCode { get => _selectedReversalReasonCode; set { if (_selectedReversalReasonCode == value) return; _selectedReversalReasonCode = value; OnPropertyChanged(); ReverseReceiptCommand.RaiseCanExecuteChanged(); } }
	public string ReversalReason { get => _reversalReason; set { if (_reversalReason == value) return; _reversalReason = value; OnPropertyChanged(); ReverseReceiptCommand.RaiseCanExecuteChanged(); } }
	public string CloseReason { get => _closeReason; set { if (_closeReason == value) return; _closeReason = value; OnPropertyChanged(); CloseOrderCommand.RaiseCanExecuteChanged(); } }
	public bool HasApprovalHistory => Draft.SubmittedAtUtc is not null;
	public bool HasStatusHistory => StatusHistory.Count > 0;
	public bool HasNoStatusHistory => StatusHistory.Count == 0;

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _orderRequest.Begin(cancellationToken);
		BeginOperation("Loading purchase orders");
		try
		{
			var suppliersTask = LoadSupplierOptionsAsync(request.Token);
			var itemsTask = LoadItemOptionsAsync(request.Token);
			var ordersTask = IsGoodsReceiptsSection
				? _receipts.SearchOpenOrdersAsync(SearchText, PageNumber, PageSize, request.Token)
				: _orders.SearchAsync(SearchText, SelectedStatusFilter.Status, PageNumber, PageSize, request.Token);
			var reasonCodesTask = _reasonCodes.GetActiveAsync(request.Token);
			await Task.WhenAll(suppliersTask, itemsTask, ordersTask, reasonCodesTask);
			if (!request.IsCurrent) return;
			ReversalReasonCodes.Clear(); foreach (var reasonCode in await reasonCodesTask) ReversalReasonCodes.Add(reasonCode);
			ApplyOrderPage(await ordersTask); CompleteOperation(Orders.Count == 0, $"{TotalCount:N0} purchase orders");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase orders could not be loaded"); }
	}

	public async Task OpenOrderAsync(long id, CancellationToken cancellationToken = default)
	{
		var order = await _orders.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException("The referenced purchase order no longer exists.");
		var existing = Orders.FirstOrDefault(candidate => candidate.Id == id);
		if (existing is null)
		{
			Orders.Insert(0, order);
			existing = order;
		}
		SelectedOrder = existing;
	}

	private async Task LoadOrdersAsync(CancellationToken cancellationToken = default)
	{
		var request = _orderRequest.Begin(cancellationToken);
		try
		{
			var page = IsGoodsReceiptsSection
				? await _receipts.SearchOpenOrdersAsync(SearchText, PageNumber, PageSize, request.Token)
				: await _orders.SearchAsync(SearchText, SelectedStatusFilter.Status, PageNumber, PageSize, request.Token);
			if (!request.IsCurrent) return;
			ApplyOrderPage(page);
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase orders could not be loaded"); }
	}

	private async Task LoadSupplierOptionsAsync(CancellationToken cancellationToken = default)
	{
		var request = _supplierOptionRequest.Begin(cancellationToken);
		try { var values = await _suppliers.SearchActiveAsync(SupplierSearchText, 50, request.Token); if (request.IsCurrent) ReplaceOptions(Suppliers, values); }
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier options could not be loaded"); }
	}

	private async Task LoadItemOptionsAsync(CancellationToken cancellationToken = default)
	{
		var request = _itemOptionRequest.Begin(cancellationToken);
		try
		{
			var page = await _items.SearchItemsAsync(ItemSearchText, 1, 50, request.Token);
			if (!request.IsCurrent) return;
			ReplaceOptions(Items, page.Items);
			if (SelectedItem is not null && Items.All(item => item.Id != SelectedItem.Id)) SelectedItem = null;
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Item options could not be loaded"); }
	}

	private async Task SelectOrderAsync(PurchaseOrder? order)
	{
		var request = _selectionRequest.Begin();
		if (order is null) { IsLoadingOrderDetails = false; Draft = NewOrderDraft(); CloseReason = string.Empty; Lines.Clear(); StatusHistory.Clear(); NotifyStatusHistoryChanged(); ReceiptLines.Clear(); GoodsReceipts.Clear(); SelectedReceipt = null; return; }
		IsLoadingOrderDetails = true;
		try
		{
			var details = await _orders.GetByIdAsync(order.Id, request.Token) ?? throw new InvalidOperationException("Purchase order was not found.");
			if (!request.IsCurrent || SelectedOrder?.Id != order.Id) return;
			if (Suppliers.All(supplier => supplier.Id != details.SupplierId))
			{
				var supplier = await _suppliers.GetByIdAsync(details.SupplierId, request.Token);
				if (!request.IsCurrent) return;
				if (supplier is not null) Suppliers.Add(supplier);
			}
			Draft = Copy(details); CloseReason = string.Empty; Lines.Clear(); foreach (var line in details.Lines) Lines.Add(Copy(line));
			OnPropertyChanged(nameof(EditorTitle)); OnPropertyChanged(nameof(CanReceive)); OnPropertyChanged(nameof(ShowReceiptEntry)); RaiseCommands();
			await Task.WhenAll(BuildReceiptLinesAsync(details, request.Token, request), LoadReceiptsAsync(details.Id, request.Token, request), LoadStatusHistoryAsync(details.Id, request.Token, request));
			if (!request.IsCurrent) return;
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Purchase order details could not be loaded"); }
		finally { if (request.IsCurrent) IsLoadingOrderDetails = false; }
	}

	private void NewOrder() { SelectedOrder = null; Draft = NewOrderDraft(); Lines.Clear(); StatusHistory.Clear(); NotifyStatusHistoryChanged(); ReceiptLines.Clear(); OnPropertyChanged(nameof(EditorTitle)); }
	private void AddOrUpdateLine()
	{
		if (SelectedItem is null || LineQuantity <= 0 || LineUnitPrice < 0) return;
		var duplicate = Lines.FirstOrDefault(line => line.ItemId == SelectedItem.Id && line != SelectedLine);
		if (duplicate is not null) { FailOperation(new InvalidOperationException("The item is already included in this order."), "Line could not be added"); return; }
		var line = SelectedLine ?? new PurchaseOrderLine();
		line.ItemId = SelectedItem.Id; line.ItemPartNumber = SelectedItem.PartNumber; line.ItemDescription = SelectedItem.Description; line.Quantity = LineQuantity; line.UnitPrice = LineUnitPrice;
		if (line.LineNumber == 0) line.LineNumber = Lines.Count + 1;
		if (SelectedLine is null) Lines.Add(line);
		SelectedLine = null;
	}
	private void RemoveLine() { if (SelectedLine is null) return; Lines.Remove(SelectedLine); SelectedLine = null; }

	private async Task SaveOrderAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving purchase order");
		try { Draft.Lines = Lines.Select(Copy).ToArray(); var saved = await _orders.SaveDraftAsync(Copy(Draft), cancellationToken); ApplyChangedOrder(saved); await LoadStatusHistoryAsync(saved.Id, cancellationToken); _purchaseOrderChanged?.Invoke(); CompleteOperation(false, "Purchase order saved"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase order could not be saved"); }
	}
	private async Task PlaceOrderAsync(CancellationToken cancellationToken) { var operationId = Guid.NewGuid(); await ChangeStatusAsync(() => _orders.PlaceOrderAsync(Draft.Id, Draft.Version, operationId, cancellationToken), "Purchase order placed", PurchaseOrderStatus.Ordered, cancellationToken); }
	private async Task SubmitForApprovalAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.SubmitForApprovalAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order submitted for approval", cancellationToken);
	private async Task ReopenRejectedAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.ReopenRejectedAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order reopened as draft", cancellationToken);
	private async Task CloseOrderAsync(CancellationToken cancellationToken)
	{
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Close Purchase Order", $"Close purchase order {Draft.OrderNumber}? Open quantities will remain open, but no further goods receipts will be accepted.\n\nReason: {CloseReason.Trim()}", true))) return;
		var operationId = Guid.NewGuid();
		await ChangeStatusAsync(() => _orders.CloseOrderAsync(Draft.Id, Draft.Version, CloseReason, operationId, cancellationToken), "Purchase order closed", PurchaseOrderStatus.Closed, cancellationToken);
	}
	private async Task CancelOrderAsync(CancellationToken cancellationToken)
	{
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Cancel Purchase Order", $"Cancel purchase order {Draft.OrderNumber}?", true))) return;
		await ChangeStatusAsync(() => _orders.CancelAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order cancelled", cancellationToken);
	}
	private async Task ChangeStatusAsync(Func<Task<PurchaseOrder>> action, string message, CancellationToken cancellationToken)
		=> await ChangeStatusAsync(action, message, null, cancellationToken);

	private async Task ChangeStatusAsync(Func<Task<PurchaseOrder>> action, string message, PurchaseOrderStatus? expectedStatus, CancellationToken cancellationToken)
	{
		BeginOperation("Updating purchase order status");
		try { var saved = await action(); ApplyChangedOrder(saved); await Task.WhenAll(BuildReceiptLinesAsync(saved, cancellationToken), LoadReceiptsAsync(saved.Id, cancellationToken), LoadStatusHistoryAsync(saved.Id, cancellationToken)); CloseReason = string.Empty; _purchaseOrderChanged?.Invoke(); CompleteOperation(false, message); }
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			if (expectedStatus is not null && await ReconcileStatusAsync(Draft.Id, expectedStatus.Value, message)) return;
			FailOperation(exception, "Purchase order status could not be updated");
		}
	}

	private async Task<bool> ReconcileStatusAsync(long id, PurchaseOrderStatus expectedStatus, string successMessage)
	{
		try
		{
			var current = await _orders.GetByIdAsync(id, CancellationToken.None);
			if (current?.Status != expectedStatus) return false;
			ApplyChangedOrder(current);
			await Task.WhenAll(BuildReceiptLinesAsync(current, CancellationToken.None), LoadReceiptsAsync(current.Id, CancellationToken.None), LoadStatusHistoryAsync(current.Id, CancellationToken.None));
			CompleteOperation(false, $"{successMessage}; current server status confirmed");
			return true;
		}
		catch { return false; }
	}

	private async Task BuildReceiptLinesAsync(PurchaseOrder order, CancellationToken cancellationToken = default, LatestRequestLease? request = null)
	{
		if (request is { IsCurrent: false }) return;
		if (order.Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
		{
			ReceiptLines.Clear();
			SupplierDeliveryNoteNumber = string.Empty; ReceiptDate = DateTime.Today; ReceiptNotes = null;
			return;
		}
		var openLines = order.Lines.Where(line => line.OpenQuantity > 0).ToArray();
		var optionsByItem = (await _receipts.GetInventoryOptionsAsync(openLines.Select(line => line.ItemId), cancellationToken))
			.GroupBy(option => option.ItemId)
			.ToDictionary(group => group.Key, group => group.ToArray());
		if (request is { IsCurrent: false }) return;
		ReceiptLines.Clear(); SupplierDeliveryNoteNumber = string.Empty; ReceiptDate = DateTime.Today; ReceiptNotes = null;
		foreach (var line in openLines)
		{
			var editor = new GoodsReceiptLineEditor(line);
			foreach (var option in optionsByItem.GetValueOrDefault(line.ItemId) ?? []) editor.InventoryOptions.Add(option);
			editor.SelectedInventory = editor.InventoryOptions.FirstOrDefault(); ReceiptLines.Add(editor);
		}
	}
	private async Task PostReceiptAsync(CancellationToken cancellationToken)
	{
		if (SelectedOrder is null) return;
		BeginOperation("Posting goods receipt");
		try
		{
			var lines = ReceiptLines.Where(line => line.Quantity > 0).Select(line => new GoodsReceiptLine { PurchaseOrderLineId = line.PurchaseOrderLineId, InventoryId = line.SelectedInventory?.InventoryId ?? 0, Quantity = line.Quantity }).ToArray();
			var receipt = new GoodsReceipt { PurchaseOrderId = SelectedOrder.Id, ReceiptDate = ReceiptDate, SupplierDeliveryNoteNumber = SupplierDeliveryNoteNumber, Notes = ReceiptNotes, Lines = lines };
			await _receipts.PostGoodsReceiptAsync(receipt, cancellationToken); await RefreshOrderAsync(receipt.PurchaseOrderId, cancellationToken); _purchaseOrderChanged?.Invoke(); _inventoryChanged?.Invoke(); CompleteOperation(false, $"Goods receipt {receipt.ReceiptNumber} posted");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Goods receipt could not be posted"); }
	}
	private async Task LoadReceiptsAsync(long purchaseOrderId, CancellationToken cancellationToken = default, LatestRequestLease? request = null)
	{
		var receipts = await _receipts.ListByPurchaseOrderAsync(purchaseOrderId, cancellationToken);
		if (request is { IsCurrent: false }) return;
		GoodsReceipts.Clear(); foreach (var receipt in receipts) GoodsReceipts.Add(receipt);
		SelectedReceipt = GoodsReceipts.FirstOrDefault();
	}
	private async Task LoadStatusHistoryAsync(long purchaseOrderId, CancellationToken cancellationToken = default, LatestRequestLease? request = null)
	{
		var history = await _history.GetHistoryAsync(purchaseOrderId, cancellationToken);
		if (request is { IsCurrent: false }) return;
		ReplaceOptions(StatusHistory, history);
		NotifyStatusHistoryChanged();
	}
	private void NotifyStatusHistoryChanged()
	{
		OnPropertyChanged(nameof(HasStatusHistory));
		OnPropertyChanged(nameof(HasNoStatusHistory));
	}
	private async Task LoadReceiptMovementsAsync(GoodsReceipt? receipt, CancellationToken cancellationToken = default)
	{
		var request = _receiptMovementRequest.Begin(cancellationToken);
		try
		{
			var movements = receipt is null
				? Array.Empty<MovementOverviewItem>()
				: await _receipts.ListMovementsAsync(receipt.ReceiptNumber, request.Token);
			if (!request.IsCurrent || SelectedReceipt?.Id != receipt?.Id) return;
			ReplaceOptions(ReceiptMovements, movements);
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Goods receipt movements could not be loaded");
		}
	}
	private async Task ReverseReceiptAsync(CancellationToken cancellationToken)
	{
		if (SelectedReceipt is null || SelectedReversalReasonCode is null) return;
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Reverse Goods Receipt", $"Reverse {SelectedReceipt.ReceiptNumber} and reduce the purchase-order received quantities?", true))) return;
		BeginOperation("Reversing goods receipt");
		try
		{
			var purchaseOrderId = SelectedReceipt.PurchaseOrderId;
			var reversed = await _receipts.ReverseAsync(SelectedReceipt.Id, SelectedReceipt.Version, SelectedReversalReasonCode.Id, ReversalReason, cancellationToken);
			var receiptIndex = GoodsReceipts.IndexOf(SelectedReceipt);
			if (receiptIndex >= 0) GoodsReceipts[receiptIndex] = reversed;
			SelectedReceipt = reversed;

			var updatedOrder = await _orders.GetByIdAsync(purchaseOrderId, cancellationToken)
				?? throw new InvalidOperationException("The purchase order was not found after reversing its goods receipt.");
			var existingOrder = Orders.FirstOrDefault(order => order.Id == purchaseOrderId);
			var orderIndex = existingOrder is null ? -1 : Orders.IndexOf(existingOrder);
			if (orderIndex >= 0) Orders[orderIndex] = updatedOrder;
			_selectedOrder = updatedOrder;
			OnPropertyChanged(nameof(SelectedOrder));
			Draft = Copy(updatedOrder);
			Lines.Clear();
			foreach (var line in updatedOrder.Lines) Lines.Add(Copy(line));
			await Task.WhenAll(BuildReceiptLinesAsync(updatedOrder, cancellationToken), LoadStatusHistoryAsync(updatedOrder.Id, cancellationToken));
			OnPropertyChanged(nameof(EditorTitle));
			OnPropertyChanged(nameof(CanReceive));
			OnPropertyChanged(nameof(ShowReceiptEntry));
			RaiseCommands();
			_purchaseOrderChanged?.Invoke();
			_inventoryChanged?.Invoke();
			CompleteOperation(false, "Goods receipt reversed");
		}
		catch (ConcurrencyConflictException)
		{
			FailOperation(new InvalidOperationException("The goods receipt was changed or reversed by another user."), "Goods receipt could not be reversed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Goods receipt could not be reversed"); }
	}

	private void ReplaceOrders(IReadOnlyList<PurchaseOrder> values) { var id = SelectedOrder?.Id; CollectionSynchronizer.Replace(Orders, values); var selected = Orders.FirstOrDefault(value => value.Id == id); _selectedOrder = null; OnPropertyChanged(nameof(SelectedOrder)); if (selected is not null) SelectedOrder = selected; }
	private void ApplyOrderPage(PageResult<PurchaseOrder> page) { ReplaceOrders(page.Items); TotalCount = page.TotalCount; PageNumber = page.PageNumber; }
	private async Task RefreshOrderAsync(long id, CancellationToken cancellationToken)
	{
		var order = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The purchase order was not found after the operation.");
		ApplyChangedOrder(order);
		await Task.WhenAll(BuildReceiptLinesAsync(order, cancellationToken), LoadReceiptsAsync(order.Id, cancellationToken), LoadStatusHistoryAsync(order.Id, cancellationToken));
	}
	private Task PreviousPageAsync(CancellationToken cancellationToken) { if (PageNumber <= 1) return Task.CompletedTask; PageNumber--; return LoadOrdersAsync(cancellationToken); }
	private Task NextPageAsync(CancellationToken cancellationToken) { if (!HasNextPage) return Task.CompletedTask; PageNumber++; return LoadOrdersAsync(cancellationToken); }
	private void RaisePagingCommands() { PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
	private static void ReplaceOptions<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
	{
		var sharedCount = Math.Min(target.Count, values.Count);
		for (var index = 0; index < sharedCount; index++) target[index] = values[index];
		while (target.Count > values.Count) target.RemoveAt(target.Count - 1);
		for (var index = sharedCount; index < values.Count; index++) target.Add(values[index]);
	}
	private void ApplyChangedOrder(PurchaseOrder order)
	{
		var existing = Orders.FirstOrDefault(value => value.Id == order.Id);
		var matches = MatchesCurrentOrderFilter(order);
		if (existing is not null && matches) Orders[Orders.IndexOf(existing)] = order;
		else if (existing is not null)
		{
			Orders.Remove(existing);
			TotalCount = Math.Max(0, TotalCount - 1);
		}
		else if (matches)
		{
			TotalCount++;
			if (PageNumber == 1)
			{
				Orders.Insert(0, order);
				if (Orders.Count > PageSize) Orders.RemoveAt(Orders.Count - 1);
			}
		}
		_selectedOrder = matches && Orders.Any(value => value.Id == order.Id) ? order : null;
		OnPropertyChanged(nameof(SelectedOrder));
		Draft = Copy(order);
		OnPropertyChanged(nameof(EditorTitle));
		OnPropertyChanged(nameof(CanReceive));
		OnPropertyChanged(nameof(ShowReceiptEntry));
		OnPropertyChanged(nameof(HasApprovalHistory));
		RaiseCommands();
	}
	private bool MatchesCurrentOrderFilter(PurchaseOrder order)
	{
		if (SelectedStatusFilter.Status is not null && order.Status != SelectedStatusFilter.Status) return false;
		var search = SearchText.Trim();
		return search.Length == 0 ||
			order.OrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			order.SupplierName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			(order.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
	}
	private void RaiseCommands() { OnPropertyChanged(nameof(CanSubmitCurrent)); OnPropertyChanged(nameof(CanReopenCurrent)); OnPropertyChanged(nameof(CanPlaceCurrent)); OnPropertyChanged(nameof(CanCancelCurrent)); SaveOrderCommand.RaiseCanExecuteChanged(); SubmitForApprovalCommand.RaiseCanExecuteChanged(); ReopenRejectedCommand.RaiseCanExecuteChanged(); PlaceOrderCommand.RaiseCanExecuteChanged(); CloseOrderCommand.RaiseCanExecuteChanged(); CancelOrderCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); PostReceiptCommand.RaiseCanExecuteChanged(); ReverseReceiptCommand.RaiseCanExecuteChanged(); }
	private static PurchaseOrder NewOrderDraft() => new() { OrderDate = DateTime.Today, ExpectedDeliveryDate = DateTime.Today.AddDays(7) };
	private static PurchaseOrder Copy(PurchaseOrder value) => new() { Id = value.Id, OrderNumber = value.OrderNumber, SupplierId = value.SupplierId, SupplierName = value.SupplierName, OrderDate = value.OrderDate, ExpectedDeliveryDate = value.ExpectedDeliveryDate, Notes = value.Notes, Status = value.Status, CreatedByUserId = value.CreatedByUserId, SubmittedByUserId = value.SubmittedByUserId, SubmittedAtUtc = value.SubmittedAtUtc, ApprovalDecisionByUserId = value.ApprovalDecisionByUserId, ApprovalDecisionAtUtc = value.ApprovalDecisionAtUtc, ApprovalComment = value.ApprovalComment, ClosedByUserId = value.ClosedByUserId, ClosedAtUtc = value.ClosedAtUtc, CloseReason = value.CloseReason, CreatedByUserDisplay = value.CreatedByUserDisplay, SubmittedByUserDisplay = value.SubmittedByUserDisplay, ApprovalDecisionByUserDisplay = value.ApprovalDecisionByUserDisplay, ClosedByUserDisplay = value.ClosedByUserDisplay, Version = value.Version, Lines = value.Lines.Select(Copy).ToArray() };
	private static PurchaseOrderLine Copy(PurchaseOrderLine value) => new() { Id = value.Id, PurchaseOrderId = value.PurchaseOrderId, LineNumber = value.LineNumber, ItemId = value.ItemId, ItemPartNumber = value.ItemPartNumber, ItemDescription = value.ItemDescription, Quantity = value.Quantity, UnitPrice = value.UnitPrice, ReceivedQuantity = value.ReceivedQuantity, Version = value.Version };
	private static string StatusLabel(PurchaseOrderStatus status) => status switch { PurchaseOrderStatus.PartiallyReceived => "Partially Received", PurchaseOrderStatus.PendingApproval => "Pending Approval", _ => status.ToString() };
	public void Dispose() { _orderRequest.Dispose(); _selectionRequest.Dispose(); _supplierOptionRequest.Dispose(); _itemOptionRequest.Dispose(); _receiptMovementRequest.Dispose(); _search.Dispose(); _supplierSearch.Dispose(); _itemSearch.Dispose(); SaveOrderCommand.Dispose(); SubmitForApprovalCommand.Dispose(); ReopenRejectedCommand.Dispose(); PlaceOrderCommand.Dispose(); CloseOrderCommand.Dispose(); CancelOrderCommand.Dispose(); PostReceiptCommand.Dispose(); ReverseReceiptCommand.Dispose(); PreviousPageCommand.Dispose(); NextPageCommand.Dispose(); }
}

public sealed record PurchaseOrderStatusFilter(string Name, PurchaseOrderStatus? Status);

public sealed class GoodsReceiptLineEditor : BaseViewModel
{
	private int _quantity;
	private ReceiptInventoryOption? _selectedInventory;
	public GoodsReceiptLineEditor(PurchaseOrderLine line) { PurchaseOrderLineId = line.Id; ItemPartNumber = line.ItemPartNumber; ItemDescription = line.ItemDescription; OpenQuantity = line.OpenQuantity; _quantity = line.OpenQuantity; }
	public long PurchaseOrderLineId { get; }
	public string ItemPartNumber { get; }
	public string ItemDescription { get; }
	public int OpenQuantity { get; }
	public ObservableCollection<ReceiptInventoryOption> InventoryOptions { get; } = new();
	public int Quantity { get => _quantity; set { if (_quantity == value) return; _quantity = value; OnPropertyChanged(); } }
	public ReceiptInventoryOption? SelectedInventory { get => _selectedInventory; set { if (_selectedInventory == value) return; _selectedInventory = value; OnPropertyChanged(); } }
}
