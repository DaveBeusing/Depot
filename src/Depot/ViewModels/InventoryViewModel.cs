// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class InventoryViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly StockService _stockService;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _listRequest = new();
	private readonly LatestRequest _detailsRequest = new();
	private InventoryOverviewItemViewModel? _selectedItem;
	private string _searchText = string.Empty;
	private int _pageNumber = 1;
	private long _totalCount;
	private bool _isLoadingDetails;

	public InventoryViewModel(StockService stockService)
	{
		_stockService = stockService;
		Details = new InventoryDetailsViewModel();
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<InventoryOverviewItemViewModel> Items { get; } = new();
	public bool HasItems => Items.Count > 0;
	public bool HasNoItems => !HasItems;
	public bool HasSelectedItem => SelectedItem is not null;
	public bool HasNoSelectedItem => !HasSelectedItem;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public bool IsLoadingDetails { get => _isLoadingDetails; private set { if (_isLoadingDetails == value) return; _isLoadingDetails = value; OnPropertyChanged(); } }
	public InventoryDetailsViewModel Details { get; }
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
			_ = _searchDebouncer.DebounceAsync(LoadAsync);
		}
	}

	public InventoryOverviewItemViewModel? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (_selectedItem == value) return;
			_selectedItem = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasSelectedItem));
			OnPropertyChanged(nameof(HasNoSelectedItem));
			_ = LoadSelectedDetailsAsync(value);
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _listRequest.Begin(cancellationToken);
		BeginOperation("Loading inventory");
		var selectedId = SelectedItem?.InventoryId;
		try
		{
			var page = await _stockService.SearchInventoryOverviewAsync(
				SearchText,
				PageNumber,
				PageSize,
				request.Token);
			if (!request.IsCurrent) return;
			CollectionSynchronizer.Replace(Items, page.Items.Select(item => new InventoryOverviewItemViewModel(item)).ToArray());
			TotalCount = page.TotalCount;
			SelectedItem = selectedId is null
				? null
				: Items.FirstOrDefault(x => x.InventoryId == selectedId);
			OnPropertyChanged(nameof(HasItems));
			OnPropertyChanged(nameof(HasNoItems));
			if (SelectedItem is null) Details.Clear();
			CompleteOperation(Items.Count == 0, $"{page.TotalCount:N0} inventory records");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
		{
			if (request.IsCurrent) CompleteOperation(Items.Count == 0);
		}
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory could not be loaded");
		}
	}

	private async Task LoadSelectedDetailsAsync(InventoryOverviewItemViewModel? selected)
	{
		var request = _detailsRequest.Begin();
		if (selected is null)
		{
			Details.Clear();
			IsLoadingDetails = false;
			return;
		}
		IsLoadingDetails = true;
		try
		{
			var details = await _stockService.GetInventoryDetailsAsync(
				selected.InventoryId,
				request.Token);
			if (!request.IsCurrent || SelectedItem?.InventoryId != selected.InventoryId) return;
			Details.Load(details);
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
		{
		}
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory details could not be loaded");
		}
		finally { if (request.IsCurrent) IsLoadingDetails = false; }
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadAsync(cancellationToken);
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		_listRequest.Dispose();
		_detailsRequest.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
	}
}
