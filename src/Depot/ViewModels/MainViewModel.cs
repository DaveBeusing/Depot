// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Repositories;
using Depot.Services;
using Depot.Services.Import;

namespace Depot.ViewModels;

public sealed class MainViewModel
	: BaseViewModel
{
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public MainViewModel(
		InventoryService inventoryService,
		StockService stockService,
		ItemRepository itemRepository,
		StockMovementRepository stockMovementRepository,
		ImportService importService)
	{
		DashboardViewModel =
			new DashboardViewModel(
				stockService);

		InventoryViewModel =
			new InventoryViewModel(
				stockService);

		ItemsViewModel =
			new ItemsViewModel(
				inventoryService);

		MovementsViewModel =
			new MovementsViewModel(
				itemRepository,
				stockMovementRepository,
				stockService);

		ImportViewModel =
			new ImportViewModel(
				importService);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Dashboard"
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Inventory"
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Items"
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Movements"
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Import"
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Reports"
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Settings"
			});

		SelectedNavigationItem =
			NavigationItems[0];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public DashboardViewModel DashboardViewModel { get; }

	public InventoryViewModel InventoryViewModel { get; }

	public ItemsViewModel ItemsViewModel { get; }

	public MovementsViewModel MovementsViewModel { get; }

	public ImportViewModel ImportViewModel { get; }

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;

		set
		{
			_selectedNavigationItem = value;

			OnPropertyChanged();

			UpdateCurrentViewModel();
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

		CurrentViewModel =
			SelectedNavigationItem.Name switch
			{
				"Dashboard" => DashboardViewModel,
				"Inventory" => InventoryViewModel,
				"Items" => ItemsViewModel,
				"Movements" => MovementsViewModel,
				"Import" => ImportViewModel,
				_ => DashboardViewModel
			};

		if (CurrentViewModel == DashboardViewModel)
		{
			DashboardViewModel.Load();
		}

		if (CurrentViewModel == InventoryViewModel)
		{
			InventoryViewModel.Load();
		}

		if (CurrentViewModel == MovementsViewModel)
		{
			MovementsViewModel.Load();
		}
	}
}