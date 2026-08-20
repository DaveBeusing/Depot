// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class SalesPricingViewModel : BaseViewModel, IDisposable
{
	private readonly SalesPricingService _pricing;
	private readonly CustomerService _customers;
	private readonly ItemService _items;
	private SalesPriceList? _selectedPriceList;
	private Customer? _selectedCustomer;
	private Item? _selectedItem;
	private decimal _unitPrice;
	private decimal _discountPercent;
	private SalesPriceList _draft = NewDraft();

	public SalesPricingViewModel(SalesPricingService pricing,CustomerService customers,ItemService items)
	{
		_pricing=pricing;_customers=customers;_items=items;
		NewPriceListCommand=new RelayCommand(()=>{SelectedPriceList=null;Draft=NewDraft();});
		SavePriceListCommand=new AsyncRelayCommand(SavePriceListAsync,()=>_pricing.CanManage&&!string.IsNullOrWhiteSpace(Draft.Name)&&!string.IsNullOrWhiteSpace(Draft.Code));
		SavePriceItemCommand=new AsyncRelayCommand(SavePriceItemAsync,()=>_pricing.CanManage&&SelectedPriceList is not null&&SelectedItem is not null&&UnitPrice>=0);
		AssignCustomerCommand=new AsyncRelayCommand(AssignCustomerAsync,()=>_pricing.CanManage&&SelectedCustomer is not null);
	}
	public ObservableCollection<SalesPriceList> PriceLists{get;}=[];
	public ObservableCollection<SalesPriceListItem> PriceItems{get;}=[];
	public ObservableCollection<Customer> Customers{get;}=[];
	public ObservableCollection<Item> Items{get;}=[];
	public RelayCommand NewPriceListCommand{get;}
	public AsyncRelayCommand SavePriceListCommand{get;}
	public AsyncRelayCommand SavePriceItemCommand{get;}
	public AsyncRelayCommand AssignCustomerCommand{get;}
	public SalesPriceList Draft{get=>_draft;private set{_draft=value;OnPropertyChanged();SavePriceListCommand.RaiseCanExecuteChanged();}}
	public SalesPriceList? SelectedPriceList{get=>_selectedPriceList;set{if(_selectedPriceList==value)return;_selectedPriceList=value;Draft=value is null?NewDraft():Copy(value);Replace(PriceItems,value?.Items??[]);OnPropertyChanged();Raise();}}
	public Customer? SelectedCustomer{get=>_selectedCustomer;set{_selectedCustomer=value;OnPropertyChanged();AssignCustomerCommand.RaiseCanExecuteChanged();}}
	public Item? SelectedItem{get=>_selectedItem;set{_selectedItem=value;OnPropertyChanged();SavePriceItemCommand.RaiseCanExecuteChanged();}}
	public decimal UnitPrice{get=>_unitPrice;set{_unitPrice=value;OnPropertyChanged();SavePriceItemCommand.RaiseCanExecuteChanged();}}
	public decimal DiscountPercent{get=>_discountPercent;set{_discountPercent=value;OnPropertyChanged();}}

	public async Task LoadAsync(CancellationToken token=default)
	{
		BeginOperation("Loading sales pricing");
		try{Replace(PriceLists,await _pricing.ListAsync(token));Replace(Customers,await _customers.ListActiveAsync(token));Replace(Items,(await _items.SearchItemsAsync(string.Empty,1,200,token)).Items);if(SelectedPriceList is not null)SelectedPriceList=PriceLists.FirstOrDefault(p=>p.Id==SelectedPriceList.Id);CompleteOperation(false,"Sales pricing loaded");}catch(Exception ex){FailOperation(ex,"Sales pricing could not be loaded");}
	}
	private async Task SavePriceListAsync(CancellationToken token){SelectedPriceList=await _pricing.SaveAsync(Draft,token);await LoadAsync(token);}
	private async Task SavePriceItemAsync(CancellationToken token){if(SelectedPriceList is null||SelectedItem is null)return;await _pricing.SaveItemAsync(new SalesPriceListItem{SalesPriceListId=SelectedPriceList.Id,ItemId=SelectedItem.Id,UnitPrice=UnitPrice,DiscountPercent=DiscountPercent},token);await LoadAsync(token);}
	private async Task AssignCustomerAsync(CancellationToken token){if(SelectedCustomer is null)return;await _pricing.AssignCustomerAsync(SelectedCustomer.Id,SelectedPriceList?.Id,token);CompleteOperation(false,SelectedPriceList is null?"Customer price list cleared":$"{SelectedPriceList.Name} assigned to {SelectedCustomer.Name}");}
	private void Raise(){SavePriceListCommand.RaiseCanExecuteChanged();SavePriceItemCommand.RaiseCanExecuteChanged();AssignCustomerCommand.RaiseCanExecuteChanged();}
	private static SalesPriceList NewDraft()=>new(){Currency="EUR",IsActive=true};
	private static SalesPriceList Copy(SalesPriceList v)=>new(){Id=v.Id,Code=v.Code,Name=v.Name,Currency=v.Currency,ValidFrom=v.ValidFrom,ValidTo=v.ValidTo,IsActive=v.IsActive,Version=v.Version,Items=v.Items};
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var v in values)target.Add(v);}
	public void Dispose(){SavePriceListCommand.Dispose();SavePriceItemCommand.Dispose();AssignCustomerCommand.Dispose();}
}

