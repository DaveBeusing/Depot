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

	private ItemViewModel? _selectedItem;

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

	public ItemViewModel? SelectedItem
	{
		get => _selectedItem;

		set
		{
			_selectedItem = value;

			OnPropertyChanged();

			LoadSelectedItem();
		}
	}

	public void LoadItems()
	{
		Items.Clear();

		foreach (var item in _inventoryService.GetItems())
		{
			Items.Add(
				new ItemViewModel(item));
		}
	}

	private void LoadSelectedItem()
	{
		if (SelectedItem is null)
		{
			return;
		}

		Editor.Id = SelectedItem.Id;
		Editor.PartNumber = SelectedItem.PartNumber;
		Editor.Description = SelectedItem.Description;
		Editor.Manufacturer = SelectedItem.Manufacturer;
		Editor.Category = SelectedItem.Category;
	}

	private void NewItem()
	{
		SelectedItem = null;

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