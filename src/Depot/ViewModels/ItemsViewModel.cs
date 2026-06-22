// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
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

		Editor = new ItemEditorViewModel();

		NewItemCommand =
			new RelayCommand(
				NewItem);

		SaveItemCommand =
			new RelayCommand(
				SaveItem);

		LoadItems();
	}

	public ObservableCollection<ItemViewModel> Items { get; }
		= new();

	public ItemEditorViewModel Editor { get; }

	public RelayCommand NewItemCommand { get; }

	public RelayCommand SaveItemCommand { get; }

	public void LoadItems()
	{
		Items.Clear();

		foreach (var item in _inventoryService.GetItems())
		{
			Items.Add(
				new ItemViewModel(item));
		}
	}

	private void NewItem()
	{
		Editor.Clear();
	}

	private void SaveItem()
	{
		_inventoryService.CreateItem(
			Editor.PartNumber,
			Editor.Description,
			Editor.Manufacturer,
			Editor.Category);

		LoadItems();

		Editor.Clear();
	}
}