public sealed class SalesQuotesViewModel : BaseViewModel, IDisposable
{
	private readonly SalesQuoteService _quotes;
	private readonly SalesPricingService _pricing;
	private readonly CustomerService _customers;
	private readonly ItemService _items;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;
	private SalesQuote? _selectedQuote;
	private Customer? _selectedCustomer;
	private CustomerContact? _selectedContact;
	private Item? _selectedItem;
	private int _quantity=1;
	private decimal _unitPrice;
	private decimal _discountPercent;
	private decimal _taxRate=19m;
	private SalesQuote _draft=NewDraft();

	public SalesQuotesViewModel(SalesQuoteService quotes,SalesPricingService pricing,CustomerService customers,ItemService items,IFileDialogService fileDialogs,SalesDocumentService documents)
	{
		_quotes=quotes;_pricing=pricing;_customers=customers;_items=items;_fileDialogs=fileDialogs;_documents=documents;
		NewQuoteCommand=new RelayCommand(NewQuote,()=>_quotes.CanCreate);
		SaveQuoteCommand=new AsyncRelayCommand(SaveQuoteAsync,()=>Draft.Status==SalesQuoteStatus.Draft&&(Draft.Id==0?_quotes.CanCreate:_quotes.CanEdit));
		AddLineCommand=new AsyncRelayCommand(AddLineAsync,()=>Draft.Status==SalesQuoteStatus.Draft&&SelectedItem is not null&&Quantity>0);
		RemoveLineCommand=new RelayCommand(RemoveLine,()=>Draft.Status==SalesQuoteStatus.Draft&&SelectedLine is not null);
		SendQuoteCommand=new AsyncRelayCommand(SendAsync,()=>_quotes.CanSend&&Draft.Id>0&&Draft.Status==SalesQuoteStatus.Draft);
		AcceptQuoteCommand=new AsyncRelayCommand(AcceptAsync,()=>_quotes.CanEdit&&Draft.Id>0&&Draft.Status==SalesQuoteStatus.Sent);
		RejectQuoteCommand=new AsyncRelayCommand(RejectAsync,()=>_quotes.CanEdit&&Draft.Id>0&&Draft.Status==SalesQuoteStatus.Sent);
		ConvertQuoteCommand=new AsyncRelayCommand(ConvertAsync,()=>_quotes.CanConvert&&Draft.Id>0&&Draft.Status is SalesQuoteStatus.Sent or SalesQuoteStatus.Accepted);
		QuotePdfCommand=new RelayCommand(CreatePdf,()=>Draft.Id>0);
	}
	public ObservableCollection<SalesQuote> Quotes{get;}=[];
	public ObservableCollection<Customer> Customers{get;}=[];
	public ObservableCollection<CustomerContact> Contacts{get;}=[];
	public ObservableCollection<Item> Items{get;}=[];
	public ObservableCollection<SalesQuoteLine> Lines{get;}=[];
	public RelayCommand NewQuoteCommand{get;}
	public AsyncRelayCommand SaveQuoteCommand{get;}
	public AsyncRelayCommand AddLineCommand{get;}
	public RelayCommand RemoveLineCommand{get;}
	public AsyncRelayCommand SendQuoteCommand{get;}
	public AsyncRelayCommand AcceptQuoteCommand{get;}
	public AsyncRelayCommand RejectQuoteCommand{get;}
	public AsyncRelayCommand ConvertQuoteCommand{get;}
	public RelayCommand QuotePdfCommand{get;}
	public string SearchText{get;set;}=string.Empty;
	public SalesQuote Draft{get=>_draft;private set{_draft=value;OnPropertyChanged();OnPropertyChanged(nameof(Title));Raise();}}
	public string Title=>Draft.Id==0?"New quote":Draft.QuoteNumber;
	public SalesQuoteLine? SelectedLine{get;set;}
	public SalesQuote? SelectedQuote{get=>_selectedQuote;set{if(_selectedQuote==value)return;_selectedQuote=value;OnPropertyChanged();_ = LoadQuoteAsync(value);}}
	public Customer? SelectedCustomer{get=>_selectedCustomer;set{if(_selectedCustomer==value)return;_selectedCustomer=value;OnPropertyChanged();_ = LoadCustomerContextAsync(value);}}
	public CustomerContact? SelectedContact{get=>_selectedContact;set{_selectedContact=value;Draft.ContactId=value?.Id;Draft.ContactName=value?.Name;OnPropertyChanged();}}
	public Item? SelectedItem{get=>_selectedItem;set{_selectedItem=value;OnPropertyChanged();AddLineCommand.RaiseCanExecuteChanged();}}
	public int Quantity{get=>_quantity;set{_quantity=value;OnPropertyChanged();AddLineCommand.RaiseCanExecuteChanged();}}
	public decimal UnitPrice{get=>_unitPrice;set{_unitPrice=value;OnPropertyChanged();}}
	public decimal DiscountPercent{get=>_discountPercent;set{_discountPercent=value;OnPropertyChanged();}}
	public decimal TaxRate{get=>_taxRate;set{_taxRate=value;OnPropertyChanged();}}

