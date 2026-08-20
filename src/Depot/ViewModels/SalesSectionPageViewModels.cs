// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public abstract class SalesSectionPageViewModel : BaseViewModel, IDisposable
{
	protected SalesSectionPageViewModel(SalesViewModel workspace, SalesSection section)
	{
		Workspace = workspace;
		Section = section;
		Workspace.Section = section;
	}

	public SalesViewModel Workspace { get; }
	public SalesSection Section { get; }
	public virtual Task LoadAsync(CancellationToken cancellationToken = default) { Workspace.Section = Section; return Workspace.LoadAsync(cancellationToken); }
	public virtual Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
	public bool HasUnsavedChanges() { Workspace.Section = Section; return Workspace.HasUnsavedChanges(); }
	public void DiscardUnsavedChanges() { Workspace.Section = Section; Workspace.DiscardUnsavedChanges(); }
	public virtual void Dispose() => Workspace.Dispose();
}

public sealed class SalesOverviewViewModel : SalesSectionPageViewModel
{
	private int _selectedCommercialTab;

	public SalesOverviewViewModel(SalesViewModel workspace)
		: this(
			workspace,
			new SalesQuotesViewModel(SalesCommercialContext.Quotes, SalesCommercialContext.Pricing, SalesCommercialContext.Customers, SalesCommercialContext.Items, SalesCommercialContext.FileDialogs, SalesCommercialContext.Documents),
			new SalesPricingViewModel(SalesCommercialContext.Pricing, SalesCommercialContext.Customers, SalesCommercialContext.Items))
	{
	}

	public SalesOverviewViewModel(SalesViewModel workspace, SalesQuotesViewModel quotes, SalesPricingViewModel pricing)
		: base(workspace, SalesSection.Overview)
	{
		Quotes = quotes;
		Pricing = pricing;
	}

	public SalesQuotesViewModel Quotes { get; }
	public SalesPricingViewModel Pricing { get; }
	public bool CanViewQuotes => SalesCommercialContext.Quotes.CanView;
	public bool CanViewPricing => SalesCommercialContext.Pricing.CanView;
	public int SelectedCommercialTab { get => _selectedCommercialTab; set { if (_selectedCommercialTab == value) return; _selectedCommercialTab = value; OnPropertyChanged(); } }

	public override async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		await base.LoadAsync(cancellationToken);
		if (CanViewQuotes) await Quotes.LoadAsync(cancellationToken);
		if (CanViewPricing) await Pricing.LoadAsync(cancellationToken);
	}

	public override void Dispose()
	{
		Quotes.Dispose();
		Pricing.Dispose();
		base.Dispose();
	}
}

public sealed class CustomersViewModel : SalesSectionPageViewModel
{
	private readonly CustomerService _customers;
	private Customer? _selectedCustomer;
	private CustomerAddress? _selectedAddress;
	private CustomerAddress _addressDraft = NewAddressDraft();
	private CustomerContact? _selectedContact;
	private CustomerContact _contactDraft = NewContactDraft();

	public CustomersViewModel(SalesViewModel workspace) : this(workspace, SalesCommercialContext.Customers) { }

	public CustomersViewModel(SalesViewModel workspace, CustomerService customers) : base(workspace, SalesSection.Customers)
	{
		_customers = customers;
		NewAddressCommand = new RelayCommand(NewAddress, () => Workspace.SelectedCustomer is not null && Workspace.CanEditCustomers);
		SaveAddressCommand = new AsyncRelayCommand(SaveAddressAsync, () => Workspace.SelectedCustomer is not null && Workspace.CanEditCustomers && !string.IsNullOrWhiteSpace(AddressDraft.Address));
		NewContactCommand = new RelayCommand(NewContact, () => Workspace.SelectedCustomer is not null && Workspace.CanEditCustomers);
		SaveContactCommand = new AsyncRelayCommand(SaveContactAsync, () => Workspace.SelectedCustomer is not null && Workspace.CanEditCustomers && !string.IsNullOrWhiteSpace(ContactDraft.Name));
	}

