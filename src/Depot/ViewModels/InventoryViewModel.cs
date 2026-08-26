// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class InventoryViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private const int TraceabilityPageSize = 100;
	private readonly StockService _stockService;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _traceabilitySearchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _listRequest = new();
	private readonly LatestRequest _detailsRequest = new();
	private readonly LatestRequest _traceabilityRequest = new();
	private readonly LatestRequest _traceabilityHistoryRequest = new();
	private InventoryOverviewItemViewModel? _selectedItem;
	private ItemTraceabilityBalance? _selectedTraceability;
	private string _searchText = string.Empty;
	private string _traceabilitySearchText = string.Empty;
	private string _blockReason = string.Empty;
	private int _pageNumber = 1;
	private int _traceabilityPageNumber = 1;
	private long _totalCount;
	private long _traceabilityTotalCount;
	private bool _isLoadingDetails;

	public InventoryViewModel(StockService stockService)
	{
		_stockService = stockService;
		Details = new InventoryDetailsViewModel();
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		PreviousTraceabilityPageCommand = new AsyncRelayCommand(PreviousTraceabilityPageAsync, () => TraceabilityPageNumber > 1);
		NextTraceabilityPageCommand = new AsyncRelayCommand(NextTraceabilityPageAsync, () => HasTraceabilityNextPage);
		BlockTrackingUnitCommand = new AsyncRelayCommand(BlockTrackingUnitAsync, () => CanBlockTrackingUnit);
		UnblockTrackingUnitCommand = new AsyncRelayCommand(UnblockTrackingUnitAsync, () => CanUnblockTrackingUnit);
	}

	public ObservableCollection<InventoryOverviewItemViewModel> Items { get; } = new();
	public ObservableCollection<ItemTraceabilityBalance> TraceabilityBalances { get; } = new();
	public ObservableCollection<ItemTraceabilityHistoryEntry> TraceabilityHistory { get; } = new();
	public bool HasItems => Items.Count > 0;
	public bool HasNoItems => !HasItems;
	public bool HasSelectedItem => SelectedItem is not null;
	public bool HasNoSelectedItem => !HasSelectedItem;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public bool HasTraceabilityBalances => TraceabilityBalances.Count > 0;
	public bool HasNoTraceabilityBalances => !HasTraceabilityBalances;
	public bool HasTraceabilityHistory => TraceabilityHistory.Count > 0;
	public bool HasNoTraceabilityHistory => !HasTraceabilityHistory;
	public bool HasSelectedTraceability => SelectedTraceability is not null;
	public bool HasTraceabilityNextPage => (long)TraceabilityPageNumber * TraceabilityPageSize < TraceabilityTotalCount;
	public bool CanManageTraceability => _stockService.CanManageTraceability;
	public bool CanBlockTrackingUnit => CanManageTraceability && SelectedTraceability is { IsBlocked: false } && !string.IsNullOrWhiteSpace(BlockReason);
	public bool CanUnblockTrackingUnit => CanManageTraceability && SelectedTraceability is { IsBlocked: true };
	public bool IsLoadingDetails { get => _isLoadingDetails; private set { if (_isLoadingDetails == value) return; _isLoadingDetails = value; OnPropertyChanged(); } }
	public InventoryDetailsViewModel Details { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public AsyncRelayCommand PreviousTraceabilityPageCommand { get; }
	public AsyncRelayCommand NextTraceabilityPageCommand { get; }
	public AsyncRelayCommand BlockTrackingUnitCommand { get; }
	public AsyncRelayCommand UnblockTrackingUnitCommand { get; }

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

	public int TraceabilityPageNumber
	{
		get => _traceabilityPageNumber;
		private set
		{
			if (_traceabilityPageNumber == value) return;
			_traceabilityPageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasTraceabilityNextPage));
			RaiseTraceabilityCommands();
		}
	}

	public long TraceabilityTotalCount
	{
		get => _traceabilityTotalCount;
		private set
		{
			if (_traceabilityTotalCount == value) return;
			_traceabilityTotalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasTraceabilityNextPage));
			RaiseTraceabilityCommands();
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
			_ = _searchDebouncer.DebounceAsync(LoadInventoryAsync);
		}
	}

	public string TraceabilitySearchText
	{
		get => _traceabilitySearchText;
		set
		{
			if (_traceabilitySearchText == value) return;
			_traceabilitySearchText = value;
			OnPropertyChanged();
			TraceabilityPageNumber = 1;
			_ = _traceabilitySearchDebouncer.DebounceAsync(LoadTraceabilityAsync);
		}
	}

	public string BlockReason
	{
		get => _blockReason;
		set
		{
			if (_blockReason == value) return;
			_blockReason = value;
			OnPropertyChanged();
			RaiseTraceabilityCommands();
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

	public ItemTraceabilityBalance? SelectedTraceability
	{
		get => _selectedTraceability;
		set
		{
			if (_selectedTraceability == value) return;
			_selectedTraceability = value;
			_blockReason = value?.BlockReason ?? string.Empty;
			OnPropertyChanged();
			OnPropertyChanged(nameof(BlockReason));
			OnPropertyChanged(nameof(HasSelectedTraceability));
			RaiseTraceabilityCommands();
			_ = LoadTraceabilityHistoryAsync(value);
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		await Task.WhenAll(LoadInventoryAsync(cancellationToken), LoadTraceabilityAsync(cancellationToken));
	}

	private async Task LoadInventoryAsync(CancellationToken cancellationToken = default)
	{
		var request = _listRequest.Begin(cancellationToken);
		BeginOperation("Loading inventory");
		var selectedId = SelectedItem?.InventoryId;
		try
		{
			var page = await _stockService.SearchInventoryOverviewAsync(SearchText, PageNumber, PageSize, request.Token);
			if (!request.IsCurrent) return;
			CollectionSynchronizer.Replace(Items, page.Items.Select(item => new InventoryOverviewItemViewModel(item)).ToArray());
			TotalCount = page.TotalCount;
			SelectedItem = selectedId is null ? null : Items.FirstOrDefault(x => x.InventoryId == selectedId);
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

	private async Task LoadTraceabilityAsync(CancellationToken cancellationToken = default)
	{
		var request = _traceabilityRequest.Begin(cancellationToken);
		var selectedUnitId = SelectedTraceability?.TrackingUnitId;
		var selectedInventoryId = SelectedTraceability?.InventoryId;
		try
		{
			var page = await _stockService.SearchTraceabilityBalancesAsync(TraceabilitySearchText, TraceabilityPageNumber, TraceabilityPageSize, request.Token);
			if (!request.IsCurrent) return;
			CollectionSynchronizer.Replace(TraceabilityBalances, page.Items);
			TraceabilityTotalCount = page.TotalCount;
			OnPropertyChanged(nameof(HasTraceabilityBalances));
			OnPropertyChanged(nameof(HasNoTraceabilityBalances));
			SelectedTraceability = selectedUnitId is null ? null : TraceabilityBalances.FirstOrDefault(value => value.TrackingUnitId == selectedUnitId && value.InventoryId == selectedInventoryId);
			if (SelectedTraceability is null)
			{
				TraceabilityHistory.Clear();
				OnPropertyChanged(nameof(HasTraceabilityHistory));
				OnPropertyChanged(nameof(HasNoTraceabilityHistory));
			}
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Serial/lot traceability could not be loaded"); }
	}

	private async Task LoadTraceabilityHistoryAsync(ItemTraceabilityBalance? selected)
	{
		var request = _traceabilityHistoryRequest.Begin();
		if (selected is null)
		{
			TraceabilityHistory.Clear();
			OnPropertyChanged(nameof(HasTraceabilityHistory));
			OnPropertyChanged(nameof(HasNoTraceabilityHistory));
			return;
		}
		try
		{
			var page = await _stockService.SearchTraceabilityHistoryAsync(selected.TrackingUnitId, 1, 250, request.Token);
			if (!request.IsCurrent || SelectedTraceability?.TrackingUnitId != selected.TrackingUnitId) return;
			CollectionSynchronizer.Replace(TraceabilityHistory, page.Items);
			OnPropertyChanged(nameof(HasTraceabilityHistory));
			OnPropertyChanged(nameof(HasNoTraceabilityHistory));
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Serial/lot history could not be loaded"); }
	}

	private async Task BlockTrackingUnitAsync(CancellationToken cancellationToken)
	{
		if (!CanBlockTrackingUnit || SelectedTraceability is null) return;
		BeginOperation("Blocking serial/lot unit");
		try
		{
			var unitId = SelectedTraceability.TrackingUnitId;
			var inventoryId = SelectedTraceability.InventoryId;
			await _stockService.SetTraceabilityBlockedAsync(SelectedTraceability, true, BlockReason, cancellationToken);
			await LoadTraceabilityAsync(cancellationToken);
			SelectedTraceability = TraceabilityBalances.FirstOrDefault(value => value.TrackingUnitId == unitId && value.InventoryId == inventoryId);
			CompleteOperation(false, "Serial/lot unit blocked");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Serial/lot unit could not be blocked"); }
	}

	private async Task UnblockTrackingUnitAsync(CancellationToken cancellationToken)
	{
		if (!CanUnblockTrackingUnit || SelectedTraceability is null) return;
		BeginOperation("Unblocking serial/lot unit");
		try
		{
			var unitId = SelectedTraceability.TrackingUnitId;
			var inventoryId = SelectedTraceability.InventoryId;
			await _stockService.SetTraceabilityBlockedAsync(SelectedTraceability, false, null, cancellationToken);
			await LoadTraceabilityAsync(cancellationToken);
			SelectedTraceability = TraceabilityBalances.FirstOrDefault(value => value.TrackingUnitId == unitId && value.InventoryId == inventoryId);
			CompleteOperation(false, "Serial/lot unit unblocked");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Serial/lot unit could not be unblocked"); }
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
			var details = await _stockService.GetInventoryDetailsAsync(selected.InventoryId, request.Token);
			if (!request.IsCurrent || SelectedItem?.InventoryId != selected.InventoryId) return;
			Details.Load(details);
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Inventory details could not be loaded"); }
		finally { if (request.IsCurrent) IsLoadingDetails = false; }
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadInventoryAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadInventoryAsync(cancellationToken);
	}

	private async Task PreviousTraceabilityPageAsync(CancellationToken cancellationToken)
	{
		if (TraceabilityPageNumber <= 1) return;
		TraceabilityPageNumber--;
		await LoadTraceabilityAsync(cancellationToken);
	}

	private async Task NextTraceabilityPageAsync(CancellationToken cancellationToken)
	{
		if (!HasTraceabilityNextPage) return;
		TraceabilityPageNumber++;
		await LoadTraceabilityAsync(cancellationToken);
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	private void RaiseTraceabilityCommands()
	{
		PreviousTraceabilityPageCommand.RaiseCanExecuteChanged();
		NextTraceabilityPageCommand.RaiseCanExecuteChanged();
		BlockTrackingUnitCommand.RaiseCanExecuteChanged();
		UnblockTrackingUnitCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		_traceabilitySearchDebouncer.Dispose();
		_listRequest.Dispose();
		_detailsRequest.Dispose();
		_traceabilityRequest.Dispose();
		_traceabilityHistoryRequest.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
		PreviousTraceabilityPageCommand.Dispose();
		NextTraceabilityPageCommand.Dispose();
		BlockTrackingUnitCommand.Dispose();
		UnblockTrackingUnitCommand.Dispose();
	}
}
