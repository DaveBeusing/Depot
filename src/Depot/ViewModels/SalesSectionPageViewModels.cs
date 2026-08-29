// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.ComponentModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public abstract class SalesSectionPageViewModel : BaseViewModel, IDisposable
{
	protected SalesSectionPageViewModel(SalesViewModel workspace, SalesSection section)
	{
		Workspace = workspace;
		WorkspaceState = SalesWorkspaceState.For(workspace);
		Section = section;
		WorkspaceState.Section = section;
	}

	public SalesViewModel Workspace { get; }
	public SalesWorkspaceState WorkspaceState { get; }
	public SalesSection Section { get; }
	public virtual Task LoadAsync(CancellationToken cancellationToken = default) { WorkspaceState.Section = Section; return Workspace.LoadAsync(cancellationToken); }
	public virtual Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
	public bool HasUnsavedChanges() { WorkspaceState.Section = Section; return Workspace.HasUnsavedChanges(); }
	public void DiscardUnsavedChanges() { WorkspaceState.Section = Section; Workspace.DiscardUnsavedChanges(); }
	public virtual void Dispose() { }
}

public sealed class SalesOverviewViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Overview);

public sealed class CustomersViewModel : SalesSectionPageViewModel
{
	private readonly CustomerService _customers;
	private Customer? _selectedCustomer;
	private CustomerAddress? _selectedAddress;
	private CustomerAddress _addressDraft = NewAddressDraft();
	private CustomerContact? _selectedContact;
	private CustomerContact _contactDraft = NewContactDraft();

	public CustomersViewModel(SalesViewModel workspace, CustomerService customers) : base(workspace, SalesSection.Customers)
	{
		_customers = customers;
		NewAddressCommand = new RelayCommand(NewAddress, () => WorkspaceState.SelectedCustomer is not null && Workspace.CanEditCustomers);
		SaveAddressCommand = new AsyncRelayCommand(SaveAddressAsync, () => WorkspaceState.SelectedCustomer is not null && Workspace.CanEditCustomers && !string.IsNullOrWhiteSpace(AddressDraft.Address));
		NewContactCommand = new RelayCommand(NewContact, () => WorkspaceState.SelectedCustomer is not null && Workspace.CanEditCustomers);
		SaveContactCommand = new AsyncRelayCommand(SaveContactAsync, () => WorkspaceState.SelectedCustomer is not null && Workspace.CanEditCustomers && !string.IsNullOrWhiteSpace(ContactDraft.Name));
	}

	public ObservableCollection<CustomerAddress> Addresses { get; } = [];
	public ObservableCollection<CustomerContact> Contacts { get; } = [];
	public IReadOnlyList<CustomerAddressType> AddressTypes { get; } = Enum.GetValues<CustomerAddressType>();
	public IReadOnlyList<CustomerContactRole> ContactRoles { get; } = Enum.GetValues<CustomerContactRole>();
	public RelayCommand NewAddressCommand { get; }
	public AsyncRelayCommand SaveAddressCommand { get; }
	public RelayCommand NewContactCommand { get; }
	public AsyncRelayCommand SaveContactCommand { get; }
	public string SearchText { get => WorkspaceState.SearchText; set { WorkspaceState.SearchText = value; OnPropertyChanged(); } }
	public Customer CustomerDraft => Workspace.CustomerDraft;
	public RelayCommand NewCustomerCommand => Workspace.NewCustomerCommand;
	public AsyncRelayCommand SaveCustomerCommand => Workspace.SaveCustomerCommand;

