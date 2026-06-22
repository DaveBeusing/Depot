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
	private string? _errorMessage;

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

		DeactivateItemCommand =
			new RelayCommand(
				DeactivateItem,
				CanDeactivateItem);

		LoadItems();
	}

	public ObservableCollection<ItemViewModel> Items { get; }
		= new();

	public ItemEditorViewModel Editor { get; }

	public RelayCommand NewItemCommand { get; }

	public RelayCommand SaveItemCommand { get; }

	public RelayCommand DeactivateItemCommand { get; }

	public ItemViewModel? SelectedItem
	{
		get => _selectedItem;

		set
		{
			_selectedItem = value;
			OnPropertyChanged();

			LoadSelectedItem();

			DeactivateItemCommand.RaiseCanExecuteChanged();
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;

		private set
		{
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage =>
		!string.IsNullOrWhiteSpace(ErrorMessage);

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
		ClearError();

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
		ClearError();

		SelectedItem = null;

		Editor.Clear();

		DeactivateItemCommand.RaiseCanExecuteChanged();
	}

	private void SaveItem()
	{
		ClearError();

		try
		{
			if (Editor.Id == 0)
			{
				_inventoryService.CreateItem(
					Editor.PartNumber,
					Editor.Description,
					Editor.Manufacturer,
					Editor.Category);
			}
			else
			{
				_inventoryService.UpdateItem(
					Editor.Id,
					Editor.Description,
					Editor.Manufacturer,
					Editor.Category);
			}

			LoadItems();

			Editor.Clear();

			SelectedItem = null;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool CanDeactivateItem()
	{
		return Editor.IsExistingItem;
	}

	private void DeactivateItem()
	{
		ClearError();

		if (!Editor.IsExistingItem)
		{
			return;
		}

		try
		{
			_inventoryService.DeactivateItem(
				Editor.Id);

			LoadItems();

			Editor.Clear();

			SelectedItem = null;

			DeactivateItemCommand.RaiseCanExecuteChanged();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void ClearError()
	{
		ErrorMessage = null;
	}
}