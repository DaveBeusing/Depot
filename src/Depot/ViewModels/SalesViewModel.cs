// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Text.Json;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public enum SalesSection
{
	Overview,
	Customers,
	SalesOrders,
	Approvals,
	Shipping,
	Invoices
}

public enum SalesQuickOpenKind
{
	Customer,
	SalesOrder,
	Shipment,
	Invoice,
	CustomerReturn,
	CreditNote
}

public sealed record SalesQuickOpenItem(SalesQuickOpenKind Kind, long Id, string Title, string Subtitle);

public sealed class SalesViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly CustomerService _customers;
	private readonly SalesOrderService _orders;
	private readonly ShipmentService _shipments;
	private readonly SalesInvoiceService _invoices;
	private readonly ItemService _items;
	private readonly IAuthorizationService _authorization;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;
	private readonly LatestRequest _loadRequest = new();
	private SalesSection _section;
	private string _searchText = string.Empty;
	private Customer? _selectedCustomer;
	private Customer? _customerBaseline;
	private Customer? _selectedOrderCustomer;
	private CustomerAddress? _selectedBillingAddress;
	private CustomerAddress? _selectedShippingAddress;
	private SalesOrder? _selectedOrder;
	private SalesOrder? _orderBaseline;
	private Shipment? _selectedShipment;
	private Shipment? _shipmentBaseline;
	private SalesInvoice? _selectedInvoice;
	private CustomerReturn? _selectedCustomerReturn;
	private SalesCreditNote? _selectedCreditNote;
	private Customer _customerDraft = NewCustomerDraft();
	private SalesOrder _orderDraft = NewOrderDraft();
	private Item? _selectedItem;
	private int _lineQuantity = 1;
	private decimal _lineUnitPrice;
	private decimal _lineDiscountPercent;
	private decimal _lineTaxRate = 19m;
	private SalesOrderLine? _selectedOrderLine;
	private SalesInventoryAvailability? _selectedAvailability;
	private int _reservationQuantity = 1;
	private InventoryReservation? _selectedReservation;
	private int _shipmentQuantity = 1;
	private string? _carrier;
	private string? _trackingNumber;
	private string? _shipmentNotes;
	private string? _approvalComment;
	private string _correctionReason = string.Empty;

	public SalesViewModel(
		CustomerService customers,
		SalesOrderService orders,
		ShipmentService shipments,
		SalesInvoiceService invoices,
		ItemService items,
		IAuthorizationService authorization,
		IFileDialogService fileDialogs,
		SalesDocumentService documents)
	{
		_customers = customers;
		_orders = orders;
		_shipments = shipments;
		_invoices = invoices;
		_items = items;
		_authorization = authorization;
		_fileDialogs = fileDialogs;
		_documents = documents;

		NewCustomerCommand = new RelayCommand(NewCustomer, () => _customers.CanCreate);
		SaveCustomerCommand = new AsyncRelayCommand(SaveCustomerAsync, () => CustomerDraft.Id == 0 ? _customers.CanCreate : _customers.CanEdit);
		NewOrderCommand = new RelayCommand(NewOrder, () => _orders.CanCreate);
		SaveOrderCommand = new AsyncRelayCommand(SaveOrderAsync, () => OrderDraft.Status == SalesOrderStatus.Draft && (OrderDraft.Id == 0 ? _orders.CanCreate : _orders.CanEdit));
		AddLineCommand = new RelayCommand(AddLine, () => OrderDraft.Status == SalesOrderStatus.Draft && SelectedItem is not null && LineQuantity > 0);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => OrderDraft.Status == SalesOrderStatus.Draft && SelectedOrderLine is not null);
		SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => _orders.CanSubmit && OrderDraft.Id > 0 && OrderDraft.Status == SalesOrderStatus.Draft);
		ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => _orders.CanApprove && SelectedOrder?.Status == SalesOrderStatus.PendingApproval);
		RejectCommand = new AsyncRelayCommand(RejectAsync, () => _orders.CanApprove && SelectedOrder?.Status == SalesOrderStatus.PendingApproval);
		ReserveCommand = new AsyncRelayCommand(ReserveAsync, () => _orders.CanEdit && SelectedOrder is { Status: SalesOrderStatus.Approved or SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped } && SelectedOrderLine is not null && SelectedAvailability is not null && ReservationQuantity > 0);
		ReleaseCommand = new AsyncRelayCommand(ReleaseAsync, () => _orders.CanRelease && SelectedOrder?.Status == SalesOrderStatus.Approved);
		CreateShipmentCommand = new AsyncRelayCommand(CreateShipmentAsync, () => _shipments.CanCreate && SelectedOrder is { Status: SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped } && SelectedReservation is not null && ShipmentQuantity > 0);
		SaveShipmentCommand = new AsyncRelayCommand(SaveShipmentAsync, () => _shipments.CanEdit && SelectedShipment?.Status == ShipmentStatus.Draft);
		PostShipmentCommand = new AsyncRelayCommand(PostShipmentAsync, () => _shipments.CanPost && SelectedShipment?.Status == ShipmentStatus.Draft);
		ReverseShipmentCommand = new AsyncRelayCommand(ReverseShipmentAsync, () => _shipments.CanReverse && SelectedShipment?.Status == ShipmentStatus.Posted && SelectedShipment.ReversedAtUtc is null && !string.IsNullOrWhiteSpace(CorrectionReason));
		CreateCustomerReturnCommand = new AsyncRelayCommand(CreateCustomerReturnAsync, () => _shipments.CanCreateCustomerReturn && SelectedShipment?.Status == ShipmentStatus.Posted && !string.IsNullOrWhiteSpace(CorrectionReason));
		PostCustomerReturnCommand = new AsyncRelayCommand(PostCustomerReturnAsync, () => _shipments.CanPostCustomerReturn && SelectedCustomerReturn?.Status == CustomerReturnStatus.Draft);
		CreateInvoiceCommand = new AsyncRelayCommand(CreateInvoiceAsync, () => _invoices.CanCreate && SelectedShipment?.Status == ShipmentStatus.Posted);
		CancelInvoiceCommand = new AsyncRelayCommand(CancelInvoiceAsync, () => _invoices.CanCreate && SelectedInvoice?.Status == SalesInvoiceStatus.Draft);
		PostInvoiceCommand = new AsyncRelayCommand(PostInvoiceAsync, () => _invoices.CanPost && SelectedInvoice?.Status == SalesInvoiceStatus.Draft);
		CreateCreditNoteCommand = new AsyncRelayCommand(CreateCreditNoteAsync, () => _invoices.CanCreateCreditNote && SelectedInvoice?.Status == SalesInvoiceStatus.Posted && !string.IsNullOrWhiteSpace(CorrectionReason));
		PostCreditNoteCommand = new AsyncRelayCommand(PostCreditNoteAsync, () => _invoices.CanPostCreditNote && SelectedCreditNote?.Status == SalesCreditNoteStatus.Draft);
		OrderConfirmationCommand = new RelayCommand(GenerateOrderConfirmation, () => SelectedOrder is not null);
		DeliveryNoteCommand = new RelayCommand(GenerateDeliveryNote, () => SelectedShipment is not null);
		InvoicePdfCommand = new RelayCommand(GenerateInvoice, () => SelectedInvoice is not null);
	}

	public ObservableCollection<Customer> Customers { get; } = [];
	public ObservableCollection<CustomerAddress> BillingAddresses { get; } = [];
	public ObservableCollection<CustomerAddress> ShippingAddresses { get; } = [];
	public ObservableCollection<SalesOrder> Orders { get; } = [];
	public ObservableCollection<SalesOrder> PendingApprovals { get; } = [];
	public ObservableCollection<Shipment> Shipments { get; } = [];
	public ObservableCollection<SalesInvoice> Invoices { get; } = [];
	public ObservableCollection<CustomerReturn> CustomerReturns { get; } = [];
	public ObservableCollection<SalesCreditNote> CreditNotes { get; } = [];
	public ObservableCollection<Item> Items { get; } = [];
	public ObservableCollection<SalesOrderLine> OrderLines { get; } = [];
	public ObservableCollection<InventoryReservation> Reservations { get; } = [];
	public ObservableCollection<SalesInventoryAvailability> Availability { get; } = [];

	public RelayCommand NewCustomerCommand { get; }
	public AsyncRelayCommand SaveCustomerCommand { get; }
	public RelayCommand NewOrderCommand { get; }
	public AsyncRelayCommand SaveOrderCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand SubmitCommand { get; }
	public AsyncRelayCommand ApproveCommand { get; }
	public AsyncRelayCommand RejectCommand { get; }
	public AsyncRelayCommand ReserveCommand { get; }
	public AsyncRelayCommand ReleaseCommand { get; }
	public AsyncRelayCommand CreateShipmentCommand { get; }
	public AsyncRelayCommand SaveShipmentCommand { get; }
	public AsyncRelayCommand PostShipmentCommand { get; }
	public AsyncRelayCommand ReverseShipmentCommand { get; }
	public AsyncRelayCommand CreateCustomerReturnCommand { get; }
	public AsyncRelayCommand PostCustomerReturnCommand { get; }
	public AsyncRelayCommand CreateInvoiceCommand { get; }
	public AsyncRelayCommand CancelInvoiceCommand { get; }
	public AsyncRelayCommand PostInvoiceCommand { get; }
	public AsyncRelayCommand CreateCreditNoteCommand { get; }
	public AsyncRelayCommand PostCreditNoteCommand { get; }
	public RelayCommand OrderConfirmationCommand { get; }
	public RelayCommand DeliveryNoteCommand { get; }
	public RelayCommand InvoicePdfCommand { get; }

	public SalesSection Section
	{
		get => _section;
		set
		{
			if (_section == value) return;
			_section = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsOverview));
			OnPropertyChanged(nameof(IsCustomers));
			OnPropertyChanged(nameof(IsSalesOrders));
			OnPropertyChanged(nameof(IsApprovals));
			OnPropertyChanged(nameof(IsShipping));
			OnPropertyChanged(nameof(IsInvoices));
		}
	}

	public bool IsOverview => Section == SalesSection.Overview;
	public bool IsCustomers => Section == SalesSection.Customers;
	public bool IsSalesOrders => Section == SalesSection.SalesOrders;
	public bool IsApprovals => Section == SalesSection.Approvals;
	public bool IsShipping => Section == SalesSection.Shipping;
	public bool IsInvoices => Section == SalesSection.Invoices;
	public bool CanViewCustomers => _authorization.HasPermission(ApplicationPermission.CustomersView);
	public bool CanViewOrders => _authorization.HasPermission(ApplicationPermission.SalesOrdersView);
	public bool CanViewShipments => _authorization.HasPermission(ApplicationPermission.ShipmentsView);
	public bool CanViewInvoices => _authorization.HasPermission(ApplicationPermission.SalesInvoicesView);
	public bool CanViewCustomerReturns => _authorization.HasPermission(ApplicationPermission.CustomerReturnsView);
	public bool CanViewCreditNotes => _authorization.HasPermission(ApplicationPermission.CreditNotesView);
	public bool CanEditCustomers => _customers.CanEdit;

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); } }
	public Customer CustomerDraft { get => _customerDraft; private set { _customerDraft = value; OnPropertyChanged(); } }
	public SalesOrder OrderDraft { get => _orderDraft; private set { _orderDraft = value; OnPropertyChanged(); OnPropertyChanged(nameof(OrderTitle)); RaiseCommands(); } }
	public string OrderTitle => OrderDraft.Id == 0 ? "New Sales Order" : OrderDraft.OrderNumber;
	public string? ApprovalComment { get => _approvalComment; set { _approvalComment = value; OnPropertyChanged(); } }
	public string? Carrier { get => _carrier; set { if (_carrier == value) return; _carrier = value; OnPropertyChanged(); } }
	public string? TrackingNumber { get => _trackingNumber; set { if (_trackingNumber == value) return; _trackingNumber = value; OnPropertyChanged(); } }
	public string? ShipmentNotes { get => _shipmentNotes; set { if (_shipmentNotes == value) return; _shipmentNotes = value; OnPropertyChanged(); } }
	public string CorrectionReason { get => _correctionReason; set { if (_correctionReason == value) return; _correctionReason = value; OnPropertyChanged(); RaiseCommands(); } }

	public Customer? SelectedCustomer { get => _selectedCustomer; set { if (_selectedCustomer == value) return; _selectedCustomer = value; OnPropertyChanged(); _ = LoadSelectedCustomerAsync(value); } }
	public Customer? SelectedOrderCustomer { get => _selectedOrderCustomer; set { if (_selectedOrderCustomer == value) return; _selectedOrderCustomer = value; OnPropertyChanged(); _ = LoadOrderAddressesAsync(value, true); } }
	public CustomerAddress? SelectedBillingAddress { get => _selectedBillingAddress; set { if (_selectedBillingAddress == value) return; _selectedBillingAddress = value; if (value is not null) OrderDraft.BillingAddress = value.Address; OnPropertyChanged(); OnPropertyChanged(nameof(OrderDraft)); } }
	public CustomerAddress? SelectedShippingAddress { get => _selectedShippingAddress; set { if (_selectedShippingAddress == value) return; _selectedShippingAddress = value; if (value is not null) OrderDraft.ShippingAddress = value.Address; OnPropertyChanged(); OnPropertyChanged(nameof(OrderDraft)); } }
	public SalesOrder? SelectedOrder { get => _selectedOrder; set { if (_selectedOrder == value) return; _selectedOrder = value; OnPropertyChanged(); _ = LoadSelectedOrderAsync(value); RaiseCommands(); } }
	public Shipment? SelectedShipment { get => _selectedShipment; set { if (_selectedShipment == value) return; _selectedShipment = value; OnPropertyChanged(); _ = LoadSelectedShipmentAsync(value); RaiseCommands(); } }
	public SalesInvoice? SelectedInvoice { get => _selectedInvoice; set { if (_selectedInvoice == value) return; _selectedInvoice = value; OnPropertyChanged(); RaiseCommands(); } }
	public CustomerReturn? SelectedCustomerReturn { get => _selectedCustomerReturn; set { if (_selectedCustomerReturn == value) return; _selectedCustomerReturn = value; OnPropertyChanged(); RaiseCommands(); } }
	public SalesCreditNote? SelectedCreditNote { get => _selectedCreditNote; set { if (_selectedCreditNote == value) return; _selectedCreditNote = value; OnPropertyChanged(); RaiseCommands(); } }
	public Item? SelectedItem { get => _selectedItem; set { if (_selectedItem == value) return; _selectedItem = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public SalesOrderLine? SelectedOrderLine { get => _selectedOrderLine; set { if (_selectedOrderLine == value) return; _selectedOrderLine = value; OnPropertyChanged(); _ = LoadAvailabilityAsync(value); RaiseCommands(); } }
	public SalesInventoryAvailability? SelectedAvailability { get => _selectedAvailability; set { _selectedAvailability = value; OnPropertyChanged(); ReserveCommand.RaiseCanExecuteChanged(); } }
	public InventoryReservation? SelectedReservation { get => _selectedReservation; set { _selectedReservation = value; OnPropertyChanged(); CreateShipmentCommand.RaiseCanExecuteChanged(); } }
	public int LineQuantity { get => _lineQuantity; set { _lineQuantity = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public decimal LineUnitPrice { get => _lineUnitPrice; set { _lineUnitPrice = value; OnPropertyChanged(); } }
	public decimal LineDiscountPercent { get => _lineDiscountPercent; set { _lineDiscountPercent = value; OnPropertyChanged(); } }
	public decimal LineTaxRate { get => _lineTaxRate; set { _lineTaxRate = value; OnPropertyChanged(); } }
	public int ReservationQuantity { get => _reservationQuantity; set { _reservationQuantity = value; OnPropertyChanged(); ReserveCommand.RaiseCanExecuteChanged(); } }
	public int ShipmentQuantity { get => _shipmentQuantity; set { _shipmentQuantity = value; OnPropertyChanged(); CreateShipmentCommand.RaiseCanExecuteChanged(); } }

	public long OpenOrdersCount => Orders.LongCount(order => order.Status is not (SalesOrderStatus.Completed or SalesOrderStatus.Cancelled));
	public long PendingApprovalCount => Orders.LongCount(order => order.Status == SalesOrderStatus.PendingApproval);
	public long ReadyToShipCount => Orders.LongCount(order => order.Status is SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped);
	public long DraftShipmentCount => Shipments.LongCount(shipment => shipment.Status == ShipmentStatus.Draft);
	public long DraftInvoiceCount => Invoices.LongCount(invoice => invoice.Status == SalesInvoiceStatus.Draft);
	public decimal PostedRevenue => Invoices.Where(invoice => invoice.Status == SalesInvoiceStatus.Posted).Sum(invoice => invoice.NetAmount);

	public Task<IReadOnlyList<CustomerAddress>> GetCustomerAddressesAsync(long customerId, CancellationToken token = default) => _customers.ListAddressesAsync(customerId, token);
	public Task<CustomerAddress> SaveCustomerAddressAsync(CustomerAddress address, CancellationToken token = default) => _customers.SaveAddressAsync(address, token);
	public async Task ReloadSelectedCustomerAsync(CancellationToken token = default) { if (SelectedCustomer is null) return; var loaded = await _customers.GetByIdAsync(SelectedCustomer.Id, token); if (loaded is not null) { _selectedCustomer = loaded; OnPropertyChanged(nameof(SelectedCustomer)); _customerBaseline = Copy(loaded); CustomerDraft = Copy(loaded); } }

	public bool HasUnsavedChanges()
	{
		if (IsCustomers)
			return _customerBaseline is null ? !Equivalent(CustomerDraft, NewCustomerDraft()) : !Equivalent(CustomerDraft, _customerBaseline);
		if (IsSalesOrders && OrderDraft.Status == SalesOrderStatus.Draft)
		{
			var current = Copy(OrderDraft); current.Lines = OrderLines.Select(CopyLine).ToArray();
			return _orderBaseline is null ? !Equivalent(current, NewOrderDraft()) : !Equivalent(current, _orderBaseline);
		}
		if (IsShipping && SelectedShipment?.Status == ShipmentStatus.Draft && _shipmentBaseline is not null)
			return !string.Equals(Carrier, _shipmentBaseline.Carrier, StringComparison.Ordinal) || !string.Equals(TrackingNumber, _shipmentBaseline.TrackingNumber, StringComparison.Ordinal) || !string.Equals(ShipmentNotes, _shipmentBaseline.Notes, StringComparison.Ordinal);
		return false;
	}

	public void DiscardUnsavedChanges()
	{
		if (IsCustomers) { CustomerDraft = _customerBaseline is null ? NewCustomerDraft() : Copy(_customerBaseline); return; }
		if (IsSalesOrders) { OrderDraft = _orderBaseline is null ? NewOrderDraft() : Copy(_orderBaseline); Replace(OrderLines, OrderDraft.Lines.Select(CopyLine)); return; }
		if (IsShipping && _shipmentBaseline is not null) { Carrier = _shipmentBaseline.Carrier; TrackingNumber = _shipmentBaseline.TrackingNumber; ShipmentNotes = _shipmentBaseline.Notes; }
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		BeginOperation("Loading sales workspace");
		try
		{
			if (CanViewCustomers) Replace(Customers, (await _customers.SearchAsync(SearchText, false, 1, PageSize, request.Token)).Items); else Customers.Clear();
			if (CanViewOrders) { Replace(Orders, (await _orders.SearchAsync(SearchText, null, 1, PageSize, request.Token)).Items); Replace(PendingApprovals, Orders.Where(order => order.Status == SalesOrderStatus.PendingApproval)); }
			else { Orders.Clear(); PendingApprovals.Clear(); }
			if (CanViewShipments) Replace(Shipments, (await _shipments.SearchAsync(SearchText, null, 1, PageSize, request.Token)).Items); else Shipments.Clear();
			if (CanViewInvoices) Replace(Invoices, (await _invoices.SearchAsync(SearchText, null, 1, PageSize, request.Token)).Items); else Invoices.Clear();
			if (CanViewCustomerReturns) Replace(CustomerReturns, (await _shipments.SearchCustomerReturnsAsync(SearchText, null, 1, PageSize, request.Token)).Items); else CustomerReturns.Clear();
			if (CanViewCreditNotes) Replace(CreditNotes, (await _invoices.SearchCreditNotesAsync(SearchText, null, 1, PageSize, request.Token)).Items); else CreditNotes.Clear();
			if (_authorization.HasPermission(ApplicationPermission.ItemsView)) Replace(Items, (await _items.SearchItemsAsync(string.Empty, 1, PageSize, request.Token)).Items); else Items.Clear();
			if (!request.IsCurrent) return;
			NotifyMetrics(); CompleteOperation(false, "Sales workspace loaded");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Sales workspace could not be loaded"); }
	}

	public async Task<IReadOnlyList<SalesQuickOpenItem>> QuickSearchAsync(string text, CancellationToken cancellationToken = default)
	{
		text = text.Trim(); if (text.Length < 2) return [];
		var result = new List<SalesQuickOpenItem>();
		if (CanViewCustomers) result.AddRange((await _customers.SearchAsync(text, false, 1, 6, cancellationToken)).Items.Select(value => new SalesQuickOpenItem(SalesQuickOpenKind.Customer, value.Id, value.Name, value.CustomerNumber)));
		if (CanViewOrders) result.AddRange((await _orders.SearchAsync(text, null, 1, 8, cancellationToken)).Items.Select(value => new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, value.Id, value.OrderNumber, value.CustomerName)));
		if (CanViewShipments) result.AddRange((await _shipments.SearchAsync(text, null, 1, 6, cancellationToken)).Items.Select(value => new SalesQuickOpenItem(SalesQuickOpenKind.Shipment, value.Id, value.ShipmentNumber, value.SalesOrderNumber)));
		if (CanViewInvoices) result.AddRange((await _invoices.SearchAsync(text, null, 1, 6, cancellationToken)).Items.Select(value => new SalesQuickOpenItem(SalesQuickOpenKind.Invoice, value.Id, value.InvoiceNumber, value.CustomerName)));
		if (CanViewCustomerReturns) result.AddRange((await _shipments.SearchCustomerReturnsAsync(text, null, 1, 4, cancellationToken)).Items.Select(value => new SalesQuickOpenItem(SalesQuickOpenKind.CustomerReturn, value.Id, value.ReturnNumber, "Customer return")));
		if (CanViewCreditNotes) result.AddRange((await _invoices.SearchCreditNotesAsync(text, null, 1, 4, cancellationToken)).Items.Select(value => new SalesQuickOpenItem(SalesQuickOpenKind.CreditNote, value.Id, value.CreditNoteNumber, "Credit note")));
		return result;
	}

	public async Task OpenQuickItemAsync(SalesQuickOpenItem item, CancellationToken cancellationToken = default)
	{
		switch (item.Kind)
		{
			case SalesQuickOpenKind.Customer: Section = SalesSection.Customers; SelectedCustomer = await _customers.GetByIdAsync(item.Id, cancellationToken); break;
			case SalesQuickOpenKind.SalesOrder: Section = SalesSection.SalesOrders; SelectedOrder = await _orders.GetByIdAsync(item.Id, cancellationToken); break;
			case SalesQuickOpenKind.Shipment: Section = SalesSection.Shipping; SelectedShipment = await _shipments.GetByIdAsync(item.Id, cancellationToken); break;
			case SalesQuickOpenKind.Invoice: Section = SalesSection.Invoices; SelectedInvoice = await _invoices.GetByIdAsync(item.Id, cancellationToken); break;
			case SalesQuickOpenKind.CustomerReturn: Section = SalesSection.Shipping; SelectedCustomerReturn = CustomerReturns.FirstOrDefault(value => value.Id == item.Id); break;
			case SalesQuickOpenKind.CreditNote: Section = SalesSection.Invoices; SelectedCreditNote = CreditNotes.FirstOrDefault(value => value.Id == item.Id); break;
		}
	}

	private void NewCustomer() { _customerBaseline = null; _selectedCustomer = null; OnPropertyChanged(nameof(SelectedCustomer)); CustomerDraft = NewCustomerDraft(); RequestEditorFocus(); }
	private async Task SaveCustomerAsync(CancellationToken token) { var saved = await _customers.SaveAsync(CustomerDraft, token); _selectedCustomer = saved; OnPropertyChanged(nameof(SelectedCustomer)); _customerBaseline = Copy(saved); CustomerDraft = Copy(saved); await LoadAsync(token); }
	private void NewOrder() { _orderBaseline = null; _selectedOrder = null; OnPropertyChanged(nameof(SelectedOrder)); _selectedOrderCustomer = null; OnPropertyChanged(nameof(SelectedOrderCustomer)); _selectedBillingAddress = null; _selectedShippingAddress = null; BillingAddresses.Clear(); ShippingAddresses.Clear(); OrderDraft = NewOrderDraft(); Replace(OrderLines, []); Replace(Reservations, []); RequestEditorFocus(); }
	private void AddLine() { if (SelectedItem is null || LineQuantity <= 0) return; var line = new SalesOrderLine { LineNumber = OrderLines.Count + 1, ItemId = SelectedItem.Id, PartNumber = SelectedItem.PartNumber, Description = SelectedItem.Description, Quantity = LineQuantity, UnitPrice = LineUnitPrice, DiscountPercent = LineDiscountPercent, TaxRate = LineTaxRate }; OrderLines.Add(line); OrderDraft.Lines = OrderLines.ToArray(); SelectedOrderLine = line; RaiseCommands(); }
	private void RemoveLine() { if (SelectedOrderLine is null) return; OrderLines.Remove(SelectedOrderLine); for (var index = 0; index < OrderLines.Count; index++) OrderLines[index].LineNumber = index + 1; OrderDraft.Lines = OrderLines.ToArray(); SelectedOrderLine = null; RaiseCommands(); }
	private async Task SaveOrderAsync(CancellationToken token)
	{
		if (SelectedOrderCustomer is not null) { OrderDraft.CustomerId = SelectedOrderCustomer.Id; OrderDraft.CustomerName = SelectedOrderCustomer.Name; }
		OrderDraft.BillingAddress = SelectedBillingAddress?.Address ?? OrderDraft.BillingAddress ?? SelectedOrderCustomer?.BillingAddress;
		OrderDraft.ShippingAddress = SelectedShippingAddress?.Address ?? OrderDraft.ShippingAddress ?? SelectedOrderCustomer?.ShippingAddress;
		OrderDraft.Lines = OrderLines.ToArray();
		var saved = await _orders.SaveDraftAsync(OrderDraft, token); SelectedOrder = saved; _orderBaseline = Copy(saved); await LoadAsync(token);
	}
	private async Task SubmitAsync(CancellationToken token) { var source = SelectedOrder ?? OrderDraft; if (source.Id <= 0) return; SelectedOrder = await _orders.SubmitAsync(source.Id, source.Version, token); await LoadAsync(token); }
	private async Task ApproveAsync(CancellationToken token) { if (SelectedOrder is null) return; SelectedOrder = await _orders.ApproveAsync(SelectedOrder.Id, SelectedOrder.Version, ApprovalComment, token); await LoadAsync(token); }
	private async Task RejectAsync(CancellationToken token) { if (SelectedOrder is null) return; SelectedOrder = await _orders.RejectAsync(SelectedOrder.Id, SelectedOrder.Version, ApprovalComment, token); await LoadAsync(token); }

	private async Task ReserveAsync(CancellationToken token)
	{
		if (SelectedOrder is null || SelectedOrderLine is null || SelectedAvailability is null) return;
		var requests = Reservations.Where(value => value.Status == InventoryReservationStatus.Active && value.SalesOrderLineId != SelectedOrderLine.Id).Select(value => new SalesReservationRequest(value.SalesOrderLineId, value.InventoryId, value.Quantity)).ToList();
		requests.Add(new SalesReservationRequest(SelectedOrderLine.Id, SelectedAvailability.InventoryId, ReservationQuantity));
		SelectedOrder = await _orders.SetReservationsAsync(SelectedOrder.Id, SelectedOrder.Version, requests, token); await LoadSelectedOrderAsync(SelectedOrder);
	}

	private async Task ReleaseAsync(CancellationToken token) { if (SelectedOrder is null) return; SelectedOrder = await _orders.ReleaseAsync(SelectedOrder.Id, SelectedOrder.Version, token); await LoadAsync(token); }
	private async Task CreateShipmentAsync(CancellationToken token) { if (SelectedOrder is null || SelectedReservation is null) return; SelectedShipment = await _shipments.CreateAsync(SelectedOrder.Id, [new ShipmentLineRequest(SelectedReservation.Id, ShipmentQuantity)], Carrier, TrackingNumber, ShipmentNotes, token); Section = SalesSection.Shipping; await LoadAsync(token); }
	private async Task SaveShipmentAsync(CancellationToken token) { if (SelectedShipment is null) return; var draft = Copy(SelectedShipment); draft.Carrier = Carrier; draft.TrackingNumber = TrackingNumber; draft.Notes = ShipmentNotes; SelectedShipment = await _shipments.UpdateDraftAsync(draft, token); await LoadAsync(token); }
	private async Task PostShipmentAsync(CancellationToken token) { if (SelectedShipment is null) return; SelectedShipment = await _shipments.PostAsync(SelectedShipment.Id, SelectedShipment.Version, token); await LoadAsync(token); }
	private async Task ReverseShipmentAsync(CancellationToken token) { if (SelectedShipment is null) return; SelectedShipment = await _shipments.ReverseAsync(SelectedShipment.Id, SelectedShipment.Version, CorrectionReason, token); CorrectionReason = string.Empty; await LoadAsync(token); }
	private async Task CreateCustomerReturnAsync(CancellationToken token) { if (SelectedShipment is null) return; SelectedCustomerReturn = await _shipments.CreateCustomerReturnAsync(SelectedShipment.Id, CorrectionReason, token); CorrectionReason = string.Empty; await LoadAsync(token); }
	private async Task PostCustomerReturnAsync(CancellationToken token) { if (SelectedCustomerReturn is null) return; SelectedCustomerReturn = await _shipments.PostCustomerReturnAsync(SelectedCustomerReturn.Id, SelectedCustomerReturn.Version, token); await LoadAsync(token); }
	private async Task CreateInvoiceAsync(CancellationToken token) { if (SelectedShipment is null) return; SelectedInvoice = await _invoices.CreateFromShipmentAsync(SelectedShipment.Id, token); Section = SalesSection.Invoices; await LoadAsync(token); }
	private async Task CancelInvoiceAsync(CancellationToken token) { if (SelectedInvoice is null) return; SelectedInvoice = await _invoices.CancelDraftAsync(SelectedInvoice.Id, SelectedInvoice.Version, token); await LoadAsync(token); }
	private async Task PostInvoiceAsync(CancellationToken token) { if (SelectedInvoice is null) return; SelectedInvoice = await _invoices.PostAsync(SelectedInvoice.Id, SelectedInvoice.Version, token); await LoadAsync(token); }
	private async Task CreateCreditNoteAsync(CancellationToken token) { if (SelectedInvoice is null) return; SelectedCreditNote = await _invoices.CreateCreditNoteAsync(SelectedInvoice.Id, CorrectionReason, token); CorrectionReason = string.Empty; await LoadAsync(token); }
	private async Task PostCreditNoteAsync(CancellationToken token) { if (SelectedCreditNote is null) return; SelectedCreditNote = await _invoices.PostCreditNoteAsync(SelectedCreditNote.Id, SelectedCreditNote.Version, token); await LoadAsync(token); }

	private async Task LoadSelectedCustomerAsync(Customer? customer)
	{
		if (customer is null) { _customerBaseline = null; CustomerDraft = NewCustomerDraft(); return; }
		try { var loaded = await _customers.GetByIdAsync(customer.Id) ?? customer; _selectedCustomer = loaded; OnPropertyChanged(nameof(SelectedCustomer)); _customerBaseline = Copy(loaded); CustomerDraft = Copy(loaded); }
		catch (Exception exception) { FailOperation(exception, "Customer details could not be loaded"); }
	}

	private async Task LoadOrderAddressesAsync(Customer? customer, bool applyDefaults)
	{
		BillingAddresses.Clear(); ShippingAddresses.Clear();
		if (customer is null) return;
		try
		{
			var loaded = await _customers.GetByIdAsync(customer.Id) ?? customer;
			_selectedOrderCustomer = loaded; OnPropertyChanged(nameof(SelectedOrderCustomer));
			OrderDraft.CustomerId = loaded.Id; OrderDraft.CustomerName = loaded.Name;
			var addresses = loaded.Addresses.Count > 0 ? loaded.Addresses : await _customers.ListAddressesAsync(loaded.Id);
			Replace(BillingAddresses, addresses.Where(a => a.IsActive && a.Type is CustomerAddressType.Billing or CustomerAddressType.Other));
			Replace(ShippingAddresses, addresses.Where(a => a.IsActive && a.Type is CustomerAddressType.Shipping or CustomerAddressType.Other));
			if (applyDefaults)
			{
				SelectedBillingAddress = BillingAddresses.FirstOrDefault(a => a.IsDefault) ?? BillingAddresses.FirstOrDefault();
				SelectedShippingAddress = ShippingAddresses.FirstOrDefault(a => a.IsDefault) ?? ShippingAddresses.FirstOrDefault();
			}
		}
		catch (Exception exception) { FailOperation(exception, "Customer addresses could not be loaded"); }
	}

	private async Task LoadSelectedOrderAsync(SalesOrder? order)
	{
		if (order is null) { _orderBaseline = null; OrderDraft = NewOrderDraft(); Replace(OrderLines, []); Replace(Reservations, []); BillingAddresses.Clear(); ShippingAddresses.Clear(); return; }
		try
		{
			var loaded = await _orders.GetByIdAsync(order.Id) ?? order; _selectedOrder = loaded; OnPropertyChanged(nameof(SelectedOrder)); _orderBaseline = Copy(loaded); OrderDraft = Copy(loaded);
			Replace(OrderLines, loaded.Lines.Select(CopyLine)); Replace(Reservations, await _orders.GetReservationsAsync(loaded.Id)); SelectedOrderLine = OrderLines.FirstOrDefault(); SelectedReservation = Reservations.FirstOrDefault(value => value.Status == InventoryReservationStatus.Active);
			var customer = Customers.FirstOrDefault(c => c.Id == loaded.CustomerId) ?? await _customers.GetByIdAsync(loaded.CustomerId); _selectedOrderCustomer = customer; OnPropertyChanged(nameof(SelectedOrderCustomer)); await LoadOrderAddressesAsync(customer, false);
			_selectedBillingAddress = BillingAddresses.FirstOrDefault(a => string.Equals(a.Address, loaded.BillingAddress, StringComparison.Ordinal)) ?? BillingAddresses.FirstOrDefault(a => a.IsDefault); OnPropertyChanged(nameof(SelectedBillingAddress));
			_selectedShippingAddress = ShippingAddresses.FirstOrDefault(a => string.Equals(a.Address, loaded.ShippingAddress, StringComparison.Ordinal)) ?? ShippingAddresses.FirstOrDefault(a => a.IsDefault); OnPropertyChanged(nameof(SelectedShippingAddress));
			RaiseCommands();
		}
		catch (Exception exception) { FailOperation(exception, "Sales order details could not be loaded"); }
	}

	private async Task LoadSelectedShipmentAsync(Shipment? shipment)
	{
		if (shipment is null) { _shipmentBaseline = null; Carrier = null; TrackingNumber = null; ShipmentNotes = null; return; }
		try { var loaded = await _shipments.GetByIdAsync(shipment.Id) ?? shipment; _selectedShipment = loaded; OnPropertyChanged(nameof(SelectedShipment)); _shipmentBaseline = Copy(loaded); Carrier = loaded.Carrier; TrackingNumber = loaded.TrackingNumber; ShipmentNotes = loaded.Notes; RaiseCommands(); }
		catch (Exception exception) { FailOperation(exception, "Shipment details could not be loaded"); }
	}

	private async Task LoadAvailabilityAsync(SalesOrderLine? line)
	{
		Availability.Clear(); SelectedAvailability = null;
		if (line is null || !_orders.CanEdit) return;
		try { Replace(Availability, await _orders.SearchAvailabilityAsync(line.ItemId)); SelectedAvailability = Availability.FirstOrDefault(value => value.AvailableQuantity > 0); }
		catch (Exception exception) { FailOperation(exception, "Inventory availability could not be loaded"); }
	}

	private void GenerateOrderConfirmation() { if (SelectedOrder is null) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save order confirmation", "PDF document (*.pdf)|*.pdf", ".pdf", $"{SelectedOrder.OrderNumber}-confirmation.pdf")); if (path is not null) _documents.CreateOrderConfirmation(path, SelectedOrder); }
	private void GenerateDeliveryNote() { if (SelectedShipment is null) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save delivery note", "PDF document (*.pdf)|*.pdf", ".pdf", $"{SelectedShipment.ShipmentNumber}-delivery-note.pdf")); if (path is not null) _documents.CreateDeliveryNote(path, SelectedShipment); }
	private void GenerateInvoice() { if (SelectedInvoice is null) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save sales invoice", "PDF document (*.pdf)|*.pdf", ".pdf", $"{SelectedInvoice.InvoiceNumber}.pdf")); if (path is not null) _documents.CreateInvoice(path, SelectedInvoice); }
	private void NotifyMetrics() { OnPropertyChanged(nameof(OpenOrdersCount)); OnPropertyChanged(nameof(PendingApprovalCount)); OnPropertyChanged(nameof(ReadyToShipCount)); OnPropertyChanged(nameof(DraftShipmentCount)); OnPropertyChanged(nameof(DraftInvoiceCount)); OnPropertyChanged(nameof(PostedRevenue)); }
	private void RaiseCommands() { SaveOrderCommand.RaiseCanExecuteChanged(); SubmitCommand.RaiseCanExecuteChanged(); ApproveCommand.RaiseCanExecuteChanged(); RejectCommand.RaiseCanExecuteChanged(); ReserveCommand.RaiseCanExecuteChanged(); ReleaseCommand.RaiseCanExecuteChanged(); CreateShipmentCommand.RaiseCanExecuteChanged(); SaveShipmentCommand.RaiseCanExecuteChanged(); PostShipmentCommand.RaiseCanExecuteChanged(); ReverseShipmentCommand.RaiseCanExecuteChanged(); CreateCustomerReturnCommand.RaiseCanExecuteChanged(); PostCustomerReturnCommand.RaiseCanExecuteChanged(); CreateInvoiceCommand.RaiseCanExecuteChanged(); CancelInvoiceCommand.RaiseCanExecuteChanged(); PostInvoiceCommand.RaiseCanExecuteChanged(); CreateCreditNoteCommand.RaiseCanExecuteChanged(); PostCreditNoteCommand.RaiseCanExecuteChanged(); OrderConfirmationCommand.RaiseCanExecuteChanged(); DeliveryNoteCommand.RaiseCanExecuteChanged(); InvoicePdfCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); }
	private static Customer NewCustomerDraft() => new() { Currency = "EUR", PaymentTermsDays = 30, IsActive = true };
	private static SalesOrder NewOrderDraft() => new() { OrderDate = DateTime.Today, Currency = "EUR", Status = SalesOrderStatus.Draft };
	private static Customer Copy(Customer value) => new()
	{
		Id = value.Id,
		CustomerNumber = value.CustomerNumber,
		Name = value.Name,
		BillingAddress = value.BillingAddress,
		ShippingAddress = value.ShippingAddress,
		ContactName = value.ContactName,
		Email = value.Email,
		Phone = value.Phone,
		TaxId = value.TaxId,
		VatId = value.VatId,
		BuyerReference = value.BuyerReference,
		EInvoiceEndpoint = value.EInvoiceEndpoint,
		EInvoiceEndpointScheme = value.EInvoiceEndpointScheme,
		BillingStreet = value.BillingStreet,
		BillingAddressLine2 = value.BillingAddressLine2,
		BillingPostalCode = value.BillingPostalCode,
		BillingCity = value.BillingCity,
		BillingCountryCode = value.BillingCountryCode,
		PaymentTermsDays = value.PaymentTermsDays,
		Currency = value.Currency,
		IsActive = value.IsActive,
		Version = value.Version,
		Addresses = value.Addresses.Select(CopyAddress).ToArray(),
		Contacts = value.Contacts.ToArray()
	};
	private static CustomerAddress CopyAddress(CustomerAddress value) => new() { Id=value.Id, CustomerId=value.CustomerId, Type=value.Type, Name=value.Name, Address=value.Address, IsDefault=value.IsDefault, IsActive=value.IsActive, Version=value.Version };
	private static SalesOrderLine CopyLine(SalesOrderLine line) => new() { Id=line.Id, SalesOrderId=line.SalesOrderId, LineNumber=line.LineNumber, ItemId=line.ItemId, PartNumber=line.PartNumber, Description=line.Description, Quantity=line.Quantity, UnitPrice=line.UnitPrice, DiscountPercent=line.DiscountPercent, TaxRate=line.TaxRate, ReservedQuantity=line.ReservedQuantity, ShippedQuantity=line.ShippedQuantity, InvoicedQuantity=line.InvoicedQuantity, Version=line.Version };
	private static SalesOrder Copy(SalesOrder value) => new() { Id = value.Id, OrderNumber = value.OrderNumber, CustomerId = value.CustomerId, CustomerName = value.CustomerName, BillingAddress=value.BillingAddress, ShippingAddress=value.ShippingAddress, OrderDate = value.OrderDate, RequestedDeliveryDate = value.RequestedDeliveryDate, Currency = value.Currency, CustomerReference = value.CustomerReference, Notes = value.Notes, Status = value.Status, CreatedByUserId = value.CreatedByUserId, SubmittedByUserId = value.SubmittedByUserId, SubmittedAtUtc = value.SubmittedAtUtc, ApprovalDecisionByUserId = value.ApprovalDecisionByUserId, ApprovalDecisionAtUtc = value.ApprovalDecisionAtUtc, ApprovalComment = value.ApprovalComment, ReleasedByUserId = value.ReleasedByUserId, ReleasedAtUtc = value.ReleasedAtUtc, CancelledByUserId = value.CancelledByUserId, CancelledAtUtc = value.CancelledAtUtc, CancelReason = value.CancelReason, Version = value.Version, Lines = value.Lines.Select(CopyLine).ToArray() };
	private static Shipment Copy(Shipment value) => new() { Id=value.Id, ShipmentNumber=value.ShipmentNumber, SalesOrderId=value.SalesOrderId, SalesOrderNumber=value.SalesOrderNumber, CustomerId=value.CustomerId, CustomerName=value.CustomerName, ShipmentDate=value.ShipmentDate, Status=value.Status, Carrier=value.Carrier, TrackingNumber=value.TrackingNumber, ShippingAddress=value.ShippingAddress, Notes=value.Notes, CreatedByUserId=value.CreatedByUserId, PostedByUserId=value.PostedByUserId, PostedAtUtc=value.PostedAtUtc, ReversedAtUtc=value.ReversedAtUtc, ReversedByUserId=value.ReversedByUserId, ReversalReason=value.ReversalReason, Version=value.Version, Lines=value.Lines.ToArray() };
	private static bool Equivalent<T>(T left, T right) => string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	public void Dispose() { _loadRequest.Dispose(); foreach (var command in new IDisposable[] { SaveCustomerCommand, SaveOrderCommand, SubmitCommand, ApproveCommand, RejectCommand, ReserveCommand, ReleaseCommand, CreateShipmentCommand, SaveShipmentCommand, PostShipmentCommand, ReverseShipmentCommand, CreateCustomerReturnCommand, PostCustomerReturnCommand, CreateInvoiceCommand, CancelInvoiceCommand, PostInvoiceCommand, CreateCreditNoteCommand, PostCreditNoteCommand }) command.Dispose(); }
}
