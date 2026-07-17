// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.ViewModels.Shared;
using Depot.ViewModels.Purposes;
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
	private readonly WarehouseStructureViewModel _warehouseStructureViewModel;
	private bool _isInitialized;

	public MasterDataViewModel(
		PurposeService purposeService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService)
	{
		_purposeViewModel = new PurposeViewModel(purposeService);
		_warehouseStructureViewModel = new WarehouseStructureViewModel(warehouseService, storageLocationService);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Purposes",
				Section = MasterDataSection.Purposes
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

				MasterDataSection.WarehouseStructure =>
					_warehouseStructureViewModel,

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
			WarehouseStructureViewModel warehouseStructure => warehouseStructure.LoadAsync(cancellationToken),
			_ => Task.CompletedTask
		};
}
