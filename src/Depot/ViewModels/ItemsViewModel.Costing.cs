// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed partial class ItemsViewModel
{
	private ItemCostCalculationService? _itemCosts;
	private ItemCostComponent? _selectedCostComponent;
	private string _costCurrency=string.Empty;
	private string _costComponentName=string.Empty;
	private ItemCostCalculationType _costCalculationType;
	private ItemCostCalculationBase _costCalculationBase;
	private decimal _costValue;
	private int _costSequence=10;
	private DateTime? _costValidFrom;
	private DateTime? _costValidUntil;
	private bool _costIsActive=true;
	private long _costComponentId;
	private long _costComponentVersion=1;
	private string _baseCostDisplay="—";
	private string _calculatedCostDisplay="—";

	public ItemsViewModel(ItemService itemService,ManufacturerService manufacturerService,CategoryService categoryService,UnitOfMeasureService unitOfMeasureService,PackagingService packagingService,ItemCostCalculationService itemCosts)
	{
		_itemService=itemService;_referenceServices=[manufacturerService,categoryService,unitOfMeasureService,packagingService];_itemCosts=itemCosts;
		Editor=new ItemEditorViewModel();NewItemCommand=new RelayCommand(NewItem);ClearReplacementCommand=new RelayCommand(()=>Editor.ReplacementItemId=null);SaveItemCommand=new AsyncRelayCommand(SaveItemAsync);DeactivateItemCommand=new AsyncRelayCommand(DeactivateItemAsync,CanDeactivateItem);PreviousPageCommand=new AsyncRelayCommand(PreviousPageAsync,()=>PageNumber>1);NextPageCommand=new AsyncRelayCommand(NextPageAsync,()=>HasNextPage);
		NewCostComponentCommand=new RelayCommand(NewCostComponent,()=>SelectedItem is not null&&itemCosts.CanManage);SaveCostProfileCommand=new AsyncRelayCommand(SaveCostProfileAsync,()=>SelectedItem is not null&&itemCosts.CanManage&&!string.IsNullOrWhiteSpace(CostCurrency));SaveCostComponentCommand=new AsyncRelayCommand(SaveCostComponentAsync,()=>SelectedItem is not null&&itemCosts.CanManage&&!string.IsNullOrWhiteSpace(CostComponentName)&&CostValue>=0&&CostSequence>=0);ToggleCostComponentCommand=new AsyncRelayCommand(ToggleCostComponentAsync,()=>SelectedCostComponent is not null&&itemCosts.CanManage);
		PropertyChanged+=OnCostingPropertyChanged;
	}

	public ObservableCollection<ItemCostComponent> CostComponents{get;}=[];
	public IReadOnlyList<ItemCostCalculationType> CostCalculationTypes{get;}=Enum.GetValues<ItemCostCalculationType>();
	public IReadOnlyList<ItemCostCalculationBase> CostCalculationBases{get;}=Enum.GetValues<ItemCostCalculationBase>();
	public RelayCommand? NewCostComponentCommand{get;private set;}
	public AsyncRelayCommand? SaveCostProfileCommand{get;private set;}
	public AsyncRelayCommand? SaveCostComponentCommand{get;private set;}
	public AsyncRelayCommand? ToggleCostComponentCommand{get;private set;}
	public bool CanManageItemCosts=>_itemCosts?.CanManage==true;
	public bool IsPercentageCost=>CostCalculationType==ItemCostCalculationType.Percentage;
	public string BaseCostDisplay{get=>_baseCostDisplay;private set{if(_baseCostDisplay==value)return;_baseCostDisplay=value;OnPropertyChanged();}}
	public string CalculatedCostDisplay{get=>_calculatedCostDisplay;private set{if(_calculatedCostDisplay==value)return;_calculatedCostDisplay=value;OnPropertyChanged();}}
	public string CostCurrency{get=>_costCurrency;set{if(_costCurrency==value)return;_costCurrency=value;OnPropertyChanged();SaveCostProfileCommand?.RaiseCanExecuteChanged();}}
	public string CostComponentName{get=>_costComponentName;set{if(_costComponentName==value)return;_costComponentName=value;OnPropertyChanged();SaveCostComponentCommand?.RaiseCanExecuteChanged();}}
	public ItemCostCalculationType CostCalculationType{get=>_costCalculationType;set{if(_costCalculationType==value)return;_costCalculationType=value;OnPropertyChanged();OnPropertyChanged(nameof(IsPercentageCost));SaveCostComponentCommand?.RaiseCanExecuteChanged();}}
	public ItemCostCalculationBase CostCalculationBase{get=>_costCalculationBase;set{if(_costCalculationBase==value)return;_costCalculationBase=value;OnPropertyChanged();}}
	public decimal CostValue{get=>_costValue;set{if(_costValue==value)return;_costValue=value;OnPropertyChanged();SaveCostComponentCommand?.RaiseCanExecuteChanged();}}
	public int CostSequence{get=>_costSequence;set{if(_costSequence==value)return;_costSequence=value;OnPropertyChanged();SaveCostComponentCommand?.RaiseCanExecuteChanged();}}
	public DateTime? CostValidFrom{get=>_costValidFrom;set{if(_costValidFrom==value)return;_costValidFrom=value;OnPropertyChanged();}}
	public DateTime? CostValidUntil{get=>_costValidUntil;set{if(_costValidUntil==value)return;_costValidUntil=value;OnPropertyChanged();}}
	public bool CostIsActive{get=>_costIsActive;set{if(_costIsActive==value)return;_costIsActive=value;OnPropertyChanged();}}
	public ItemCostComponent? SelectedCostComponent{get=>_selectedCostComponent;set{if(_selectedCostComponent==value)return;_selectedCostComponent=value;OnPropertyChanged();LoadCostComponentDraft(value);ToggleCostComponentCommand?.RaiseCanExecuteChanged();}}

	private void OnCostingPropertyChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName!=nameof(SelectedItem))return;NewCostComponentCommand?.RaiseCanExecuteChanged();SaveCostProfileCommand?.RaiseCanExecuteChanged();SaveCostComponentCommand?.RaiseCanExecuteChanged();_ = LoadCostBuildUpAsync();}
	private async Task LoadCostBuildUpAsync(CancellationToken token=default)
	{
		CostComponents.Clear();BaseCostDisplay="—";CalculatedCostDisplay="—";if(SelectedItem is null||_itemCosts is null)return;try{var profile=await _itemCosts.GetProfileAsync(SelectedItem.Id,token);CostCurrency=profile?.Currency??string.Empty;foreach(var component in await _itemCosts.ListComponentsAsync(SelectedItem.Id,token))CostComponents.Add(component);var result=await _itemCosts.CalculateAsync(SelectedItem.Id,DateTime.Today,null,token);if(result.IsSuccess){BaseCostDisplay=$"{result.Currency} {result.BaseCost:N2}";CalculatedCostDisplay=$"{result.Currency} {result.CalculatedCost:N2}";}else CalculatedCostDisplay=result.Error??"Not calculable";}catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception ex){CalculatedCostDisplay=ex.Message;}
	}
	private async Task SaveCostProfileAsync(CancellationToken token){if(SelectedItem is null||_itemCosts is null)return;var existing=await _itemCosts.GetProfileAsync(SelectedItem.Id,token);await _itemCosts.SaveProfileAsync(new ItemCostProfile{Id=existing?.Id??0,ItemId=SelectedItem.Id,BaseCostSource=ItemCostBaseSource.PreferredSupplierPurchasePrice,Currency=CostCurrency,Version=existing?.Version??1},token);await LoadCostBuildUpAsync(token);}
	private async Task SaveCostComponentAsync(CancellationToken token){if(SelectedItem is null||_itemCosts is null)return;await _itemCosts.SaveComponentAsync(new ItemCostComponent{Id=_costComponentId,ItemId=SelectedItem.Id,Name=CostComponentName,CalculationType=CostCalculationType,CalculationBase=CostCalculationBase,Value=CostValue,Sequence=CostSequence,IsActive=CostIsActive,ValidFrom=CostValidFrom,ValidUntil=CostValidUntil,Version=_costComponentVersion},token);NewCostComponent();await LoadCostBuildUpAsync(token);}
	private async Task ToggleCostComponentAsync(CancellationToken token){if(SelectedCostComponent is null||_itemCosts is null)return;await _itemCosts.SetComponentActiveAsync(SelectedCostComponent,!SelectedCostComponent.IsActive,token);NewCostComponent();await LoadCostBuildUpAsync(token);}
	private void NewCostComponent(){SelectedCostComponent=null;_costComponentId=0;_costComponentVersion=1;CostComponentName=string.Empty;CostCalculationType=ItemCostCalculationType.Absolute;CostCalculationBase=ItemCostCalculationBase.BaseCost;CostValue=0m;CostSequence=CostComponents.Count==0?10:CostComponents.Max(c=>c.Sequence)+10;CostValidFrom=null;CostValidUntil=null;CostIsActive=true;}
	private void LoadCostComponentDraft(ItemCostComponent? component){if(component is null)return;_costComponentId=component.Id;_costComponentVersion=component.Version;CostComponentName=component.Name;CostCalculationType=component.CalculationType;CostCalculationBase=component.CalculationBase;CostValue=component.Value;CostSequence=component.Sequence;CostValidFrom=component.ValidFrom;CostValidUntil=component.ValidUntil;CostIsActive=component.IsActive;}
	private void DisposeCosting(){PropertyChanged-=OnCostingPropertyChanged;SaveCostProfileCommand?.Dispose();SaveCostComponentCommand?.Dispose();ToggleCostComponentCommand?.Dispose();}
}
