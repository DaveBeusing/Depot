// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ItemsViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly ItemService _itemService;
	private readonly IItemReferenceDataService[] _referenceServices;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private ItemViewModel? _selectedItem;
	private string? _errorMessage;
	private string _searchText = string.Empty;
	private int _pageNumber = 1;
	private long _totalCount;

	public ItemsViewModel(
		ItemService itemService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService)
	{
		_itemService = itemService;
		_referenceServices = [manufacturerService, categoryService, unitOfMeasureService, packagingService];
		Editor = new ItemEditorViewModel();
		NewItemCommand = new RelayCommand(NewItem);
		SaveItemCommand = new AsyncRelayCommand(SaveItemAsync);
		DeactivateItemCommand = new AsyncRelayCommand(DeactivateItemAsync, CanDeactivateItem);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<ItemViewModel> Items { get; } = new();
	public ObservableCollection<ItemReferenceData> Manufacturers { get; } = new();
	public ObservableCollection<ItemReferenceData> Categories { get; } = new();
	public ObservableCollection<ItemReferenceData> UnitsOfMeasure { get; } = new();
	public ObservableCollection<ItemReferenceData> Packagings { get; } = new();
	public bool HasItems => Items.Count > 0;
	public bool HasNoItems => !HasItems;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public ItemEditorViewModel Editor { get; }
	public RelayCommand NewItemCommand { get; }
	public AsyncRelayCommand SaveItemCommand { get; }
	public AsyncRelayCommand DeactivateItemCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public int PageNumber
	{
		get => _pageNumber;
		private set
		{
			if (_pageNumber == value) return;
			_pageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			RaisePagingCommands();
		}
	}

	public long TotalCount
	{
		get => _totalCount;
		private set
		{
			if (_totalCount == value) return;
			_totalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			RaisePagingCommands();
		}
	}

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = _searchDebouncer.DebounceAsync(token => LoadItemsAsync(token));
		}
	}

	public ItemViewModel? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (_selectedItem == value) return;
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
			if (_errorMessage == value) return;
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

	public async Task LoadItemsAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading items");
		var selectedId = SelectedItem?.Id;
		try
		{
			if (Manufacturers.Count == 0)
			{
				var values = await Task.WhenAll(_referenceServices.Select(service => service.GetActiveAsync(cancellationToken)));
				Fill(Manufacturers, values[0]); Fill(Categories, values[1]); Fill(UnitsOfMeasure, values[2]); Fill(Packagings, values[3]);
			}
			var page = await _itemService.SearchItemsAsync(
				SearchText,
				PageNumber,
				PageSize,
				cancellationToken);
			Items.Clear();
			foreach (var item in page.Items) Items.Add(new ItemViewModel(item));
			TotalCount = page.TotalCount;
			SelectedItem = selectedId is null ? null : Items.FirstOrDefault(x => x.Id == selectedId);
			RaiseCollectionState();
			CompleteOperation(Items.Count == 0, $"{page.TotalCount:N0} items");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(Items.Count == 0);
		}
		catch (Exception exception)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Items could not be loaded");
		}
	}

	private void LoadSelectedItem()
	{
		ClearError();
		if (SelectedItem is null) return;
		Editor.Id = SelectedItem.Id;
		Editor.PartNumber = SelectedItem.PartNumber;
		Editor.Description = SelectedItem.Description;
		Editor.Manufacturer = Manufacturers.FirstOrDefault(value => value.Id == SelectedItem.ManufacturerId);
		Editor.Category = Categories.FirstOrDefault(value => value.Id == SelectedItem.CategoryId);
		Editor.UnitOfMeasure = UnitsOfMeasure.FirstOrDefault(value => value.Id == SelectedItem.UnitOfMeasureId);
		Editor.Packaging = Packagings.FirstOrDefault(value => value.Id == SelectedItem.PackagingId);
		Editor.Version = SelectedItem.Version;
	}

	private void NewItem()
	{
		ClearError();
		SelectedItem = null;
		Editor.Clear();
		DeactivateItemCommand.RaiseCanExecuteChanged();
	}

	private async Task SaveItemAsync(CancellationToken cancellationToken)
	{
		ClearError();
		BeginOperation("Saving item");
		try
		{
			var item = Editor.Id == 0
				? await _itemService.CreateItemWithReferencesAsync(
					Editor.PartNumber,
					Editor.Description,
					Editor.Manufacturer?.Id,
					Editor.Category?.Id,
					Editor.UnitOfMeasure?.Id,
					Editor.Packaging?.Id,
					cancellationToken)
				: await _itemService.UpdateItemWithReferencesAsync(
					Editor.Id,
					Editor.Version,
					Editor.Description,
					Editor.Manufacturer?.Id,
					Editor.Category?.Id,
					Editor.UnitOfMeasure?.Id,
					Editor.Packaging?.Id,
					cancellationToken);
			UpdateItem(item);
			Editor.Clear();
			SelectedItem = null;
			CompleteOperation(Items.Count == 0, "Item saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Item could not be saved");
		}
	}

	private bool CanDeactivateItem() => Editor.IsExistingItem;

	private async Task DeactivateItemAsync(CancellationToken cancellationToken)
	{
		ClearError();
		if (!Editor.IsExistingItem) return;
		BeginOperation("Deactivating item");
		try
		{
			var id = Editor.Id;
			await _itemService.DeactivateItemAsync(id, Editor.Version, cancellationToken);
			var existing = Items.FirstOrDefault(x => x.Id == id);
			if (existing is not null) Items.Remove(existing);
			TotalCount = Math.Max(0, TotalCount - 1);
			Editor.Clear();
			SelectedItem = null;
			RaiseCollectionState();
			CompleteOperation(Items.Count == 0, "Item deactivated");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Item could not be deactivated");
		}
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadItemsAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadItemsAsync(cancellationToken);
	}

	private void UpdateItem(Item item)
	{
		var existing = Items.FirstOrDefault(x => x.Id == item.Id);
		if (existing is not null)
		{
			Items[Items.IndexOf(existing)] = new ItemViewModel(item);
		}
		else if (PageNumber == 1 && MatchesSearch(item))
		{
			Items.Insert(0, new ItemViewModel(item));
			if (Items.Count > PageSize) Items.RemoveAt(Items.Count - 1);
			TotalCount++;
		}
		RaiseCollectionState();
	}

	private bool MatchesSearch(Item item)
	{
		if (string.IsNullOrWhiteSpace(SearchText)) return true;
		var search = SearchText.Trim();
		return item.PartNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			item.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			(item.Manufacturer?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Category?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.UnitOfMeasure?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Packaging?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	private void RaiseCollectionState()
	{
		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasNoItems));
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	private void ClearError() => ErrorMessage = null;

	private static void Fill(ObservableCollection<ItemReferenceData> target, IReadOnlyList<ItemReferenceData> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		SaveItemCommand.Dispose();
		DeactivateItemCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
	}
}
