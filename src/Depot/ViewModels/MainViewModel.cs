// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;
using Depot.Repositories;

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
		StockMovementRepository stockMovementRepository)
	{
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

	public InventoryViewModel InventoryViewModel { get; }

	public ItemsViewModel ItemsViewModel { get; }

	public MovementsViewModel MovementsViewModel { get; }

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
				"Inventory" => InventoryViewModel,
				"Items" => ItemsViewModel,
				"Movements" => MovementsViewModel,
				_ => InventoryViewModel
			};
	}
}