// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed partial class SalesPricingViewModel
{
	private PriceListGenerationService? _priceListGeneration;
	private CategoryService? _bulkCategoriesService;
	private ManufacturerService? _bulkManufacturersService;
	private BulkPriceFilterType _bulkFilterType=BulkPriceFilterType.AllActiveItems;
	private BulkPriceApplyMode _bulkApplyMode=BulkPriceApplyMode.ReplaceCalculatedPrices;
	private BulkPricePricingMethod _bulkPricingMethod=BulkPricePricingMethod.PercentageMarkup;
	private CommercialRoundingStrategy _bulkRoundingStrategy=CommercialRoundingStrategy.CurrencyPrecision;
	private ItemReferenceData? _bulkCategory;
	private ItemReferenceData? _bulkManufacturer;
	private decimal _bulkMarkupPercentage;
	private decimal _bulkGrossMarginPercentage;
	private PriceListGenerationPreview? _bulkPreview;
	private BulkPricePreviewRow? _selectedBulkPreviewRow;
	private PricingExchangeRate? _selectedExchangeRate;
	private long _exchangeRateId;
	private long _exchangeRateVersion=1;
	private string _exchangeRateSourceCurrency="USD";
	private string _exchangeRateTargetCurrency="EUR";
	private DateTime _exchangeRateEffectiveDate=DateTime.Today;
	private string _exchangeRateSource=string.Empty;
	private decimal _exchangeRateValue;

	public SalesPricingViewModel(SalesPricingService pricing,CustomerService customers,ItemService items,CategoryService categories,ManufacturerService manufacturers,PriceListGenerationService priceListGeneration)
	{
		_pricing=pricing;_customers=customers;_items=items;_priceListGeneration=priceListGeneration;_bulkCategoriesService=categories;_bulkManufacturersService=manufacturers;
		NewPriceListCommand=new RelayCommand(()=>{SelectedPriceList=null;Draft=NewDraft();});SavePriceListCommand=new AsyncRelayCommand(SavePriceListAsync,()=>_pricing.CanManage&&!string.IsNullOrWhiteSpace(Draft.Name)&&!string.IsNullOrWhiteSpace(Draft.Code)&&(Draft.Scope!=SalesPriceListScope.Region||SelectedPriceListRegion is not null));SavePriceItemCommand=new AsyncRelayCommand(SavePriceItemAsync,()=>_pricing.CanManage&&SelectedPriceList is not null&&SelectedItem is not null&&UnitPrice>=0);DeletePriceItemCommand=new AsyncRelayCommand(DeletePriceItemAsync,()=>_pricing.CanManage&&SelectedPriceItem is not null);AssignCustomerCommand=new AsyncRelayCommand(AssignCustomerAsync,()=>_pricing.CanManage&&SelectedCustomer is not null&&SelectedPriceList?.Scope==SalesPriceListScope.Customer);InitializeScopedPricing();InitializePricingStrategyDesigner();
		CalculateBulkPreviewCommand=new AsyncRelayCommand(CalculateBulkPreviewAsync,CanCalculateBulkPreview);ApplyBulkPricesCommand=new AsyncRelayCommand(ApplyBulkPricesAsync,()=>BulkPreview is {ErrorCount:0}&&priceListGeneration.CanApply);NewExchangeRateCommand=new RelayCommand(NewExchangeRate,()=>priceListGeneration.CanManageExchangeRates);SaveExchangeRateCommand=new AsyncRelayCommand(SaveExchangeRateAsync,CanSaveExchangeRate);PropertyChanged+=OnBulkPropertyChanged;_ = LoadBulkReferenceDataAsync();
	}

	public ObservableCollection<ItemReferenceData> BulkCategories{get;}=[];
	public ObservableCollection<ItemReferenceData> BulkManufacturers{get;}=[];
	public ObservableCollection<BulkItemSelectionViewModel> BulkItemSelections{get;}=[];
	public ObservableCollection<BulkPricePreviewRow> BulkPreviewRows{get;}=[];
	public ObservableCollection<PricingExchangeRate> ExchangeRates{get;}=[];
	public IReadOnlyList<BulkPriceFilterType> BulkFilterTypes{get;}=Enum.GetValues<BulkPriceFilterType>();
	public IReadOnlyList<BulkPriceApplyMode> BulkApplyModes{get;}=Enum.GetValues<BulkPriceApplyMode>();
	public IReadOnlyList<BulkPricePricingMethod> BulkPricingMethods{get;}=Enum.GetValues<BulkPricePricingMethod>();
	public IReadOnlyList<CommercialRoundingStrategy> BulkRoundingStrategies{get;}=Enum.GetValues<CommercialRoundingStrategy>();
	public AsyncRelayCommand? CalculateBulkPreviewCommand{get;private set;}
	public AsyncRelayCommand? ApplyBulkPricesCommand{get;private set;}
	public RelayCommand? NewExchangeRateCommand{get;private set;}
	public AsyncRelayCommand? SaveExchangeRateCommand{get;private set;}
	public BulkPriceFilterType BulkFilterType{get=>_bulkFilterType;set{if(_bulkFilterType==value)return;_bulkFilterType=value;OnPropertyChanged();OnPropertyChanged(nameof(IsBulkCategoryFilter));OnPropertyChanged(nameof(IsBulkManufacturerFilter));OnPropertyChanged(nameof(IsBulkSelectedItemsFilter));InvalidateBulkPreview();}}
	public BulkPriceApplyMode BulkApplyMode{get=>_bulkApplyMode;set{if(_bulkApplyMode==value)return;_bulkApplyMode=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public BulkPricePricingMethod BulkPricingMethod{get=>_bulkPricingMethod;set{if(_bulkPricingMethod==value)return;_bulkPricingMethod=value;OnPropertyChanged();OnPropertyChanged(nameof(BulkPricingPercentage));InvalidateBulkPreview();}}
	public CommercialRoundingStrategy BulkRoundingStrategy{get=>_bulkRoundingStrategy;set{if(_bulkRoundingStrategy==value)return;_bulkRoundingStrategy=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public ItemReferenceData? BulkCategory{get=>_bulkCategory;set{if(_bulkCategory==value)return;_bulkCategory=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public ItemReferenceData? BulkManufacturer{get=>_bulkManufacturer;set{if(_bulkManufacturer==value)return;_bulkManufacturer=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public decimal BulkMarkupPercentage{get=>_bulkMarkupPercentage;set{if(_bulkMarkupPercentage==value)return;_bulkMarkupPercentage=value;OnPropertyChanged();if(BulkPricingMethod==BulkPricePricingMethod.PercentageMarkup)OnPropertyChanged(nameof(BulkPricingPercentage));InvalidateBulkPreview();}}
	public decimal BulkGrossMarginPercentage{get=>_bulkGrossMarginPercentage;set{if(_bulkGrossMarginPercentage==value)return;_bulkGrossMarginPercentage=value;OnPropertyChanged();if(BulkPricingMethod==BulkPricePricingMethod.TargetGrossMargin)OnPropertyChanged(nameof(BulkPricingPercentage));InvalidateBulkPreview();}}
	public decimal BulkPricingPercentage{get=>BulkPricingMethod==BulkPricePricingMethod.TargetGrossMargin?BulkGrossMarginPercentage:BulkMarkupPercentage;set{if(BulkPricingMethod==BulkPricePricingMethod.TargetGrossMargin)BulkGrossMarginPercentage=value;else BulkMarkupPercentage=value;}}
	public bool IsBulkCategoryFilter=>BulkFilterType==BulkPriceFilterType.Category;
	public bool IsBulkManufacturerFilter=>BulkFilterType==BulkPriceFilterType.Manufacturer;
	public bool IsBulkSelectedItemsFilter=>BulkFilterType==BulkPriceFilterType.SelectedItems;
	public PriceListGenerationPreview? BulkPreview{get=>_bulkPreview;private set{_bulkPreview=value;OnPropertyChanged();OnPropertyChanged(nameof(BulkPreviewSummary));ApplyBulkPricesCommand?.RaiseCanExecuteChanged();}}
	public string BulkPreviewSummary=>BulkPreview is null?"Calculate a preview before applying prices.":$"{BulkPreview.CreateCount} create · {BulkPreview.UpdateCount} update · {BulkPreview.SkipCount} skip · {BulkPreview.ErrorCount} errors";
	public BulkPricePreviewRow? SelectedBulkPreviewRow{get=>_selectedBulkPreviewRow;set{if(_selectedBulkPreviewRow==value)return;_selectedBulkPreviewRow=value;OnPropertyChanged();}}
	public PricingExchangeRate? SelectedExchangeRate{get=>_selectedExchangeRate;set{if(_selectedExchangeRate==value)return;_selectedExchangeRate=value;OnPropertyChanged();LoadExchangeRateDraft(value);}}
	public string ExchangeRateSourceCurrency{get=>_exchangeRateSourceCurrency;set{if(_exchangeRateSourceCurrency==value)return;_exchangeRateSourceCurrency=value;OnPropertyChanged();SaveExchangeRateCommand?.RaiseCanExecuteChanged();}}
	public string ExchangeRateTargetCurrency{get=>_exchangeRateTargetCurrency;set{if(_exchangeRateTargetCurrency==value)return;_exchangeRateTargetCurrency=value;OnPropertyChanged();SaveExchangeRateCommand?.RaiseCanExecuteChanged();}}
	public DateTime ExchangeRateEffectiveDate{get=>_exchangeRateEffectiveDate;set{if(_exchangeRateEffectiveDate==value)return;_exchangeRateEffectiveDate=value;OnPropertyChanged();}}
	public string ExchangeRateSource{get=>_exchangeRateSource;set{if(_exchangeRateSource==value)return;_exchangeRateSource=value;OnPropertyChanged();SaveExchangeRateCommand?.RaiseCanExecuteChanged();}}
	public decimal ExchangeRateValue{get=>_exchangeRateValue;set{if(_exchangeRateValue==value)return;_exchangeRateValue=value;OnPropertyChanged();SaveExchangeRateCommand?.RaiseCanExecuteChanged();}}

	private void OnBulkPropertyChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName is nameof(SelectedPriceList) or nameof(Draft))InvalidateBulkPreview();}
	private async Task LoadBulkReferenceDataAsync(CancellationToken token=default)
	{
		if(_bulkCategoriesService is null||_bulkManufacturersService is null||_priceListGeneration is null)return;try{var categoriesTask=_bulkCategoriesService.GetActiveAsync(token);var manufacturersTask=_bulkManufacturersService.GetActiveAsync(token);var ratesTask=_priceListGeneration.ListExchangeRatesAsync(token);var itemsTask=_items.SearchItemsAsync(string.Empty,1,5000,token);await Task.WhenAll(categoriesTask,manufacturersTask,ratesTask,itemsTask);Replace(BulkCategories,await categoriesTask);Replace(BulkManufacturers,await manufacturersTask);ExchangeRates.Clear();foreach(var rate in await ratesTask)ExchangeRates.Add(rate);var items=await itemsTask;BulkItemSelections.Clear();foreach(var item in items.Items.Where(i=>i.IsActive).OrderBy(i=>i.PartNumber))BulkItemSelections.Add(new BulkItemSelectionViewModel(item.Id,item.PartNumber,item.Description,InvalidateBulkPreview));}catch(OperationCanceledException)when(token.IsCancellationRequested){}
	}
	private bool CanCalculateBulkPreview()
	{
		if(_priceListGeneration?.CanPreview!=true)return false;if(BulkPricingMethod==BulkPricePricingMethod.PercentageMarkup&&BulkMarkupPercentage<0m)return false;if(BulkPricingMethod==BulkPricePricingMethod.TargetGrossMargin&&(BulkGrossMarginPercentage<0m||BulkGrossMarginPercentage>=100m))return false;if(SelectedPriceList is null&&(string.IsNullOrWhiteSpace(Draft.Code)||string.IsNullOrWhiteSpace(Draft.Name)))return false;if(BulkFilterType==BulkPriceFilterType.Category&&BulkCategory is null)return false;if(BulkFilterType==BulkPriceFilterType.Manufacturer&&BulkManufacturer is null)return false;if(BulkFilterType==BulkPriceFilterType.SelectedItems&&!BulkItemSelections.Any(i=>i.IsSelected))return false;return true;
	}
	private async Task CalculateBulkPreviewAsync(CancellationToken token)
	{
		if(_priceListGeneration is null)return;var request=new PriceListGenerationRequest{ExistingPriceListId=SelectedPriceList?.Id,NewPriceList=SelectedPriceList is null?Copy(Draft):null,FilterType=BulkFilterType,FilterId=BulkFilterType==BulkPriceFilterType.Category?BulkCategory?.Id:BulkFilterType==BulkPriceFilterType.Manufacturer?BulkManufacturer?.Id:null,SelectedItemIds=BulkItemSelections.Where(i=>i.IsSelected).Select(i=>i.ItemId).ToArray(),PricingMethod=BulkPricingMethod,MarkupPercentage=BulkMarkupPercentage,GrossMarginPercentage=BulkGrossMarginPercentage,RoundingStrategy=BulkRoundingStrategy,ApplyMode=BulkApplyMode,EffectiveDate=DateTime.Today};var preview=await _priceListGeneration.PreviewAsync(request,token);BulkPreview=preview;Replace(BulkPreviewRows,preview.Rows);CompleteOperation(false,"Bulk price preview calculated");
	}
	private async Task ApplyBulkPricesAsync(CancellationToken token)
	{
		if(_priceListGeneration is null||BulkPreview is null)return;var result=await _priceListGeneration.ApplyAsync(BulkPreview,token);CompleteOperation(false,$"Bulk pricing applied: {result.Created} created, {result.Updated} updated, {result.Skipped} skipped");BulkPreview=null;BulkPreviewRows.Clear();await LoadAsync(token);
	}
	private bool CanSaveExchangeRate()=>_priceListGeneration?.CanManageExchangeRates==true&&!string.IsNullOrWhiteSpace(ExchangeRateSourceCurrency)&&!string.IsNullOrWhiteSpace(ExchangeRateTargetCurrency)&&!string.IsNullOrWhiteSpace(ExchangeRateSource)&&ExchangeRateValue>0m;
	private void NewExchangeRate(){SelectedExchangeRate=null;_exchangeRateId=0;_exchangeRateVersion=1;ExchangeRateSourceCurrency="USD";ExchangeRateTargetCurrency=SelectedPriceList?.Currency??Draft.Currency;ExchangeRateEffectiveDate=DateTime.Today;ExchangeRateSource=string.Empty;ExchangeRateValue=0m;}
	private async Task SaveExchangeRateAsync(CancellationToken token)
	{
		if(_priceListGeneration is null)return;var saved=await _priceListGeneration.SaveExchangeRateAsync(new PricingExchangeRate{Id=_exchangeRateId,SourceCurrency=ExchangeRateSourceCurrency,TargetCurrency=ExchangeRateTargetCurrency,EffectiveDate=ExchangeRateEffectiveDate,RateSource=ExchangeRateSource,Rate=ExchangeRateValue,Version=_exchangeRateVersion},token);ExchangeRates.Clear();foreach(var rate in await _priceListGeneration.ListExchangeRatesAsync(token))ExchangeRates.Add(rate);SelectedExchangeRate=ExchangeRates.FirstOrDefault(rate=>rate.Id==saved.Id);InvalidateBulkPreview();CompleteOperation(false,"Pricing exchange rate saved");
	}
	private void LoadExchangeRateDraft(PricingExchangeRate? rate){if(rate is null)return;_exchangeRateId=rate.Id;_exchangeRateVersion=rate.Version;ExchangeRateSourceCurrency=rate.SourceCurrency;ExchangeRateTargetCurrency=rate.TargetCurrency;ExchangeRateEffectiveDate=rate.EffectiveDate;ExchangeRateSource=rate.RateSource;ExchangeRateValue=rate.Rate;}
	private void InvalidateBulkPreview(){BulkPreview=null;BulkPreviewRows.Clear();CalculateBulkPreviewCommand?.RaiseCanExecuteChanged();ApplyBulkPricesCommand?.RaiseCanExecuteChanged();}
	private void DisposeBulkPricing(){PropertyChanged-=OnBulkPropertyChanged;CalculateBulkPreviewCommand?.Dispose();ApplyBulkPricesCommand?.Dispose();SaveExchangeRateCommand?.Dispose();}
}

public sealed class BulkItemSelectionViewModel : BaseViewModel
{
	private readonly Action _changed;
	private bool _isSelected;
	public BulkItemSelectionViewModel(long itemId,string partNumber,string description,Action changed){ItemId=itemId;PartNumber=partNumber;Description=description;_changed=changed;}
	public long ItemId{get;} public string PartNumber{get;set;} public string Description{get;set;}
	public bool IsSelected{get=>_isSelected;set{if(_isSelected==value)return;_isSelected=value;OnPropertyChanged();_changed();}}
}
