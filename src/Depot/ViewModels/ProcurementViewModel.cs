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
	private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
	private PurchaseOrder? _selectedOrder;
	private PurchaseOrder _draft = NewOrderDraft();
	private PurchaseOrderLine? _selectedLine;
	private Item? _selectedItem;
	private string _searchText = string.Empty;
	private PurchaseOrderStatusFilter _selectedStatusFilter;
	private int _lineQuantity = 1;
	private decimal _lineUnitPrice;
	private string _invoiceNumber = string.Empty;
	private DateTime _invoiceDate = DateTime.Today;
	private string? _invoiceDocumentPath;

	public ProcurementViewModel(PurchaseOrderService orders, GoodsReceiptService receipts, SupplierService suppliers, ItemService items, IFileDialogService fileDialogs)
	{
		_orders = orders; _receipts = receipts; _suppliers = suppliers; _items = items; _fileDialogs = fileDialogs;
		StatusFilters = [new("All statuses", null), .. Enum.GetValues<PurchaseOrderStatus>().Select(status => new PurchaseOrderStatusFilter(StatusLabel(status), status))];
		_selectedStatusFilter = StatusFilters[0];
		NewOrderCommand = new RelayCommand(NewOrder);
		SaveOrderCommand = new AsyncRelayCommand(SaveOrderAsync, () => IsDraft);
		MarkOrderedCommand = new AsyncRelayCommand(MarkOrderedAsync, () => SelectedOrder?.Status == PurchaseOrderStatus.Draft);
		CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync, () => SelectedOrder?.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered);
		AddLineCommand = new RelayCommand(AddOrUpdateLine, () => IsDraft && SelectedItem is not null);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null);
		PostReceiptCommand = new AsyncRelayCommand(PostReceiptAsync, () => CanReceive);
		BrowseInvoiceCommand = new RelayCommand(BrowseInvoice);
	}

	public ObservableCollection<PurchaseOrder> Orders { get; } = new();
	public ObservableCollection<Supplier> Suppliers { get; } = new();
	public ObservableCollection<Item> Items { get; } = new();
	public ObservableCollection<PurchaseOrderLine> Lines { get; } = new();
	public ObservableCollection<GoodsReceiptLineEditor> ReceiptLines { get; } = new();
	public IReadOnlyList<PurchaseOrderStatusFilter> StatusFilters { get; }
	public RelayCommand NewOrderCommand { get; }
	public AsyncRelayCommand SaveOrderCommand { get; }
	public AsyncRelayCommand MarkOrderedCommand { get; }
	public AsyncRelayCommand CancelOrderCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand PostReceiptCommand { get; }
	public RelayCommand BrowseInvoiceCommand { get; }

	public PurchaseOrder Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsOrderReadOnly)); RaiseCommands(); } }
	public bool IsDraft => Draft.Status == PurchaseOrderStatus.Draft;
	public bool IsOrderReadOnly => !IsDraft;
	public bool CanReceive => SelectedOrder?.Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived;
	public string EditorTitle => Draft.Id == 0 ? "New Purchase Order" : Draft.OrderNumber;
	public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); _ = _search.DebounceAsync(LoadOrdersAsync); } }
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
	public string InvoiceNumber { get => _invoiceNumber; set { if (_invoiceNumber == value) return; _invoiceNumber = value; OnPropertyChanged(); } }
	public DateTime InvoiceDate { get => _invoiceDate; set { if (_invoiceDate == value) return; _invoiceDate = value; OnPropertyChanged(); } }
	public string? InvoiceDocumentPath { get => _invoiceDocumentPath; set { if (_invoiceDocumentPath == value) return; _invoiceDocumentPath = value; OnPropertyChanged(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading purchase orders");
		try
		{
			var suppliersTask = _suppliers.GetActiveAsync(cancellationToken);
			var itemsTask = _items.SearchItemsAsync(null, 1, 200, cancellationToken);
			var ordersTask = _orders.SearchAsync(SearchText, SelectedStatusFilter.Status, 1, 100, cancellationToken);
			await Task.WhenAll(suppliersTask, itemsTask, ordersTask);
			Suppliers.Clear(); foreach (var supplier in await suppliersTask) Suppliers.Add(supplier);
			Items.Clear(); foreach (var item in (await itemsTask).Items) Items.Add(item);
			ReplaceOrders((await ordersTask).Items); CompleteOperation(Orders.Count == 0, $"{Orders.Count:N0} purchase orders");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase orders could not be loaded"); }
	}

	private async Task LoadOrdersAsync(CancellationToken cancellationToken = default)
	{
		try { ReplaceOrders((await _orders.SearchAsync(SearchText, SelectedStatusFilter.Status, 1, 100, cancellationToken)).Items); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase orders could not be loaded"); }
	}

	private async Task SelectOrderAsync(PurchaseOrder? order)
	{
		if (order is null) { Draft = NewOrderDraft(); Lines.Clear(); ReceiptLines.Clear(); return; }
		try
		{
			var details = await _orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Purchase order was not found.");
			Draft = Copy(details); Lines.Clear(); foreach (var line in details.Lines) Lines.Add(Copy(line));
			OnPropertyChanged(nameof(EditorTitle)); OnPropertyChanged(nameof(CanReceive)); RaiseCommands();
			await BuildReceiptLinesAsync(details);
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
	private async Task CancelOrderAsync(CancellationToken cancellationToken)
	{
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Cancel Purchase Order", $"Cancel purchase order {Draft.OrderNumber}?", true))) return;
		await ChangeStatusAsync(() => _orders.CancelAsync(Draft.Id, Draft.Version, cancellationToken), "Purchase order cancelled", cancellationToken);
	}
	private void BrowseInvoice()
	{
		var path = _fileDialogs.ShowOpenFile(new OpenFileDialogRequest("Select Invoice Document", "Invoice documents (*.pdf;*.xml)|*.pdf;*.xml|All files (*.*)|*.*"));
		if (path is not null) InvoiceDocumentPath = path;
	}
	private async Task ChangeStatusAsync(Func<Task<PurchaseOrder>> action, string message, CancellationToken cancellationToken)
	{
		BeginOperation("Updating purchase order status");
		try { var saved = await action(); await LoadOrdersAsync(cancellationToken); SelectedOrder = Orders.FirstOrDefault(order => order.Id == saved.Id); CompleteOperation(false, message); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Purchase order status could not be updated"); }
	}

	private async Task BuildReceiptLinesAsync(PurchaseOrder order)
	{
		ReceiptLines.Clear(); InvoiceNumber = string.Empty; InvoiceDate = DateTime.Today; InvoiceDocumentPath = null;
		if (order.Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived)) return;
		foreach (var line in order.Lines.Where(line => line.OpenQuantity > 0))
		{
			var editor = new GoodsReceiptLineEditor(line);
			foreach (var option in await _receipts.GetInventoryOptionsAsync(line.ItemId)) editor.InventoryOptions.Add(option);
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
			var receipt = new GoodsReceipt { PurchaseOrderId = SelectedOrder.Id, ReceiptDate = DateTime.Today, InvoiceNumber = InvoiceNumber, InvoiceDate = InvoiceDate, InvoiceDocumentPath = InvoiceDocumentPath, Lines = lines };
			await _receipts.PostAsync(receipt, cancellationToken); await LoadOrdersAsync(cancellationToken); SelectedOrder = Orders.FirstOrDefault(order => order.Id == receipt.PurchaseOrderId); CompleteOperation(false, $"Goods receipt {receipt.ReceiptNumber} posted");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Goods receipt could not be posted"); }
	}

	private void ReplaceOrders(IReadOnlyList<PurchaseOrder> values) { var id = SelectedOrder?.Id; Orders.Clear(); foreach (var value in values) Orders.Add(value); var selected = Orders.FirstOrDefault(value => value.Id == id); _selectedOrder = null; OnPropertyChanged(nameof(SelectedOrder)); if (selected is not null) SelectedOrder = selected; }
	private void RaiseCommands() { SaveOrderCommand.RaiseCanExecuteChanged(); MarkOrderedCommand.RaiseCanExecuteChanged(); CancelOrderCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); PostReceiptCommand.RaiseCanExecuteChanged(); }
	private static PurchaseOrder NewOrderDraft() => new() { OrderDate = DateTime.Today, ExpectedDeliveryDate = DateTime.Today.AddDays(7) };
	private static PurchaseOrder Copy(PurchaseOrder value) => new() { Id = value.Id, OrderNumber = value.OrderNumber, SupplierId = value.SupplierId, SupplierName = value.SupplierName, OrderDate = value.OrderDate, ExpectedDeliveryDate = value.ExpectedDeliveryDate, Notes = value.Notes, Status = value.Status, Version = value.Version, Lines = value.Lines.Select(Copy).ToArray() };
	private static PurchaseOrderLine Copy(PurchaseOrderLine value) => new() { Id = value.Id, PurchaseOrderId = value.PurchaseOrderId, LineNumber = value.LineNumber, ItemId = value.ItemId, ItemPartNumber = value.ItemPartNumber, ItemDescription = value.ItemDescription, Quantity = value.Quantity, UnitPrice = value.UnitPrice, ReceivedQuantity = value.ReceivedQuantity, Version = value.Version };
	private static string StatusLabel(PurchaseOrderStatus status) => status switch { PurchaseOrderStatus.PartiallyReceived => "Partially Received", _ => status.ToString() };
	public void Dispose() { _search.Dispose(); SaveOrderCommand.Dispose(); MarkOrderedCommand.Dispose(); CancelOrderCommand.Dispose(); PostReceiptCommand.Dispose(); }
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