	public ObservableCollection<CustomerAddress> Addresses { get; } = [];
	public ObservableCollection<CustomerContact> Contacts { get; } = [];
	public IReadOnlyList<CustomerAddressType> AddressTypes { get; } = Enum.GetValues<CustomerAddressType>();
	public IReadOnlyList<CustomerContactRole> ContactRoles { get; } = Enum.GetValues<CustomerContactRole>();
	public RelayCommand NewAddressCommand { get; }
	public AsyncRelayCommand SaveAddressCommand { get; }
	public RelayCommand NewContactCommand { get; }
	public AsyncRelayCommand SaveContactCommand { get; }
	public string SearchText { get => Workspace.SearchText; set { Workspace.SearchText = value; OnPropertyChanged(); } }
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
		set { if (_selectedContact == value) return; _selectedContact=value; ContactDraft=value is null?NewContactDraft():Copy(value); OnPropertyChanged(); }
	}
	public CustomerContact ContactDraft { get=>_contactDraft; private set { _contactDraft=value; OnPropertyChanged(); SaveContactCommand.RaiseCanExecuteChanged(); } }

	public override async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		await base.LoadAsync(cancellationToken);
		if (SelectedCustomer is not null) await SelectCustomerAsync(SelectedCustomer, cancellationToken);
	}

	private async Task SelectCustomerAsync(Customer? customer, CancellationToken token = default)
	{
		Workspace.SelectedCustomer = customer;
		if (customer is null) { Addresses.Clear(); Contacts.Clear(); RaiseCustomerCommands(); return; }
		await Workspace.OpenQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Customer, customer.Id, customer.Name, customer.CustomerNumber), token);
		var loaded=await _customers.GetByIdAsync(customer.Id,token)??customer;
		Replace(Addresses, loaded.Addresses); Replace(Contacts, loaded.Contacts);
		SelectedAddress = Addresses.FirstOrDefault(a => a.IsDefault) ?? Addresses.FirstOrDefault();
		SelectedContact = Contacts.FirstOrDefault(c => c.IsPrimary) ?? Contacts.FirstOrDefault();
		OnPropertyChanged(nameof(CustomerDraft)); RaiseCustomerCommands();
	}

	private void NewAddress(){_selectedAddress=null;OnPropertyChanged(nameof(SelectedAddress));AddressDraft=NewAddressDraft();if(Workspace.SelectedCustomer is { } customer)AddressDraft.CustomerId=customer.Id;}
	private async Task SaveAddressAsync(CancellationToken token){if(Workspace.SelectedCustomer is not { } customer)return;AddressDraft.CustomerId=customer.Id;var saved=await Workspace.SaveCustomerAddressAsync(AddressDraft,token);await SelectCustomerAsync(customer,token);SelectedAddress=Addresses.FirstOrDefault(a=>a.Id==saved.Id);}
	private void NewContact(){_selectedContact=null;OnPropertyChanged(nameof(SelectedContact));ContactDraft=NewContactDraft();if(Workspace.SelectedCustomer is { } customer)ContactDraft.CustomerId=customer.Id;}
	private async Task SaveContactAsync(CancellationToken token){if(Workspace.SelectedCustomer is not { } customer)return;ContactDraft.CustomerId=customer.Id;var saved=await _customers.SaveContactAsync(ContactDraft,token);await SelectCustomerAsync(customer,token);SelectedContact=Contacts.FirstOrDefault(c=>c.Id==saved.Id);}
	private void RaiseCustomerCommands(){NewAddressCommand.RaiseCanExecuteChanged();SaveAddressCommand.RaiseCanExecuteChanged();NewContactCommand.RaiseCanExecuteChanged();SaveContactCommand.RaiseCanExecuteChanged();}
	private static CustomerAddress NewAddressDraft()=>new(){Type=CustomerAddressType.Shipping,IsActive=true};
	private static CustomerContact NewContactDraft()=>new(){Role=CustomerContactRole.General,IsActive=true};
	private static CustomerAddress Copy(CustomerAddress v)=>new(){Id=v.Id,CustomerId=v.CustomerId,Type=v.Type,Name=v.Name,Address=v.Address,IsDefault=v.IsDefault,IsActive=v.IsActive,Version=v.Version};
	private static CustomerContact Copy(CustomerContact v)=>new(){Id=v.Id,CustomerId=v.CustomerId,Name=v.Name,Role=v.Role,Department=v.Department,Email=v.Email,Phone=v.Phone,Mobile=v.Mobile,IsPrimary=v.IsPrimary,IsActive=v.IsActive,Version=v.Version};
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var value in values)target.Add(value);}
	public override void Dispose(){SaveAddressCommand.Dispose();SaveContactCommand.Dispose();base.Dispose();}
}