	public async Task LoadAsync(CancellationToken token=default)
	{
		BeginOperation("Loading quotes");
		try{Replace(Quotes,(await _quotes.SearchAsync(SearchText,null,1,100,token)).Items);Replace(Customers,await _customers.ListActiveAsync(token));Replace(Items,(await _items.SearchItemsAsync(string.Empty,1,200,token)).Items);CompleteOperation(false,"Quotes loaded");}catch(Exception ex){FailOperation(ex,"Quotes could not be loaded");}
	}
	private void NewQuote(){_selectedQuote=null;OnPropertyChanged(nameof(SelectedQuote));_selectedCustomer=null;Contacts.Clear();Lines.Clear();Draft=NewDraft();}
	private async Task LoadQuoteAsync(SalesQuote? value){if(value is null)return;var loaded=await _quotes.GetByIdAsync(value.Id)??value;Draft=Copy(loaded);Replace(Lines,loaded.Lines.Select(Copy));SelectedCustomer=Customers.FirstOrDefault(c=>c.Id==loaded.CustomerId);}
	private async Task LoadCustomerContextAsync(Customer? customer){Contacts.Clear();if(customer is null)return;var loaded=await _customers.GetByIdAsync(customer.Id)??customer;Replace(Contacts,loaded.Contacts);SelectedContact=Contacts.FirstOrDefault(c=>c.IsPrimary)??Contacts.FirstOrDefault();Draft.CustomerId=loaded.Id;Draft.CustomerName=loaded.Name;Draft.Currency=loaded.Currency;Draft.BillingAddress=loaded.Addresses.FirstOrDefault(a=>a.Type==CustomerAddressType.Billing&&a.IsDefault)?.Address??loaded.BillingAddress;Draft.ShippingAddress=loaded.Addresses.FirstOrDefault(a=>a.Type==CustomerAddressType.Shipping&&a.IsDefault)?.Address??loaded.ShippingAddress;}
	private async Task AddLineAsync(CancellationToken token){if(SelectedItem is null)return;var price=SelectedCustomer is null?null:await _pricing.ResolveAsync(SelectedCustomer.Id,SelectedItem.Id,Draft.QuoteDate,token);var line=new SalesQuoteLine{LineNumber=Lines.Count+1,ItemId=SelectedItem.Id,PartNumber=SelectedItem.PartNumber,Description=SelectedItem.Description,Quantity=Quantity,UnitPrice=price?.UnitPrice??UnitPrice,DiscountPercent=price?.DiscountPercent??DiscountPercent,TaxRate=TaxRate};Lines.Add(line);Draft.Lines=Lines.ToArray();SelectedLine=line;OnPropertyChanged(nameof(Draft));}
	private void RemoveLine(){if(SelectedLine is null)return;Lines.Remove(SelectedLine);for(var i=0;i<Lines.Count;i++)Lines[i].LineNumber=i+1;Draft.Lines=Lines.ToArray();SelectedLine=null;OnPropertyChanged(nameof(Draft));}
	private async Task SaveQuoteAsync(CancellationToken token){Draft.Lines=Lines.ToArray();var saved=await _quotes.SaveDraftAsync(Draft,token);Draft=Copy(saved);Replace(Lines,saved.Lines.Select(Copy));await LoadAsync(token);}
	private async Task SendAsync(CancellationToken token){Draft=Copy(await _quotes.MarkSentAsync(Draft.Id,Draft.Version,token));await LoadAsync(token);}
	private async Task AcceptAsync(CancellationToken token){Draft=Copy(await _quotes.AcceptAsync(Draft.Id,Draft.Version,token));await LoadAsync(token);}
	private async Task RejectAsync(CancellationToken token){Draft=Copy(await _quotes.RejectAsync(Draft.Id,Draft.Version,token));await LoadAsync(token);}
	private async Task ConvertAsync(CancellationToken token){var order=await _quotes.ConvertToSalesOrderAsync(Draft.Id,Draft.Version,token);CompleteOperation(false,$"Converted to sales order {order.OrderNumber}");Draft=Copy(await _quotes.GetByIdAsync(Draft.Id,token)??Draft);await LoadAsync(token);}
	private void CreatePdf(){var path=_fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save quote","PDF document (*.pdf)|*.pdf",".pdf",$"{Draft.QuoteNumber}.pdf"));if(path is not null)_documents.CreateQuote(path,Draft);}
	private void Raise(){SaveQuoteCommand.RaiseCanExecuteChanged();AddLineCommand.RaiseCanExecuteChanged();RemoveLineCommand.RaiseCanExecuteChanged();SendQuoteCommand.RaiseCanExecuteChanged();AcceptQuoteCommand.RaiseCanExecuteChanged();RejectQuoteCommand.RaiseCanExecuteChanged();ConvertQuoteCommand.RaiseCanExecuteChanged();QuotePdfCommand.RaiseCanExecuteChanged();}
	private static SalesQuote NewDraft()=>new(){QuoteDate=DateTime.Today,ValidUntil=DateTime.Today.AddDays(30),Currency="EUR",Status=SalesQuoteStatus.Draft};
	private static SalesQuote Copy(SalesQuote v)=>new(){Id=v.Id,QuoteNumber=v.QuoteNumber,CustomerId=v.CustomerId,CustomerName=v.CustomerName,BillingAddress=v.BillingAddress,ShippingAddress=v.ShippingAddress,ContactId=v.ContactId,ContactName=v.ContactName,QuoteDate=v.QuoteDate,ValidUntil=v.ValidUntil,Currency=v.Currency,CustomerReference=v.CustomerReference,Notes=v.Notes,Status=v.Status,CreatedByUserId=v.CreatedByUserId,CreatedAtUtc=v.CreatedAtUtc,ConvertedSalesOrderId=v.ConvertedSalesOrderId,ConvertedAtUtc=v.ConvertedAtUtc,Version=v.Version,Lines=v.Lines.Select(Copy).ToArray()};
	private static SalesQuoteLine Copy(SalesQuoteLine v)=>new(){Id=v.Id,SalesQuoteId=v.SalesQuoteId,LineNumber=v.LineNumber,ItemId=v.ItemId,PartNumber=v.PartNumber,Description=v.Description,Quantity=v.Quantity,UnitPrice=v.UnitPrice,DiscountPercent=v.DiscountPercent,TaxRate=v.TaxRate,Version=v.Version};
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var v in values)target.Add(v);}
	public void Dispose(){SaveQuoteCommand.Dispose();AddLineCommand.Dispose();SendQuoteCommand.Dispose();AcceptQuoteCommand.Dispose();RejectQuoteCommand.Dispose();ConvertQuoteCommand.Dispose();}
}
