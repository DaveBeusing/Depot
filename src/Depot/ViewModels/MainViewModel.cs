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

	public MainViewModel(
		InventoryService inventoryService)
	{
		_inventoryService = inventoryService;

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

		LoadItems();
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public ObservableCollection<ItemViewModel> Items { get; }
		= new();

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;

		set
		{
			_selectedNavigationItem = value;
			OnPropertyChanged();
		}
	}

	private void LoadItems()
	{
		Items.Clear();

		foreach (var item in _inventoryService.GetItems())
		{
			Items.Add(
				new ItemViewModel(item));
		}
	}
}