public sealed class SalesOrdersViewModel : SalesSectionPageViewModel
{
	private readonly SalesPricingService _pricing;
	private readonly SalesTimelineService _timeline;
	private SalesOrder? _selectedOrder;

	public SalesOrdersViewModel(SalesViewModel workspace) : this(workspace, SalesCommercialContext.Pricing, SalesCommercialContext.Timeline) { }

	public SalesOrdersViewModel(SalesViewModel workspace, SalesPricingService pricing, SalesTimelineService timeline) : base(workspace, SalesSection.SalesOrders)
	{
		_pricing=pricing;_timeline=timeline;
		ApplyCustomerPriceCommand=new AsyncRelayCommand(ApplyCustomerPriceAsync,()=>Workspace.SelectedOrderCustomer is not null&&Workspace.SelectedItem is not null);
	}
	public ObservableCollection<SalesOrderTimelineItem> Timeline { get; }=[];
	public AsyncRelayCommand ApplyCustomerPriceCommand { get; }
	public SalesOrder? SelectedOrder { get=>_selectedOrder; set { if(_selectedOrder==value)return;_selectedOrder=value;Workspace.SelectedOrder=value;OnPropertyChanged();_ = LoadTimelineAsync(value); } }
	public override async Task LoadAsync(CancellationToken cancellationToken=default){await base.LoadAsync(cancellationToken);if(Workspace.SelectedOrder is { } order){_selectedOrder=order;OnPropertyChanged(nameof(SelectedOrder));await LoadTimelineAsync(order,cancellationToken);}ApplyCustomerPriceCommand.RaiseCanExecuteChanged();}
	private async Task LoadTimelineAsync(SalesOrder? order,CancellationToken token=default){Timeline.Clear();if(order is null)return;foreach(var item in await _timeline.ListAsync(order,token))Timeline.Add(item);}
	private async Task ApplyCustomerPriceAsync(CancellationToken token){if(Workspace.SelectedOrderCustomer is null||Workspace.SelectedItem is null)return;var price=await _pricing.ResolveAsync(Workspace.SelectedOrderCustomer.Id,Workspace.SelectedItem.Id,Workspace.OrderDraft.OrderDate,token);if(price is null)return;Workspace.LineUnitPrice=price.UnitPrice;Workspace.LineDiscountPercent=price.DiscountPercent;}
	public override void Dispose(){ApplyCustomerPriceCommand.Dispose();base.Dispose();}
}

public sealed class SalesApprovalsViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Approvals);

public sealed class ShippingViewModel : SalesSectionPageViewModel
{
	private readonly ShipmentPackingService _packing;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;

	public ShippingViewModel(SalesViewModel workspace) : this(workspace, SalesCommercialContext.Packing, SalesCommercialContext.FileDialogs, SalesCommercialContext.Documents) { }

