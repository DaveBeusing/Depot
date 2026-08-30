// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.ViewModels.Shared;

namespace Depot.ViewModels;

public sealed partial class ItemsViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly ItemService _itemService;
	private readonly IItemReferenceDataService[] _referenceServices;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _listRequest = new();
	private ItemViewModel? _selectedItem;
	private string? _errorMessage;
	private string _searchText = string.Empty;
	private int _pageNumber = 1;
	private long _totalCount;
	private ActivationFilterOption _selectedActivationFilter = ActivationFilterOption.All[0];

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
		ClearReplacementCommand = new RelayCommand(() => Editor.ReplacementItemId = null);
		SaveItemCommand = new AsyncRelayCommand(SaveItemAsync);
		DeactivateItemCommand = new AsyncRelayCommand(DeactivateItemAsync, CanDeactivateItem);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<ItemViewModel> Items { get; } = new();
	public ObservableCollection<ItemViewModel> ReplacementItems { get; } = new();
	public ObservableCollection<ItemReferenceData> Manufacturers { get; } = new();
	public ObservableCollection<ItemReferenceData> Categories { get; } = new();
	public ObservableCollection<ItemReferenceData> UnitsOfMeasure { get; } = new();
	public ObservableCollection<ItemReferenceData> Packagings { get; } = new();
	public IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();
	public IReadOnlyList<ItemLifecycleStatus> LifecycleStatuses { get; } = Enum.GetValues<ItemLifecycleStatus>();
	public IReadOnlyList<ItemTrackingMode> TrackingModes { get; } = Enum.GetValues<ItemTrackingMode>();
	public IReadOnlyList<ItemComplianceStatus> ComplianceStatuses { get; } = Enum.GetValues<ItemComplianceStatus>();
	public bool HasItems => Items.Count > 0;
	public bool HasNoItems => !HasItems;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public IReadOnlyList<ActivationFilterOption> ActivationFilters => ActivationFilterOption.All;
	public string EditorStatus => Editor.Id == 0 ? "New" : SelectedItem?.IsActive == true ? "Active" : "Inactive";
	public string ActivationActionText => SelectedItem?.IsActive == true ? "Deactivate" : "Activate";
	public ItemEditorViewModel Editor { get; }
	public RelayCommand NewItemCommand { get; }
	public RelayCommand ClearReplacementCommand { get; }
	public AsyncRelayCommand SaveItemCommand { get; }
	public AsyncRelayCommand DeactivateItemCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public ActivationFilterOption SelectedActivationFilter
	{
		get => _selectedActivationFilter;
		set
		{
			if (_selectedActivationFilter == value) return;
			_selectedActivationFilter = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = _searchDebouncer.DebounceAsync(LoadItemsAsync);
		}
	}

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
			OnPropertyChanged(nameof(EditorStatus));
			OnPropertyChanged(nameof(ActivationActionText));
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
		var request = _listRequest.Begin(cancellationToken);
		BeginOperation("Loading items");
		var selectedId = SelectedItem?.Id;
		try
		{
			if (Manufacturers.Count == 0)
			{
				var values = await Task.WhenAll(_referenceServices.Select(service => service.GetActiveAsync(request.Token)));
				if (!request.IsCurrent) return;
				Fill(Manufacturers, values[0]);
				Fill(Categories, values[1]);
				Fill(UnitsOfMeasure, values[2]);
				Fill(Packagings, values[3]);
				ApplyDefaultUnitOfMeasure();
			}
			if (ReplacementItems.Count == 0)
			{
				var replacements = await _itemService.GetReplacementCandidatesAsync(request.Token);
				if (!request.IsCurrent) return;
				ReplaceReplacementItems(replacements);
			}
			var page = await _itemService.SearchItemMasterDataAsync(
				SearchText,
				SelectedActivationFilter.IsActive,
				PageNumber,
				PageSize,
				request.Token);
			if (!request.IsCurrent) return;
			CollectionSynchronizer.Replace(Items, page.Items.Select(item => new ItemViewModel(item)).ToArray());
			TotalCount = page.TotalCount;
			SelectedItem = selectedId is null ? null : Items.FirstOrDefault(x => x.Id == selectedId);
			RaiseCollectionState();
			CompleteOperation(Items.Count == 0, $"{page.TotalCount:N0} items");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
		{
			if (request.IsCurrent) CompleteOperation(Items.Count == 0);
		}
		catch (Exception) when (!request.IsCurrent) { }
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
		Editor.Gtin = SelectedItem.Gtin;
		Editor.ItemType = SelectedItem.ItemType;
		Editor.LifecycleStatus = SelectedItem.LifecycleStatus;
		Editor.Revision = SelectedItem.Revision;
		Editor.Model = SelectedItem.Model;
		Editor.ProductFamily = SelectedItem.ProductFamily;
		Editor.CountryOfOrigin = SelectedItem.CountryOfOrigin;
		Editor.CustomsTariffNumber = SelectedItem.CustomsTariffNumber;
		Editor.Eccn = SelectedItem.Eccn;
		Editor.TrackingMode = SelectedItem.TrackingMode;
		Editor.NetWeightKg = SelectedItem.NetWeightKg;
		Editor.GrossWeightKg = SelectedItem.GrossWeightKg;
		Editor.LengthMm = SelectedItem.LengthMm;
		Editor.WidthMm = SelectedItem.WidthMm;
		Editor.HeightMm = SelectedItem.HeightMm;
		Editor.IsDangerousGoods = SelectedItem.IsDangerousGoods;
		Editor.UnNumber = SelectedItem.UnNumber;
		Editor.ContainsBattery = SelectedItem.ContainsBattery;
		Editor.RohsStatus = SelectedItem.RohsStatus;
		Editor.ReachStatus = SelectedItem.ReachStatus;
		Editor.IntroductionDate = SelectedItem.IntroductionDate;
		Editor.EndOfLifeDate = SelectedItem.EndOfLifeDate;
		Editor.LastBuyDate = SelectedItem.LastBuyDate;
		Editor.EndOfSupportDate = SelectedItem.EndOfSupportDate;
		Editor.ReplacementItemId = SelectedItem.ReplacementItemId;
		Editor.Notes = SelectedItem.Notes;
		Editor.Version = SelectedItem.Version;
	}

	private void NewItem()
	{
		ClearError();
		SelectedItem = null;
		Editor.Clear();
		ApplyDefaultUnitOfMeasure();
		OnPropertyChanged(nameof(EditorStatus));
		OnPropertyChanged(nameof(ActivationActionText));
		DeactivateItemCommand.RaiseCanExecuteChanged();
		RequestEditorFocus();
	}

	private async Task SaveItemAsync(CancellationToken cancellationToken)
	{
		ClearError();
		BeginOperation("Saving item");
		try
		{
			var masterData = Editor.ToMasterData();
			var item = Editor.Id == 0
				? await _itemService.CreateItemMasterDataAsync(
					Editor.PartNumber,
					Editor.Description,
					Editor.Manufacturer?.Id,
					Editor.Category?.Id,
					Editor.UnitOfMeasure?.Id,
					Editor.Packaging?.Id,
					masterData,
					cancellationToken)
				: await _itemService.UpdateItemMasterDataAsync(
					Editor.Id,
					Editor.Version,
					Editor.Description,
					Editor.Manufacturer?.Id,
					Editor.Category?.Id,
					Editor.UnitOfMeasure?.Id,
					Editor.Packaging?.Id,
					masterData,
					cancellationToken);
			UpdateItem(item);
			UpdateReplacementCandidate(item);
			Editor.Clear();
			ApplyDefaultUnitOfMeasure();
			SelectedItem = null;
			CompleteOperation(Items.Count == 0, "Item saved");
			RequestEditorFocus();
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
		var isActive = SelectedItem?.IsActive != true;
		BeginOperation(isActive ? "Activating item" : "Deactivating item");
		try
		{
			var id = Editor.Id;
			var saved = await _itemService.SetItemActiveAsync(id, Editor.Version, isActive, cancellationToken);
			UpdateItem(saved);
			UpdateReplacementCandidate(saved);
			Editor.Clear();
			SelectedItem = null;
			RaiseCollectionState();
			CompleteOperation(Items.Count == 0, saved.IsActive ? "Item activated" : "Item deactivated");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Item status could not be changed");
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
			if (MatchesActivationFilter(item)) Items[Items.IndexOf(existing)] = new ItemViewModel(item);
			else
			{
				Items.Remove(existing);
				TotalCount = Math.Max(0, TotalCount - 1);
			}
		}
		else if (PageNumber == 1 && MatchesSearch(item) && MatchesActivationFilter(item))
		{
			Items.Insert(0, new ItemViewModel(item));
			if (Items.Count > PageSize) Items.RemoveAt(Items.Count - 1);
			TotalCount++;
		}
		RaiseCollectionState();
	}

	private void UpdateReplacementCandidate(Item item)
	{
		var existing = ReplacementItems.FirstOrDefault(candidate => candidate.Id == item.Id);
		if (!item.IsActive)
		{
			if (existing is not null) ReplacementItems.Remove(existing);
			return;
		}
		var viewModel = new ItemViewModel(item);
		if (existing is not null) ReplacementItems[ReplacementItems.IndexOf(existing)] = viewModel;
		else ReplacementItems.Add(viewModel);
		SortReplacementItems();
	}

	private void ReplaceReplacementItems(IReadOnlyList<Item> items)
	{
		ReplacementItems.Clear();
		foreach (var item in items) ReplacementItems.Add(new ItemViewModel(item));
	}

	private void SortReplacementItems()
	{
		var sorted = ReplacementItems.OrderBy(item => item.PartNumber, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToArray();
		ReplacementItems.Clear();
		foreach (var item in sorted) ReplacementItems.Add(item);
	}

	private bool MatchesActivationFilter(Item item) =>
		SelectedActivationFilter.IsActive is not bool isActive || item.IsActive == isActive;

	private bool MatchesSearch(Item item)
	{
		if (string.IsNullOrWhiteSpace(SearchText)) return true;
		var search = SearchText.Trim();
		return item.PartNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			item.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			(item.Manufacturer?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Category?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.UnitOfMeasure?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Packaging?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Gtin?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Revision?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Model?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.ProductFamily?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.CountryOfOrigin?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.CustomsTariffNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Eccn?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.UnNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
			(item.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	private void ApplyDefaultUnitOfMeasure()
	{
		if (Editor.Id != 0 || Editor.UnitOfMeasure is not null) return;
		Editor.UnitOfMeasure = UnitsOfMeasure.FirstOrDefault(value =>
			string.Equals(value.Name, "EA", StringComparison.OrdinalIgnoreCase));
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
		_listRequest.Dispose();
		SaveItemCommand.Dispose();
		DeactivateItemCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
		DisposeCosting();
	}
}
