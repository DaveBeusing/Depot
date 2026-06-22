// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class ItemsViewModel
	: BaseViewModel
{
	private readonly InventoryService _inventoryService;

	public ItemsViewModel(
		InventoryService inventoryService)
	{
		_inventoryService = inventoryService;

		LoadItems();
	}

	public ObservableCollection<ItemViewModel> Items { get; }
		= new();

	public void LoadItems()
	{
		Items.Clear();

		foreach (var item in _inventoryService.GetItems())
		{
			Items.Add(
				new ItemViewModel(item));
		}
	}
}