	public ShippingViewModel(SalesViewModel workspace,ShipmentPackingService packing,IFileDialogService? fileDialogs=null,SalesDocumentService? documents=null):base(workspace,SalesSection.Shipping)
	{
		_packing=packing;
		_fileDialogs=fileDialogs??SalesCommercialContext.FileDialogs;
		_documents=documents??SalesCommercialContext.Documents;
		StartPickingCommand=new AsyncRelayCommand(ct=>SetPackingAsync(ShipmentPackingStatus.Picking,ct),()=>_packing.CanPack&&Workspace.SelectedShipment?.Status==ShipmentStatus.Draft);
		MarkPackedCommand=new AsyncRelayCommand(ct=>SetPackingAsync(ShipmentPackingStatus.Packed,ct),()=>_packing.CanPack&&Workspace.SelectedShipment?.Status==ShipmentStatus.Draft);
		ResetPackingCommand=new AsyncRelayCommand(ct=>SetPackingAsync(ShipmentPackingStatus.NotStarted,ct),()=>_packing.CanPack&&Workspace.SelectedShipment?.Status==ShipmentStatus.Draft);
		PickListPdfCommand=new RelayCommand(CreatePickList,()=>Workspace.SelectedShipment is not null);
		PackingSlipPdfCommand=new RelayCommand(CreatePackingSlip,()=>Workspace.SelectedShipment is not null);
	}
	public AsyncRelayCommand StartPickingCommand{get;}
	public AsyncRelayCommand MarkPackedCommand{get;}
	public AsyncRelayCommand ResetPackingCommand{get;}
	public RelayCommand PickListPdfCommand{get;}
	public RelayCommand PackingSlipPdfCommand{get;}
	private async Task SetPackingAsync(ShipmentPackingStatus status,CancellationToken token){if(Workspace.SelectedShipment is null)return;Workspace.SelectedShipment=await _packing.SetStatusAsync(Workspace.SelectedShipment.Id,Workspace.SelectedShipment.Version,status,token);Raise();}
	private void CreatePickList(){if(Workspace.SelectedShipment is not { } shipment)return;var path=_fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save pick list","PDF document (*.pdf)|*.pdf",".pdf",$"{shipment.ShipmentNumber}-pick-list.pdf"));if(path is not null)_documents.CreatePickList(path,shipment);}
	private void CreatePackingSlip(){if(Workspace.SelectedShipment is not { } shipment)return;var path=_fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save packing slip","PDF document (*.pdf)|*.pdf",".pdf",$"{shipment.ShipmentNumber}-packing-slip.pdf"));if(path is not null)_documents.CreatePackingSlip(path,shipment);}
	private void Raise(){StartPickingCommand.RaiseCanExecuteChanged();MarkPackedCommand.RaiseCanExecuteChanged();ResetPackingCommand.RaiseCanExecuteChanged();PickListPdfCommand.RaiseCanExecuteChanged();PackingSlipPdfCommand.RaiseCanExecuteChanged();}
	public override async Task LoadAsync(CancellationToken cancellationToken=default){await base.LoadAsync(cancellationToken);Raise();}
	public override void Dispose(){StartPickingCommand.Dispose();MarkPackedCommand.Dispose();ResetPackingCommand.Dispose();base.Dispose();}
}

public sealed class SalesInvoicesViewModel : SalesSectionPageViewModel
{
	private readonly SalesInvoiceService _invoices;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;
	private readonly SalesDocumentEmailService _email;
	private SalesInvoiceLine? _selectedInvoiceLine;
	private int _creditQuantity = 1;

	public SalesInvoicesViewModel(SalesViewModel workspace, SalesInvoiceService invoices, IFileDialogService fileDialogs, SalesDocumentService documents) : base(workspace, SalesSection.Invoices)
	{
		_invoices = invoices; _fileDialogs = fileDialogs; _documents = documents; _email=SalesCommercialContext.Email;
		CreatePartialCreditNoteCommand = new AsyncRelayCommand(CreatePartialCreditNoteAsync, CanCreatePartialCreditNote);
		CreditNotePdfCommand = new RelayCommand(CreateCreditNotePdf, () => Workspace.SelectedCreditNote is not null && Workspace.SelectedInvoice is not null);
		InvoiceEmailCommand = new RelayCommand(CreateInvoiceEmail, () => Workspace.SelectedInvoice is not null);
	}
	public SalesInvoiceLine? SelectedInvoiceLine { get => _selectedInvoiceLine; set { if (_selectedInvoiceLine == value) return; _selectedInvoiceLine = value; OnPropertyChanged(); CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); } }
	public int CreditQuantity { get => _creditQuantity; set { if (_creditQuantity == value) return; _creditQuantity = value; OnPropertyChanged(); CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); } }
	public decimal CreditedGrossAmount => Workspace.SelectedInvoice is null ? 0m : Workspace.CreditNotes.Where(note => note.SalesInvoiceId == Workspace.SelectedInvoice.Id && note.Status == SalesCreditNoteStatus.Posted).Sum(note => note.GrossAmount);
	public decimal EffectiveGrossAmount => Math.Max(0m, (Workspace.SelectedInvoice?.GrossAmount ?? 0m) - CreditedGrossAmount);
	public AsyncRelayCommand CreatePartialCreditNoteCommand { get; }
	public RelayCommand CreditNotePdfCommand { get; }
	public RelayCommand InvoiceEmailCommand { get; }
	public override async Task LoadAsync(CancellationToken cancellationToken = default){await base.LoadAsync(cancellationToken);SelectedInvoiceLine=Workspace.SelectedInvoice?.Lines.FirstOrDefault();OnPropertyChanged(nameof(CreditedGrossAmount));OnPropertyChanged(nameof(EffectiveGrossAmount));CreatePartialCreditNoteCommand.RaiseCanExecuteChanged();CreditNotePdfCommand.RaiseCanExecuteChanged();InvoiceEmailCommand.RaiseCanExecuteChanged();}
	private bool CanCreatePartialCreditNote()=>_invoices.CanCreateCreditNote&&Workspace.SelectedInvoice?.Status==SalesInvoiceStatus.Posted&&SelectedInvoiceLine is not null&&CreditQuantity>0&&CreditQuantity<=SelectedInvoiceLine.Quantity&&!string.IsNullOrWhiteSpace(Workspace.CorrectionReason);
	private async Task CreatePartialCreditNoteAsync(CancellationToken token){if(Workspace.SelectedInvoice is null||SelectedInvoiceLine is null)return;Workspace.SelectedCreditNote=await _invoices.CreateCreditNoteAsync(Workspace.SelectedInvoice.Id,[new SalesCreditRequest(SelectedInvoiceLine.Id,CreditQuantity)],Workspace.CorrectionReason,token);Workspace.CorrectionReason=string.Empty;await LoadAsync(token);}
	private void CreateCreditNotePdf(){if(Workspace.SelectedCreditNote is null||Workspace.SelectedInvoice is null)return;var path=_fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save credit note","PDF document (*.pdf)|*.pdf",".pdf",$"{Workspace.SelectedCreditNote.CreditNoteNumber}.pdf"));if(path is not null)_documents.CreateCreditNote(path,Workspace.SelectedCreditNote,Workspace.SelectedInvoice);}
	private void CreateInvoiceEmail(){if(Workspace.SelectedInvoice is not { } invoice)return;var pdf=Path.Combine(Path.GetTempPath(),$"{invoice.InvoiceNumber}-{Guid.NewGuid():N}.pdf");_documents.CreateInvoice(pdf,invoice);var draft=_email.CreateDraft(pdf,null,$"Invoice {invoice.InvoiceNumber}",$"Please find invoice {invoice.InvoiceNumber} attached.\n\nDue date: {invoice.DueDate:d}");_email.OpenDraft(draft);}
	public override void Dispose(){CreatePartialCreditNoteCommand.Dispose();base.Dispose();}
}