	public Customer? SelectedCustomer
	{
		get => _selectedCustomer;
		set { if (_selectedCustomer == value) return; _selectedCustomer = value; OnPropertyChanged(); _ = SelectCustomerAsync(value); }
	}
	public CustomerAddress? SelectedAddress
	{
		get => _selectedAddress;
		set { if (_selectedAddress == value) return; _selectedAddress = value; AddressDraft = value is null ? NewAddressDraft() : Copy(value); OnPropertyChanged(); }
	}
	public CustomerAddress AddressDraft { get => _addressDraft; private set { _addressDraft = value; OnPropertyChanged(); SaveAddressCommand.RaiseCanExecuteChanged(); } }
	public CustomerContact? SelectedContact
	{
		get => _selectedContact;
		set { if (_selectedContact == value) return; _selectedContact = value; ContactDraft = value is null ? NewContactDraft() : Copy(value); OnPropertyChanged(); }
	}
	public CustomerContact ContactDraft { get => _contactDraft; private set { _contactDraft = value; OnPropertyChanged(); SaveContactCommand.RaiseCanExecuteChanged(); } }

	public override async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		await base.LoadAsync(cancellationToken);
		if (SelectedCustomer is not null) await SelectCustomerAsync(SelectedCustomer, cancellationToken);
	}

	private async Task SelectCustomerAsync(Customer? customer, CancellationToken token = default)
	{
		WorkspaceState.SelectedCustomer = customer;
		if (customer is null) { Addresses.Clear(); Contacts.Clear(); RaiseCustomerCommands(); return; }
		await Workspace.OpenQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Customer, customer.Id, customer.Name, customer.CustomerNumber), token);
		var loaded = await _customers.GetByIdAsync(customer.Id, token) ?? customer;
		Replace(Addresses, loaded.Addresses);
		Replace(Contacts, loaded.Contacts);
		SelectedAddress = Addresses.FirstOrDefault(a => a.IsDefault) ?? Addresses.FirstOrDefault();
		SelectedContact = Contacts.FirstOrDefault(c => c.IsPrimary) ?? Contacts.FirstOrDefault();
		OnPropertyChanged(nameof(CustomerDraft));
		RaiseCustomerCommands();
	}

	private void NewAddress() { _selectedAddress = null; OnPropertyChanged(nameof(SelectedAddress)); AddressDraft = NewAddressDraft(); if (WorkspaceState.SelectedCustomer is { } customer) AddressDraft.CustomerId = customer.Id; }
	private async Task SaveAddressAsync(CancellationToken token) { if (WorkspaceState.SelectedCustomer is not { } customer) return; AddressDraft.CustomerId = customer.Id; var saved = await Workspace.SaveCustomerAddressAsync(AddressDraft, token); await SelectCustomerAsync(customer, token); SelectedAddress = Addresses.FirstOrDefault(a => a.Id == saved.Id); }
	private void NewContact() { _selectedContact = null; OnPropertyChanged(nameof(SelectedContact)); ContactDraft = NewContactDraft(); if (WorkspaceState.SelectedCustomer is { } customer) ContactDraft.CustomerId = customer.Id; }
	private async Task SaveContactAsync(CancellationToken token) { if (WorkspaceState.SelectedCustomer is not { } customer) return; ContactDraft.CustomerId = customer.Id; var saved = await _customers.SaveContactAsync(ContactDraft, token); await SelectCustomerAsync(customer, token); SelectedContact = Contacts.FirstOrDefault(c => c.Id == saved.Id); }
	private void RaiseCustomerCommands() { NewAddressCommand.RaiseCanExecuteChanged(); SaveAddressCommand.RaiseCanExecuteChanged(); NewContactCommand.RaiseCanExecuteChanged(); SaveContactCommand.RaiseCanExecuteChanged(); }
	private static CustomerAddress NewAddressDraft() => new() { Type = CustomerAddressType.Shipping, IsActive = true };
	private static CustomerContact NewContactDraft() => new() { Role = CustomerContactRole.General, IsActive = true };
	private static CustomerAddress Copy(CustomerAddress v) => new() { Id = v.Id, CustomerId = v.CustomerId, Type = v.Type, Name = v.Name, Address = v.Address, IsDefault = v.IsDefault, IsActive = v.IsActive, Version = v.Version };
	private static CustomerContact Copy(CustomerContact v) => new() { Id = v.Id, CustomerId = v.CustomerId, Name = v.Name, Role = v.Role, Department = v.Department, Email = v.Email, Phone = v.Phone, Mobile = v.Mobile, IsPrimary = v.IsPrimary, IsActive = v.IsActive, Version = v.Version };
	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	public override void Dispose() { SaveAddressCommand.Dispose(); SaveContactCommand.Dispose(); }
}

