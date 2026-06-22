// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class MainViewModel
	: BaseViewModel
{
	private readonly InventoryService _inventoryService;

	public MainViewModel(
		InventoryService inventoryService)
	{
		_inventoryService = inventoryService;

		LoadItems();
	}

	public ObservableCollection<ItemViewModel> Items { get; }
		= new();

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