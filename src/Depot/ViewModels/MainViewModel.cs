// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class MainViewModel
	: BaseViewModel
{
	private readonly InventoryService _inventoryService;

	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public MainViewModel(
		InventoryService inventoryService)
	{
		_inventoryService = inventoryService;

		ItemsViewModel =
			new ItemsViewModel(
				_inventoryService);

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
			NavigationItems[1];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public ItemsViewModel ItemsViewModel { get; }

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
				"Items" => ItemsViewModel,
				_ => ItemsViewModel
			};
	}

}