public sealed class SalesOrdersViewModel : SalesSectionPageViewModel
{
	private readonly SalesPricingService _pricing;
	private readonly SalesTimelineService _timeline;
	private SalesOrder? _selectedOrder;

	public SalesOrdersViewModel(SalesViewModel workspace, SalesPricingService pricing, SalesTimelineService timeline) : base(workspace, SalesSection.SalesOrders)
	{
		_pricing = pricing;
		_timeline = timeline;
		ApplyCustomerPriceCommand = new AsyncRelayCommand(ApplyCustomerPriceAsync, () => Workspace.SelectedOrderCustomer is not null && Workspace.SelectedItem is not null);
	}

	public ObservableCollection<SalesOrderTimelineItem> Timeline { get; } = [];
	public AsyncRelayCommand ApplyCustomerPriceCommand { get; }
	public SalesOrder? SelectedOrder { get => _selectedOrder; set { if (_selectedOrder == value) return; _selectedOrder = value; WorkspaceState.SelectedOrder = value; OnPropertyChanged(); _ = LoadTimelineAsync(value); } }
	public override async Task LoadAsync(CancellationToken cancellationToken = default) { await base.LoadAsync(cancellationToken); if (WorkspaceState.SelectedOrder is { } order) { _selectedOrder = order; OnPropertyChanged(nameof(SelectedOrder)); await LoadTimelineAsync(order, cancellationToken); } ApplyCustomerPriceCommand.RaiseCanExecuteChanged(); }
	private async Task LoadTimelineAsync(SalesOrder? order, CancellationToken token = default) { Timeline.Clear(); if (order is null) return; foreach (var item in await _timeline.ListAsync(order, token)) Timeline.Add(item); }
	private async Task ApplyCustomerPriceAsync(CancellationToken token) { if (Workspace.SelectedOrderCustomer is null || Workspace.SelectedItem is null) return; var price = await _pricing.ResolveAsync(Workspace.SelectedOrderCustomer.Id, Workspace.SelectedItem.Id, Workspace.LineQuantity, Workspace.OrderDraft.OrderDate, Workspace.OrderDraft.Currency, token); if (price is null) return; Workspace.LineUnitPrice = price.UnitPrice; Workspace.LineDiscountPercent = price.DiscountPercent; }
	public override void Dispose() => ApplyCustomerPriceCommand.Dispose();
}

public sealed class SalesApprovalsViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Approvals);

public sealed class ShippingViewModel : SalesSectionPageViewModel
{
	private readonly ShipmentPackingService _packing;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;

	public ShippingViewModel(SalesViewModel workspace, ShipmentPackingService packing, IFileDialogService fileDialogs, SalesDocumentService documents) : base(workspace, SalesSection.Shipping)
	{
		_packing = packing;
		_fileDialogs = fileDialogs;
		_documents = documents;
		StartPickingCommand = new AsyncRelayCommand(ct => SetPackingAsync(ShipmentPackingStatus.Picking, ct), () => _packing.CanPack && WorkspaceState.SelectedShipment?.Status == ShipmentStatus.Draft);
		MarkPackedCommand = new AsyncRelayCommand(ct => SetPackingAsync(ShipmentPackingStatus.Packed, ct), () => _packing.CanPack && WorkspaceState.SelectedShipment?.Status == ShipmentStatus.Draft);
		ResetPackingCommand = new AsyncRelayCommand(ct => SetPackingAsync(ShipmentPackingStatus.NotStarted, ct), () => _packing.CanPack && WorkspaceState.SelectedShipment?.Status == ShipmentStatus.Draft);
		PickListPdfCommand = new RelayCommand(CreatePickList, () => WorkspaceState.SelectedShipment is not null);
		PackingSlipPdfCommand = new RelayCommand(CreatePackingSlip, () => WorkspaceState.SelectedShipment is not null);
	}

