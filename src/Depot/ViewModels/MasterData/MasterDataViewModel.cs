// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;
using Depot.ViewModels.Purposes;
using Depot.ViewModels.ReasonCodes;
using Depot.ViewModels.Suppliers;
using Depot.ViewModels.Warehouses;

namespace Depot.ViewModels.MasterData;

public sealed class MasterDataViewModel : BaseViewModel, IDisposable
{
	private readonly PurposeViewModel _purposeViewModel;
	private readonly ReasonCodeViewModel _reasonCodeViewModel;
	private readonly IReadOnlyDictionary<MasterDataSection, ItemReferenceDataViewModel> _itemReferenceViewModels;
	private readonly WarehouseStructureViewModel _warehouseStructureViewModel;
	private readonly SupplierViewModel _supplierViewModel;
	private readonly Dictionary<MasterDataSection, NavigationLoadState> _loadStates = [];
	private CancellationTokenSource? _navigationCancellation;
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public MasterDataViewModel(
		PurposeService purposeService,
		ReasonCodeService reasonCodeService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService,
		SupplierCategoryService supplierCategoryService,
		SupplierService supplierService,
		SupplierItemService supplierItemService,
		ItemService itemService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService)
	{
		_purposeViewModel = new PurposeViewModel(purposeService);
		_reasonCodeViewModel = new ReasonCodeViewModel(reasonCodeService);
		_itemReferenceViewModels = new Dictionary<MasterDataSection, ItemReferenceDataViewModel>
		{
			[MasterDataSection.Manufacturers] = new(manufacturerService),
			[MasterDataSection.Categories] = new(categoryService),
			[MasterDataSection.UnitsOfMeasure] = new(unitOfMeasureService),
			[MasterDataSection.Packaging] = new(packagingService),
			[MasterDataSection.SupplierCategories] = new(supplierCategoryService)
		};
		_supplierViewModel = new SupplierViewModel(supplierService, supplierItemService, supplierCategoryService, itemService);
		_warehouseStructureViewModel = new WarehouseStructureViewModel(warehouseService, storageLocationService);

		Add("Purposes", MasterDataSection.Purposes);
		Add("Manufacturers", MasterDataSection.Manufacturers);
		Add("Categories", MasterDataSection.Categories);
		Add("Units of Measure", MasterDataSection.UnitsOfMeasure);
		Add("Packaging", MasterDataSection.Packaging);
		Add("Supplier Categories", MasterDataSection.SupplierCategories);
		Add("Reason Codes", MasterDataSection.ReasonCodes);
		SetSelection(NavigationItems.FirstOrDefault());
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; } = [];
	public WarehouseStructureViewModel WarehouseStructureViewModel => _warehouseStructureViewModel;
	public SupplierViewModel SupplierViewModel => _supplierViewModel;

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;
		set
		{
			if (value is null || _selectedNavigationItem == value) return;
			_ = NavigateAsync(value);
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;
		private set
		{
			if (_currentViewModel == value) return;
			_currentViewModel = value;
			OnPropertyChanged();
		}
	}

	public Task ActivateAsync(CancellationToken cancellationToken = default) =>
		ActivateCurrentAsync(false, cancellationToken);

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		ActivateCurrentAsync(true, cancellationToken);

	public Task LoadAsync(CancellationToken cancellationToken = default) => ActivateAsync(cancellationToken);

	private async Task NavigateAsync(NavigationItem target)
	{
		_navigationCancellation?.Cancel();
		_navigationCancellation?.Dispose();
		var navigation = new CancellationTokenSource();
		_navigationCancellation = navigation;
		SetSelection(target);
		try
		{
			await ActivateCurrentAsync(false, navigation.Token);
		}
		catch (OperationCanceledException) when (navigation.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, $"{target.Name} could not be loaded");
		}
	}

	private Task ActivateCurrentAsync(bool refresh, CancellationToken cancellationToken)
	{
		if (SelectedNavigationItem?.Section is not MasterDataSection section) return Task.CompletedTask;
		var state = _loadStates[section];
		return refresh
			? state.RefreshAsync(LoadCurrentViewModelAsync, cancellationToken)
			: state.ActivateAsync(LoadCurrentViewModelAsync, cancellationToken);
	}

	private void SetSelection(NavigationItem? target)
	{
		_selectedNavigationItem = target;
		OnPropertyChanged(nameof(SelectedNavigationItem));
		CurrentViewModel = target?.Section is MasterDataSection section ? ViewModelFor(section) : null;
	}

	private BaseViewModel ViewModelFor(MasterDataSection section)
	{
		if (_itemReferenceViewModels.TryGetValue(section, out var itemReference)) return itemReference;
		return section switch
		{
			MasterDataSection.Purposes => _purposeViewModel,
			MasterDataSection.ReasonCodes => _reasonCodeViewModel,
			MasterDataSection.WarehouseStructure => _warehouseStructureViewModel,
			MasterDataSection.Suppliers => _supplierViewModel,
			_ => _purposeViewModel
		};
	}

	private Task LoadCurrentViewModelAsync(CancellationToken cancellationToken) => CurrentViewModel switch
	{
		PurposeViewModel purpose => purpose.LoadPurposesAsync(cancellationToken),
		ReasonCodeViewModel reasonCodes => reasonCodes.LoadAsync(cancellationToken),
		WarehouseStructureViewModel warehouseStructure => warehouseStructure.LoadAsync(cancellationToken),
		SupplierViewModel suppliers => suppliers.LoadAsync(cancellationToken),
		ItemReferenceDataViewModel itemReference => itemReference.LoadAsync(cancellationToken),
		_ => Task.CompletedTask
	};

	private void Add(string name, MasterDataSection section)
	{
		NavigationItems.Add(new NavigationItem { Name = name, Section = section });
		_loadStates.Add(section, new NavigationLoadState());
	}

	public void Dispose()
	{
		_navigationCancellation?.Cancel();
		_navigationCancellation?.Dispose();
		foreach (var state in _loadStates.Values) state.Dispose();
		_purposeViewModel.Dispose();
		_reasonCodeViewModel.Dispose();
		foreach (var viewModel in _itemReferenceViewModels.Values) viewModel.Dispose();
		_warehouseStructureViewModel.Dispose();
		_supplierViewModel.Dispose();
	}
}
