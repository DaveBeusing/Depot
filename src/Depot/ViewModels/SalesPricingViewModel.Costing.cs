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
	private ItemReferenceData? _bulkCategory;
	private ItemReferenceData? _bulkManufacturer;
	private decimal _bulkMarkupPercentage;
	private PriceListGenerationPreview? _bulkPreview;
	private BulkPricePreviewRow? _selectedBulkPreviewRow;

	public SalesPricingViewModel(SalesPricingService pricing,CustomerService customers,ItemService items,CategoryService categories,ManufacturerService manufacturers,PriceListGenerationService priceListGeneration)
	{
		_pricing=pricing;_customers=customers;_items=items;_priceListGeneration=priceListGeneration;_bulkCategoriesService=categories;_bulkManufacturersService=manufacturers;
		NewPriceListCommand=new RelayCommand(()=>{SelectedPriceList=null;Draft=NewDraft();});SavePriceListCommand=new AsyncRelayCommand(SavePriceListAsync,()=>_pricing.CanManage&&!string.IsNullOrWhiteSpace(Draft.Name)&&!string.IsNullOrWhiteSpace(Draft.Code)&&(Draft.Scope!=SalesPriceListScope.Region||SelectedPriceListRegion is not null));SavePriceItemCommand=new AsyncRelayCommand(SavePriceItemAsync,()=>_pricing.CanManage&&SelectedPriceList is not null&&SelectedItem is not null&&UnitPrice>=0);DeletePriceItemCommand=new AsyncRelayCommand(DeletePriceItemAsync,()=>_pricing.CanManage&&SelectedPriceItem is not null);AssignCustomerCommand=new AsyncRelayCommand(AssignCustomerAsync,()=>_pricing.CanManage&&SelectedCustomer is not null&&SelectedPriceList?.Scope==SalesPriceListScope.Customer);InitializeScopedPricing();
		CalculateBulkPreviewCommand=new AsyncRelayCommand(CalculateBulkPreviewAsync,CanCalculateBulkPreview);ApplyBulkPricesCommand=new AsyncRelayCommand(ApplyBulkPricesAsync,()=>BulkPreview is {ErrorCount:0}&&priceListGeneration.CanApply);PropertyChanged+=OnBulkPropertyChanged;_ = LoadBulkReferenceDataAsync();
	}

	public ObservableCollection<ItemReferenceData> BulkCategories{get;}=[];
	public ObservableCollection<ItemReferenceData> BulkManufacturers{get;}=[];
	public ObservableCollection<BulkItemSelectionViewModel> BulkItemSelections{get;}=[];
	public ObservableCollection<BulkPricePreviewRow> BulkPreviewRows{get;}=[];
	public IReadOnlyList<BulkPriceFilterType> BulkFilterTypes{get;}=Enum.GetValues<BulkPriceFilterType>();
	public IReadOnlyList<BulkPriceApplyMode> BulkApplyModes{get;}=Enum.GetValues<BulkPriceApplyMode>();
	public AsyncRelayCommand? CalculateBulkPreviewCommand{get;private set;}
	public AsyncRelayCommand? ApplyBulkPricesCommand{get;private set;}
	public BulkPriceFilterType BulkFilterType{get=>_bulkFilterType;set{if(_bulkFilterType==value)return;_bulkFilterType=value;OnPropertyChanged();OnPropertyChanged(nameof(IsBulkCategoryFilter));OnPropertyChanged(nameof(IsBulkManufacturerFilter));OnPropertyChanged(nameof(IsBulkSelectedItemsFilter));InvalidateBulkPreview();}}
	public BulkPriceApplyMode BulkApplyMode{get=>_bulkApplyMode;set{if(_bulkApplyMode==value)return;_bulkApplyMode=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public ItemReferenceData? BulkCategory{get=>_bulkCategory;set{if(_bulkCategory==value)return;_bulkCategory=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public ItemReferenceData? BulkManufacturer{get=>_bulkManufacturer;set{if(_bulkManufacturer==value)return;_bulkManufacturer=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public decimal BulkMarkupPercentage{get=>_bulkMarkupPercentage;set{if(_bulkMarkupPercentage==value)return;_bulkMarkupPercentage=value;OnPropertyChanged();InvalidateBulkPreview();}}
	public bool IsBulkCategoryFilter=>BulkFilterType==BulkPriceFilterType.Category;
	public bool IsBulkManufacturerFilter=>BulkFilterType==BulkPriceFilterType.Manufacturer;
	public bool IsBulkSelectedItemsFilter=>BulkFilterType==BulkPriceFilterType.SelectedItems;
	public PriceListGenerationPreview? BulkPreview{get=>_bulkPreview;private set{_bulkPreview=value;OnPropertyChanged();OnPropertyChanged(nameof(BulkPreviewSummary));ApplyBulkPricesCommand?.RaiseCanExecuteChanged();}}
	public string BulkPreviewSummary=>BulkPreview is null?"Calculate a preview before applying prices.":$"{BulkPreview.CreateCount} create · {BulkPreview.UpdateCount} update · {BulkPreview.SkipCount} skip · {BulkPreview.ErrorCount} errors";
	public BulkPricePreviewRow? SelectedBulkPreviewRow{get=>_selectedBulkPreviewRow;set{if(_selectedBulkPreviewRow==value)return;_selectedBulkPreviewRow=value;OnPropertyChanged();}}

	private void OnBulkPropertyChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName is nameof(SelectedPriceList) or nameof(Draft))InvalidateBulkPreview();}
	private async Task LoadBulkReferenceDataAsync(CancellationToken token=default)
	{
		if(_bulkCategoriesService is null||_bulkManufacturersService is null)return;try{var categories=await _bulkCategoriesService.GetActiveAsync(token);var manufacturers=await _bulkManufacturersService.GetActiveAsync(token);Replace(BulkCategories,categories);Replace(BulkManufacturers,manufacturers);var items=await _items.SearchItemsAsync(string.Empty,1,5000,token);BulkItemSelections.Clear();foreach(var item in items.Items.Where(i=>i.IsActive).OrderBy(i=>i.PartNumber))BulkItemSelections.Add(new BulkItemSelectionViewModel(item.Id,item.PartNumber,item.Description,InvalidateBulkPreview));}catch(OperationCanceledException)when(token.IsCancellationRequested){}
	}
	private bool CanCalculateBulkPreview()
	{
		if(_priceListGeneration?.CanPreview!=true||BulkMarkupPercentage<0m)return false;if(SelectedPriceList is null&&(string.IsNullOrWhiteSpace(Draft.Code)||string.IsNullOrWhiteSpace(Draft.Name)))return false;if(BulkFilterType==BulkPriceFilterType.Category&&BulkCategory is null)return false;if(BulkFilterType==BulkPriceFilterType.Manufacturer&&BulkManufacturer is null)return false;if(BulkFilterType==BulkPriceFilterType.SelectedItems&&!BulkItemSelections.Any(i=>i.IsSelected))return false;return true;
	}
	private async Task CalculateBulkPreviewAsync(CancellationToken token)
	{
		if(_priceListGeneration is null)return;var request=new PriceListGenerationRequest{ExistingPriceListId=SelectedPriceList?.Id,NewPriceList=SelectedPriceList is null?Copy(Draft):null,FilterType=BulkFilterType,FilterId=BulkFilterType==BulkPriceFilterType.Category?BulkCategory?.Id:BulkFilterType==BulkPriceFilterType.Manufacturer?BulkManufacturer?.Id:null,SelectedItemIds=BulkItemSelections.Where(i=>i.IsSelected).Select(i=>i.ItemId).ToArray(),MarkupPercentage=BulkMarkupPercentage,ApplyMode=BulkApplyMode,EffectiveDate=DateTime.Today};var preview=await _priceListGeneration.PreviewAsync(request,token);BulkPreview=preview;Replace(BulkPreviewRows,preview.Rows);CompleteOperation(false,"Bulk price preview calculated");
	}
	private async Task ApplyBulkPricesAsync(CancellationToken token)
	{
		if(_priceListGeneration is null||BulkPreview is null)return;var result=await _priceListGeneration.ApplyAsync(BulkPreview,token);CompleteOperation(false,$"Bulk pricing applied: {result.Created} created, {result.Updated} updated, {result.Skipped} skipped");BulkPreview=null;BulkPreviewRows.Clear();await LoadAsync(token);
	}
	private void InvalidateBulkPreview(){BulkPreview=null;BulkPreviewRows.Clear();CalculateBulkPreviewCommand?.RaiseCanExecuteChanged();ApplyBulkPricesCommand?.RaiseCanExecuteChanged();}
	private void DisposeBulkPricing(){PropertyChanged-=OnBulkPropertyChanged;CalculateBulkPreviewCommand?.Dispose();ApplyBulkPricesCommand?.Dispose();}
}

public sealed class BulkItemSelectionViewModel : BaseViewModel
{
	private readonly Action _changed;
	private bool _isSelected;
	public BulkItemSelectionViewModel(long itemId,string partNumber,string description,Action changed){ItemId=itemId;PartNumber=partNumber;Description=description;_changed=changed;}
	public long ItemId{get;} public string PartNumber{get;} public string Description{get;}
	public bool IsSelected{get=>_isSelected;set{if(_isSelected==value)return;_isSelected=value;OnPropertyChanged();_changed();}}
}
