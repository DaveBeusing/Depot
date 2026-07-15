// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ItemsViewModel
	: BaseViewModel
{
	private readonly ItemService _itemService;

	private ItemViewModel? _selectedItem;
	private string? _errorMessage;
	private string _searchText = string.Empty;

	public ItemsViewModel(
		ItemService itemService)
	{
		_itemService = itemService;

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

	public bool HasItems => Items.Count > 0;

	public bool HasNoItems => !HasItems;

	public ItemEditorViewModel Editor { get; }

	public RelayCommand NewItemCommand { get; }

	public RelayCommand SaveItemCommand { get; }

	public RelayCommand DeactivateItemCommand { get; }

	public string SearchText
	{
		get => _searchText;

		set
		{
			if (_searchText == value)
			{
				return;
			}

			_searchText = value;
			OnPropertyChanged();

			LoadItems();
		}
	}

	public ItemViewModel? SelectedItem
	{
		get => _selectedItem;

		set
		{
			if (_selectedItem == value)
			{
				return;
			}

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
			OnPropertyChanged(
				nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage =>
		!string.IsNullOrWhiteSpace(
			ErrorMessage);

	public void LoadItems()
	{
		var selectedItemId =
			SelectedItem?.Id;

		Items.Clear();

		foreach (var item in _itemService.SearchItems(SearchText))
		{
			Items.Add(
				new ItemViewModel(
					item));
		}

		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasNoItems));

		if (selectedItemId is not null)
		{
			SelectedItem =
				Items.FirstOrDefault(
					x => x.Id == selectedItemId.Value);
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
				_itemService.CreateItem(
					Editor.PartNumber,
					Editor.Description,
					Editor.Manufacturer,
					Editor.Category);
			}
			else
			{
				_itemService.UpdateItem(
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
			_itemService.DeactivateItem(
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