	public AsyncRelayCommand StartPickingCommand { get; }
	public AsyncRelayCommand MarkPackedCommand { get; }
	public AsyncRelayCommand ResetPackingCommand { get; }
	public RelayCommand PickListPdfCommand { get; }
	public RelayCommand PackingSlipPdfCommand { get; }
	private async Task SetPackingAsync(ShipmentPackingStatus status, CancellationToken token) { if (WorkspaceState.SelectedShipment is null) return; WorkspaceState.SelectedShipment = await _packing.SetStatusAsync(WorkspaceState.SelectedShipment.Id, WorkspaceState.SelectedShipment.Version, status, token); Raise(); }
	private void CreatePickList() { if (WorkspaceState.SelectedShipment is not { } shipment) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save pick list", "PDF document (*.pdf)|*.pdf", ".pdf", $"{shipment.ShipmentNumber}-pick-list.pdf")); if (path is not null) _documents.CreatePickList(path, shipment); }
	private void CreatePackingSlip() { if (WorkspaceState.SelectedShipment is not { } shipment) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save packing slip", "PDF document (*.pdf)|*.pdf", ".pdf", $"{shipment.ShipmentNumber}-packing-slip.pdf")); if (path is not null) _documents.CreatePackingSlip(path, shipment); }
	private void Raise() { StartPickingCommand.RaiseCanExecuteChanged(); MarkPackedCommand.RaiseCanExecuteChanged(); ResetPackingCommand.RaiseCanExecuteChanged(); PickListPdfCommand.RaiseCanExecuteChanged(); PackingSlipPdfCommand.RaiseCanExecuteChanged(); }
	public override async Task LoadAsync(CancellationToken cancellationToken = default) { await base.LoadAsync(cancellationToken); Raise(); }
	public override void Dispose() { StartPickingCommand.Dispose(); MarkPackedCommand.Dispose(); ResetPackingCommand.Dispose(); }
}

public sealed class SalesInvoicesViewModel : SalesSectionPageViewModel
{
	private readonly SalesInvoiceService _invoices;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;
	private readonly SalesDocumentEmailService _email;
	private SalesInvoiceLine? _selectedInvoiceLine;
	private int _creditQuantity = 1;

	public SalesInvoicesViewModel(SalesViewModel workspace, SalesInvoiceService invoices, IFileDialogService fileDialogs, SalesDocumentService documents, SalesDocumentEmailService email) : base(workspace, SalesSection.Invoices)
	{
		_invoices = invoices;
		_fileDialogs = fileDialogs;
		_documents = documents;
		_email = email;
		CreatePartialCreditNoteCommand = new AsyncRelayCommand(CreatePartialCreditNoteAsync, CanCreatePartialCreditNote);
		CreditNotePdfCommand = new RelayCommand(CreateCreditNotePdf, () => WorkspaceState.SelectedCreditNote is not null && WorkspaceState.SelectedInvoice is not null);
		InvoiceEmailCommand = new RelayCommand(CreateInvoiceEmail, () => WorkspaceState.SelectedInvoice is not null);
		XRechnungXmlCommand = new RelayCommand(ExportXRechnung, () => WorkspaceState.SelectedInvoice?.Status == SalesInvoiceStatus.Posted);
		Workspace.PropertyChanged += OnWorkspacePropertyChanged;
	}

	public SalesInvoiceLine? SelectedInvoiceLine { get => _selectedInvoiceLine; set { if (_selectedInvoiceLine == value) return; _selectedInvoiceLine = value; OnPropertyChanged(); CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); } }
	public int CreditQuantity { get => _creditQuantity; set { if (_creditQuantity == value) return; _creditQuantity = value; OnPropertyChanged(); CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); } }
	public decimal CreditedGrossAmount => WorkspaceState.SelectedInvoice is null ? 0m : Workspace.CreditNotes.Where(note => note.SalesInvoiceId == WorkspaceState.SelectedInvoice.Id && note.Status == SalesCreditNoteStatus.Posted).Sum(note => note.GrossAmount);
	public decimal EffectiveGrossAmount => Math.Max(0m, (WorkspaceState.SelectedInvoice?.GrossAmount ?? 0m) - CreditedGrossAmount);
	public AsyncRelayCommand CreatePartialCreditNoteCommand { get; }
	public RelayCommand CreditNotePdfCommand { get; }
	public RelayCommand InvoiceEmailCommand { get; }
	public RelayCommand XRechnungXmlCommand { get; }
	public override async Task LoadAsync(CancellationToken cancellationToken = default) { await base.LoadAsync(cancellationToken); SelectedInvoiceLine = WorkspaceState.SelectedInvoice?.Lines.FirstOrDefault(); OnPropertyChanged(nameof(CreditedGrossAmount)); OnPropertyChanged(nameof(EffectiveGrossAmount)); RaiseInvoiceCommands(); }
	private bool CanCreatePartialCreditNote() => _invoices.CanCreateCreditNote && WorkspaceState.SelectedInvoice?.Status == SalesInvoiceStatus.Posted && SelectedInvoiceLine is not null && CreditQuantity > 0 && CreditQuantity <= SelectedInvoiceLine.Quantity && !string.IsNullOrWhiteSpace(Workspace.CorrectionReason);
	private async Task CreatePartialCreditNoteAsync(CancellationToken token) { if (WorkspaceState.SelectedInvoice is null || SelectedInvoiceLine is null) return; WorkspaceState.SelectedCreditNote = await _invoices.CreateCreditNoteAsync(WorkspaceState.SelectedInvoice.Id, [new SalesCreditRequest(SelectedInvoiceLine.Id, CreditQuantity)], Workspace.CorrectionReason, token); Workspace.CorrectionReason = string.Empty; await LoadAsync(token); }
	private void CreateCreditNotePdf() { if (WorkspaceState.SelectedCreditNote is null || WorkspaceState.SelectedInvoice is null) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save credit note", "PDF document (*.pdf)|*.pdf", ".pdf", $"{WorkspaceState.SelectedCreditNote.CreditNoteNumber}.pdf")); if (path is not null) _documents.CreateCreditNote(path, WorkspaceState.SelectedCreditNote, WorkspaceState.SelectedInvoice); }
	private void CreateInvoiceEmail() { if (WorkspaceState.SelectedInvoice is not { } invoice) return; var pdf = Path.Combine(Path.GetTempPath(), $"{invoice.InvoiceNumber}-{Guid.NewGuid():N}.pdf"); _documents.CreateInvoice(pdf, invoice); var draft = _email.CreateDraft(pdf, null, $"Invoice {invoice.InvoiceNumber}", $"Please find invoice {invoice.InvoiceNumber} attached.\n\nDue date: {invoice.DueDate:d}"); _email.OpenDraft(draft); }
	private void ExportXRechnung() { if (WorkspaceState.SelectedInvoice is not { Status: SalesInvoiceStatus.Posted } invoice) return; var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Export XRechnung", "XML document (*.xml)|*.xml", ".xml", $"{invoice.InvoiceNumber}-xrechnung.xml")); if (path is not null) _documents.ExportXRechnung(path, invoice); }
	private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName != nameof(SalesViewModel.SelectedInvoice)) return; SelectedInvoiceLine = WorkspaceState.SelectedInvoice?.Lines.FirstOrDefault(); OnPropertyChanged(nameof(CreditedGrossAmount)); OnPropertyChanged(nameof(EffectiveGrossAmount)); RaiseInvoiceCommands(); }
	private void RaiseInvoiceCommands() { CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); CreditNotePdfCommand.RaiseCanExecuteChanged(); InvoiceEmailCommand.RaiseCanExecuteChanged(); XRechnungXmlCommand.RaiseCanExecuteChanged(); }
	public override void Dispose() { Workspace.PropertyChanged -= OnWorkspacePropertyChanged; CreatePartialCreditNoteCommand.Dispose(); }
}
