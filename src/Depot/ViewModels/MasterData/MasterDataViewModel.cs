// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.ViewModels.Shared;
using Depot.ViewModels.Purposes;
using Depot.ViewModels.ReasonCodes;
using Depot.ViewModels.Warehouses;
using Depot.Services;

namespace Depot.ViewModels.MasterData;

/// <summary>
/// Represents the master data module.
/// </summary>
public sealed class MasterDataViewModel
	: BaseViewModel
{
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;
	private readonly PurposeViewModel _purposeViewModel;
	private readonly ReasonCodeViewModel _reasonCodeViewModel;
	private readonly IReadOnlyDictionary<MasterDataSection, ItemReferenceDataViewModel> _itemReferenceViewModels;
	private readonly WarehouseStructureViewModel _warehouseStructureViewModel;
	private bool _isInitialized;

	public MasterDataViewModel(
		PurposeService purposeService,
		ReasonCodeService reasonCodeService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService,
		SupplierService supplierService,
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
			[MasterDataSection.Suppliers] = new(supplierService)
		};
		_warehouseStructureViewModel = new WarehouseStructureViewModel(warehouseService, storageLocationService);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Purposes",
				Section = MasterDataSection.Purposes
			});

		AddItemReferenceNavigation("Manufacturers", MasterDataSection.Manufacturers);
		AddItemReferenceNavigation("Categories", MasterDataSection.Categories);
		AddItemReferenceNavigation("Units of Measure", MasterDataSection.UnitsOfMeasure);
		AddItemReferenceNavigation("Packaging", MasterDataSection.Packaging);
		AddItemReferenceNavigation("Suppliers", MasterDataSection.Suppliers);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Reason Codes",
				Section = MasterDataSection.ReasonCodes
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Warehouse Structure",
				Section = MasterDataSection.WarehouseStructure
			});

		SelectedNavigationItem =
			NavigationItems[0];
		_isInitialized = true;
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;

		set
		{
			if (_selectedNavigationItem == value)
			{
				return;
			}

			_selectedNavigationItem = value;

			OnPropertyChanged();

			UpdateCurrentViewModel();
			if (_isInitialized) _ = LoadAsync();
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;

		private set
		{
			_currentViewModel = value;

			OnPropertyChanged();
		}
	}

	private void UpdateCurrentViewModel()
	{
		if (SelectedNavigationItem is null)
		{
			CurrentViewModel = null;
			return;
		}

		var section =
			(MasterDataSection)SelectedNavigationItem.Section;

		CurrentViewModel =
			section switch
			{
				MasterDataSection.Purposes =>
					_purposeViewModel,

				MasterDataSection.ReasonCodes =>
					_reasonCodeViewModel,

				MasterDataSection.WarehouseStructure =>
					_warehouseStructureViewModel,

				_ when _itemReferenceViewModels.TryGetValue(section, out var referenceViewModel) =>
					referenceViewModel,

				_ =>
					new PlaceholderViewModel(
						"Master Data",
						"This module is currently under development.")
			};
	}

	public Task LoadAsync(CancellationToken cancellationToken = default) =>
		CurrentViewModel switch
		{
			PurposeViewModel purpose => purpose.LoadPurposesAsync(cancellationToken),
			ReasonCodeViewModel reasonCodes => reasonCodes.LoadAsync(cancellationToken),
			WarehouseStructureViewModel warehouseStructure => warehouseStructure.LoadAsync(cancellationToken),
			ItemReferenceDataViewModel itemReference => itemReference.LoadAsync(cancellationToken),
			_ => Task.CompletedTask
		};

	private void AddItemReferenceNavigation(string name, MasterDataSection section) =>
		NavigationItems.Add(new NavigationItem { Name = name, Section = section });
}
