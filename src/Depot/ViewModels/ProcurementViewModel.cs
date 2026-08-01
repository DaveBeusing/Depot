// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ProcurementViewModel : BaseViewModel, IDisposable
{
	private readonly PurchaseOrderService _orders;
	private readonly GoodsReceiptService _receipts;
	private readonly SupplierService _suppliers;
	private readonly ItemService _items;
	private readonly IFileDialogService _fileDialogs;
	private readonly ReasonCodeService _reasonCodes;
	private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _supplierSearch = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _itemSearch = new(TimeSpan.FromMilliseconds(300));
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
	private string _approvalComment = string.Empty;
	private string _closeReason = string.Empty;

	public ProcurementViewModel(PurchaseOrderService orders, GoodsReceiptService receipts, SupplierService suppliers, ItemService items, IFileDialogService fileDialogs, ReasonCodeService reasonCodes)
	{
		_orders = orders; _receipts = receipts; _suppliers = suppliers; _items = items; _fileDialogs = fileDialogs; _reasonCodes = reasonCodes;
		StatusFilters = [new("All statuses", null), .. Enum.GetValues<PurchaseOrderStatus>().Select(status => new PurchaseOrderStatusFilter(StatusLabel(status), status))];
		_selectedStatusFilter = StatusFilters[0];
		NewOrderCommand = new RelayCommand(NewOrder, () => CanCreateOrders);
		SaveOrderCommand = new AsyncRelayCommand(SaveOrderAsync, () => IsDraft);
		SubmitForApprovalCommand = new AsyncRelayCommand(SubmitForApprovalAsync, () => CanSubmitOrders && Draft.Id > 0 && Draft.Status == PurchaseOrderStatus.Draft);
		ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => Draft.Status == PurchaseOrderStatus.PendingApproval && _orders.CanCurrentUserApprove);
		RejectCommand = new AsyncRelayCommand(RejectAsync, () => Draft.Status == PurchaseOrderStatus.PendingApproval && _orders.CanCurrentUserApprove);
		ReopenRejectedCommand = new AsyncRelayCommand(ReopenRejectedAsync, () => CanEditOrders && Draft.Status == PurchaseOrderStatus.Rejected);
		MarkOrderedCommand = new AsyncRelayCommand(MarkOrderedAsync, () => CanOrderPurchaseOrders && Draft.Status == PurchaseOrderStatus.Approved);
		CloseOrderCommand = new AsyncRelayCommand(CloseOrderAsync, () => CanClose && !string.IsNullOrWhiteSpace(CloseReason));
		CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync, () => CanEditOrders && Draft.Status is (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered));
		AddLineCommand = new RelayCommand(AddOrUpdateLine, () => IsDraft && SelectedItem is not null);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null);
		PostReceiptCommand = new AsyncRelayCommand(PostReceiptAsync, () => CanReceive);
		ReverseReceiptCommand = new AsyncRelayCommand(ReverseReceiptAsync, () => CanReverseReceipt && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason));
	}

	public ObservableCollection<PurchaseOrder> Orders { get; } = new();
	public ObservableCollection<Supplier> Suppliers { get; } = new();
	public ObservableCollection<Item> Items { get; } = new();
	public ObservableCollection<PurchaseOrderLine> Lines { get; } = new();
	public ObservableCollection<GoodsReceiptLineEditor> ReceiptLines { get; } = new();
	public ObservableCollection<GoodsReceipt> GoodsReceipts { get; } = new();
	public ObservableCollection<ReasonCode> ReversalReasonCodes { get; } = new();
	public IReadOnlyList<PurchaseOrderStatusFilter> StatusFilters { get; }
	public RelayCommand NewOrderCommand { get; }
	public AsyncRelayCommand SaveOrderCommand { get; }
	public AsyncRelayCommand MarkOrderedCommand { get; }
	public AsyncRelayCommand SubmitForApprovalCommand { get; }
	public AsyncRelayCommand ApproveCommand { get; }
	public AsyncRelayCommand RejectCommand { get; }
	public AsyncRelayCommand ReopenRejectedCommand { get; }
	public AsyncRelayCommand CloseOrderCommand { get; }
	public AsyncRelayCommand CancelOrderCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand PostReceiptCommand { get; }
	public AsyncRelayCommand ReverseReceiptCommand { get; }

	public PurchaseOrder Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsOrderReadOnly)); OnPropertyChanged(nameof(CanClose)); OnPropertyChanged(nameof(HasApprovalHistory)); RaiseCommands(); } }
	public bool CanCreateOrders => _orders.CanCurrentUserCreate;
	public bool CanEditOrders => _orders.CanCurrentUserEdit;
	public bool CanSubmitOrders => _orders.CanCurrentUserSubmit;
	public bool CanApproveOrders => _orders.CanCurrentUserApprove;
	public bool CanOrderPurchaseOrders => _orders.CanCurrentUserOrder;
	public bool CanCloseOrders => _orders.CanCurrentUserClose;
	public bool IsDraft => Draft.Status == PurchaseOrderStatus.Draft && (Draft.Id == 0 ? CanCreateOrders : CanEditOrders);
	public bool IsOrderReadOnly => !IsDraft;
	public bool CanClose => CanCloseOrders && Draft.Status is (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived);
	public bool CanReceive => CanOrderPurchaseOrders && SelectedOrder?.Status is (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived);
	public string EditorTitle => Draft.Id == 0 ? "New Purchase Order" : Draft.OrderNumber;
	public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); _ = _search.DebounceAsync(LoadOrdersAsync); } }
	public string SupplierSearchText { get => _supplierSearchText; set { if (_supplierSearchText == value) return; _supplierSearchText = value; OnPropertyChanged(); _ = _supplierSearch.DebounceAsync(LoadSupplierOptionsAsync); } }
	public string ItemSearchText { get => _itemSearchText; set { if (_itemSearchText == value) return; _itemSearchText = value; OnPropertyChanged(); _ = _itemSearch.DebounceAsync(LoadItemOptionsAsync); } }
	public PurchaseOrderStatusFilter SelectedStatusFilter { get => _selectedStatusFilter; set { if (_selectedStatusFilter == value) return; _selectedStatusFilter = value; OnPropertyChanged(); _ = LoadOrdersAsync(); } }
	public PurchaseOrder? SelectedOrder
	{
		get => _selectedOrder;
		set { if (_selectedOrder == value) return; _selectedOrder = value; OnPropertyChanged(); _ = SelectOrderAsync(value); }
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
	public GoodsReceipt? SelectedReceipt { get => _selectedReceipt; set { if (_selectedReceipt == value) return; _selectedReceipt = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanReverseReceipt)); ReverseReceiptCommand.RaiseCanExecuteChanged(); } }
	public bool CanReverseReceipt => CanOrderPurchaseOrders && SelectedReceipt is { IsReversed: false };
	public ReasonCode? SelectedReversalReasonCode { get => _selectedReversalReasonCode; set { if (_selectedReversalReasonCode == value) return; _selectedReversalReasonCode = value; OnPropertyChanged(); ReverseReceiptCommand.RaiseCanExecuteChanged(); } }
	public string ReversalReason { get => _reversalReason; set { if (_reversalReason == value) return; _reversalReason = value; OnPropertyChanged(); ReverseReceiptCommand.RaiseCanExecuteChanged(); } }
	public string ApprovalComment { get => _approvalComment; set { if (_approvalComment == value) return; _approvalComment = value; OnPropertyChanged(); } }
	public string CloseReason { get => _closeReason; set { if (_closeReason == value) return; _closeReason = value; OnPropertyChanged(); CloseOrderCommand.RaiseCanExecuteChanged(); } }
	public bool HasApprovalHistory => Draft.SubmittedAtUtc is not null;

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading purchase orders");
		try
		{
			var suppliersTask = _suppliers.SearchActiveAsync(SupplierSearchText, 50, cancellationToken);
			var itemsTask = _items.SearchItemsAsync(ItemSearchText, 1, 50, cancellationToken);
			var ordersTask = _orders.SearchAsync(SearchText, SelectedStatusFilter.Status, 1, 100, cancellationToken);
			var reasonCodesTask = _reasonCodes.GetActiveAsync(cancellationToken);
			await Task.WhenAll(suppliersTask, itemsTask, ordersTask, reasonCodesTask);
			ReplaceOptions(Suppliers, await suppliersTask);
			ReplaceOptions(Items, (await itemsTask).Items);
			ReversalReasonCodes.Clear(); foreach (var reasonCode in await reasonCodesTask) ReversalReasonCodes.Add(reasonCode);
			ReplaceOrders((await ordersTask).Items); CompleteOperation(Orders.Count == 0, $"{Orders.Count:N0} purchase orders");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase orders could not be loaded"); }
	}

	private async Task LoadOrdersAsync(CancellationToken cancellationToken = default)
	{
		try { ReplaceOrders((await _orders.SearchAsync(SearchText, SelectedStatusFilter.Status, 1, 100, cancellationToken)).Items); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase orders could not be loaded"); }
	}

	private async Task LoadSupplierOptionsAsync(CancellationToken cancellationToken = default)
	{
		try { ReplaceOptions(Suppliers, await _suppliers.SearchActiveAsync(SupplierSearchText, 50, cancellationToken)); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier options could not be loaded"); }
	}

	private async Task LoadItemOptionsAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var page = await _items.SearchItemsAsync(ItemSearchText, 1, 50, cancellationToken);
			ReplaceOptions(Items, page.Items);
			if (SelectedItem is not null && Items.All(item => item.Id != SelectedItem.Id)) SelectedItem = null;
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Item options could not be loaded"); }
	}

	private async Task SelectOrderAsync(PurchaseOrder? order)
	{
		if (order is null) { Draft = NewOrderDraft(); CloseReason = string.Empty; Lines.Clear(); ReceiptLines.Clear(); GoodsReceipts.Clear(); SelectedReceipt = null; return; }
		try
		{
			var details = await _orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Purchase order was not found.");
			if (Suppliers.All(supplier => supplier.Id != details.SupplierId))
			{
				var supplier = await _suppliers.GetByIdAsync(details.SupplierId);
				if (supplier is not null) Suppliers.Add(supplier);
			}
			Draft = Copy(details); ApprovalComment = string.Empty; CloseReason = string.Empty; Lines.Clear(); foreach (var line in details.Lines) Lines.Add(Copy(line));
			OnPropertyChanged(nameof(EditorTitle)); OnPropertyChanged(nameof(CanReceive)); RaiseCommands();
			await Task.WhenAll(BuildReceiptLinesAsync(details), LoadReceiptsAsync(details.Id));
		}
		catch (Exception exception) { FailOperation(exception, "Purchase order details could not be loaded"); }
	}

	private void NewOrder() { SelectedOrder = null; Draft = NewOrderDraft(); Lines.Clear(); ReceiptLines.Clear(); OnPropertyChanged(nameof(EditorTitle)); }
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
		try { Draft.Lines = Lines.Select(Copy).ToArray(); var saved = await _orders.SaveDraftAsync(Copy(Draft), cancellationToken); await LoadOrdersAsync(cancellationToken); SelectedOrder = Orders.FirstOrDefault(order => order.Id == saved.Id); CompleteOperation(false, "Purchase order saved"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase order could not be saved"); }
	}
	private async Task MarkOrderedAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.MarkOrderedAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order marked as ordered", cancellationToken);
	private async Task SubmitForApprovalAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.SubmitForApprovalAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order submitted for approval", cancellationToken);
	private async Task ApproveAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.ApproveAsync(Draft.Id, Draft.Version, ApprovalComment, cancellationToken), "Purchase order approved", cancellationToken);
	private async Task RejectAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.RejectAsync(Draft.Id, Draft.Version, ApprovalComment, cancellationToken), "Purchase order rejected", cancellationToken);
	private async Task ReopenRejectedAsync(CancellationToken cancellationToken) => await ChangeStatusAsync(() => _orders.ReopenRejectedAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order reopened as draft", cancellationToken);
	private async Task CloseOrderAsync(CancellationToken cancellationToken)
	{
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Close Purchase Order", $"Close purchase order {Draft.OrderNumber}? Open quantities will remain open, but no further goods receipts will be accepted.\n\nReason: {CloseReason.Trim()}", true))) return;
		await ChangeStatusAsync(() => _orders.CloseAsync(Draft.Id, Draft.Version, CloseReason, cancellationToken), "Purchase order closed", cancellationToken);
	}
	private async Task CancelOrderAsync(CancellationToken cancellationToken)
	{
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Cancel Purchase Order", $"Cancel purchase order {Draft.OrderNumber}?", true))) return;
		await ChangeStatusAsync(() => _orders.CancelAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order cancelled", cancellationToken);
	}
	private async Task ChangeStatusAsync(Func<Task<PurchaseOrder>> action, string message, CancellationToken cancellationToken)
	{
		BeginOperation("Updating purchase order status");
		try { var saved = await action(); ApplyChangedOrder(saved); ApprovalComment = string.Empty; CloseReason = string.Empty; CompleteOperation(false, message); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase order status could not be updated"); }
	}

	private async Task BuildReceiptLinesAsync(PurchaseOrder order)
	{
		ReceiptLines.Clear(); SupplierDeliveryNoteNumber = string.Empty; ReceiptDate = DateTime.Today; ReceiptNotes = null;
		if (order.Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived)) return;
		var openLines = order.Lines.Where(line => line.OpenQuantity > 0).ToArray();
		var optionsByItem = (await _receipts.GetInventoryOptionsAsync(openLines.Select(line => line.ItemId)))
			.GroupBy(option => option.ItemId)
			.ToDictionary(group => group.Key, group => group.ToArray());
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
			await _receipts.PostAsync(receipt, cancellationToken); await LoadOrdersAsync(cancellationToken); SelectedOrder = Orders.FirstOrDefault(order => order.Id == receipt.PurchaseOrderId); CompleteOperation(false, $"Goods receipt {receipt.ReceiptNumber} posted");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Goods receipt could not be posted"); }
	}
	private async Task LoadReceiptsAsync(long purchaseOrderId, CancellationToken cancellationToken = default)
	{
		var receipts = await _receipts.ListByPurchaseOrderAsync(purchaseOrderId, cancellationToken);
		GoodsReceipts.Clear(); foreach (var receipt in receipts) GoodsReceipts.Add(receipt);
		SelectedReceipt = GoodsReceipts.FirstOrDefault();
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
			await BuildReceiptLinesAsync(updatedOrder);
			OnPropertyChanged(nameof(EditorTitle));
			OnPropertyChanged(nameof(CanReceive));
			RaiseCommands();
			CompleteOperation(false, "Goods receipt reversed");
		}
		catch (ConcurrencyConflictException)
		{
			FailOperation(new InvalidOperationException("The goods receipt was changed or reversed by another user."), "Goods receipt could not be reversed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Goods receipt could not be reversed"); }
	}

	private void ReplaceOrders(IReadOnlyList<PurchaseOrder> values) { var id = SelectedOrder?.Id; CollectionSynchronizer.Replace(Orders, values); var selected = Orders.FirstOrDefault(value => value.Id == id); _selectedOrder = null; OnPropertyChanged(nameof(SelectedOrder)); if (selected is not null) SelectedOrder = selected; }
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
		if (existing is not null) Orders[Orders.IndexOf(existing)] = order;
		_selectedOrder = order;
		OnPropertyChanged(nameof(SelectedOrder));
		Draft = Copy(order);
		OnPropertyChanged(nameof(EditorTitle));
		OnPropertyChanged(nameof(CanReceive));
		OnPropertyChanged(nameof(HasApprovalHistory));
		RaiseCommands();
	}
	private void RaiseCommands() { SaveOrderCommand.RaiseCanExecuteChanged(); SubmitForApprovalCommand.RaiseCanExecuteChanged(); ApproveCommand.RaiseCanExecuteChanged(); RejectCommand.RaiseCanExecuteChanged(); ReopenRejectedCommand.RaiseCanExecuteChanged(); MarkOrderedCommand.RaiseCanExecuteChanged(); CloseOrderCommand.RaiseCanExecuteChanged(); CancelOrderCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); PostReceiptCommand.RaiseCanExecuteChanged(); ReverseReceiptCommand.RaiseCanExecuteChanged(); }
	private static PurchaseOrder NewOrderDraft() => new() { OrderDate = DateTime.Today, ExpectedDeliveryDate = DateTime.Today.AddDays(7) };
	private static PurchaseOrder Copy(PurchaseOrder value) => new() { Id = value.Id, OrderNumber = value.OrderNumber, SupplierId = value.SupplierId, SupplierName = value.SupplierName, OrderDate = value.OrderDate, ExpectedDeliveryDate = value.ExpectedDeliveryDate, Notes = value.Notes, Status = value.Status, CreatedByUserId = value.CreatedByUserId, SubmittedByUserId = value.SubmittedByUserId, SubmittedAtUtc = value.SubmittedAtUtc, ApprovalDecisionByUserId = value.ApprovalDecisionByUserId, ApprovalDecisionAtUtc = value.ApprovalDecisionAtUtc, ApprovalComment = value.ApprovalComment, ClosedByUserId = value.ClosedByUserId, ClosedAtUtc = value.ClosedAtUtc, CloseReason = value.CloseReason, CreatedByUserDisplay = value.CreatedByUserDisplay, SubmittedByUserDisplay = value.SubmittedByUserDisplay, ApprovalDecisionByUserDisplay = value.ApprovalDecisionByUserDisplay, ClosedByUserDisplay = value.ClosedByUserDisplay, Version = value.Version, Lines = value.Lines.Select(Copy).ToArray() };
	private static PurchaseOrderLine Copy(PurchaseOrderLine value) => new() { Id = value.Id, PurchaseOrderId = value.PurchaseOrderId, LineNumber = value.LineNumber, ItemId = value.ItemId, ItemPartNumber = value.ItemPartNumber, ItemDescription = value.ItemDescription, Quantity = value.Quantity, UnitPrice = value.UnitPrice, ReceivedQuantity = value.ReceivedQuantity, Version = value.Version };
	private static string StatusLabel(PurchaseOrderStatus status) => status switch { PurchaseOrderStatus.PartiallyReceived => "Partially Received", PurchaseOrderStatus.PendingApproval => "Pending Approval", _ => status.ToString() };
	public void Dispose() { _search.Dispose(); _supplierSearch.Dispose(); _itemSearch.Dispose(); SaveOrderCommand.Dispose(); SubmitForApprovalCommand.Dispose(); ApproveCommand.Dispose(); RejectCommand.Dispose(); ReopenRejectedCommand.Dispose(); MarkOrderedCommand.Dispose(); CloseOrderCommand.Dispose(); CancelOrderCommand.Dispose(); PostReceiptCommand.Dispose(); ReverseReceiptCommand.Dispose(); }
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
