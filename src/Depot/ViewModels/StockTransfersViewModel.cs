// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class StockTransfersViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 50;
	private readonly StockTransferService _transfers;
	private readonly WarehouseService _warehouses;
	private readonly IFileDialogService _dialogs;
	private readonly ReasonCodeService _reasonCodes;
	private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
	private CancellationTokenSource? _selectionCancellation;
	private CancellationTokenSource? _inventoryCancellation;
	private StockTransferOverviewItem? _selectedTransfer;
	private StockTransfer _draft = NewDraft();
	private Warehouse? _selectedSourceWarehouse;
	private Warehouse? _selectedDestinationWarehouse;
	private StockTransferLineEditorViewModel? _selectedLine;
	private StockTransferInventoryOption? _selectedSourceInventory;
	private StockTransferInventoryOption? _selectedDestinationInventory;
	private string _searchText = string.Empty;
	private StockTransferStatusFilter _selectedStatusFilter;
	private int _lineQuantity = 1;
	private int _pageNumber = 1;
	private long _totalCount;
	private ReasonCode? _selectedReversalReasonCode;
	private string _reversalReason = string.Empty;

	public StockTransfersViewModel(
		StockTransferService transfers,
		WarehouseService warehouses,
		IFileDialogService dialogs,
		ReasonCodeService reasonCodes)
	{
		_transfers = transfers;
		_warehouses = warehouses;
		_dialogs = dialogs;
		_reasonCodes = reasonCodes;
		StatusFilters =
		[
			new StockTransferStatusFilter("All statuses", null),
			.. Enum.GetValues<StockTransferStatus>()
				.Select(status => new StockTransferStatusFilter(StatusLabel(status), status))
		];
		_selectedStatusFilter = StatusFilters[0];
		NewTransferCommand = new RelayCommand(NewTransfer);
		SaveTransferCommand = new AsyncRelayCommand(SaveTransferAsync, () => IsDraft && Lines.Count > 0);
		PostTransferCommand = new AsyncRelayCommand(PostTransferAsync, () => Draft.Id > 0 && IsDraft);
		CancelTransferCommand = new AsyncRelayCommand(CancelTransferAsync, () => Draft.Id > 0 && IsDraft);
		ReverseTransferCommand = new AsyncRelayCommand(ReverseTransferAsync, () => CanReverse && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason));
		AddLineCommand = new RelayCommand(AddOrUpdateLine, CanAddLine);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<StockTransferOverviewItem> Transfers { get; } = new();
	public ObservableCollection<Warehouse> Warehouses { get; } = new();
	public ObservableCollection<StockTransferLineEditorViewModel> Lines { get; } = new();
	public ObservableCollection<StockTransferInventoryOption> SourceInventoryOptions { get; } = new();
	public ObservableCollection<StockTransferInventoryOption> DestinationInventoryOptions { get; } = new();
	public ObservableCollection<MovementOverviewItem> Movements { get; } = new();
	public ObservableCollection<ReasonCode> ReversalReasonCodes { get; } = new();
	public IReadOnlyList<StockTransferStatusFilter> StatusFilters { get; }
	public IEnumerable<Warehouse> DestinationWarehouses =>
		Warehouses.Where(warehouse => warehouse.Id != SelectedSourceWarehouse?.Id);

	public RelayCommand NewTransferCommand { get; }
	public AsyncRelayCommand SaveTransferCommand { get; }
	public AsyncRelayCommand PostTransferCommand { get; }
	public AsyncRelayCommand CancelTransferCommand { get; }
	public AsyncRelayCommand ReverseTransferCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public StockTransfer Draft
	{
		get => _draft;
		private set
		{
			_draft = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsDraft));
			OnPropertyChanged(nameof(IsReadOnly));
			OnPropertyChanged(nameof(CanReverse));
			OnPropertyChanged(nameof(EditorTitle));
			RaiseCommands();
		}
	}

	public bool IsDraft => Draft.Status == StockTransferStatus.Draft;
	public bool IsReadOnly => !IsDraft;
	public bool CanReverse => Draft.Status == StockTransferStatus.Posted && !Draft.IsReversed;
	public bool HasMovements => Movements.Count > 0;
	public string EditorTitle => Draft.Id == 0 ? "New Stock Transfer" : Draft.TransferNumber;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} transfers";
	public ReasonCode? SelectedReversalReasonCode { get => _selectedReversalReasonCode; set { if (_selectedReversalReasonCode == value) return; _selectedReversalReasonCode = value; OnPropertyChanged(); ReverseTransferCommand.RaiseCanExecuteChanged(); } }
	public string ReversalReason { get => _reversalReason; set { if (_reversalReason == value) return; _reversalReason = value; OnPropertyChanged(); ReverseTransferCommand.RaiseCanExecuteChanged(); } }

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = _search.DebounceAsync(LoadPageAsync);
		}
	}

	public StockTransferStatusFilter SelectedStatusFilter
	{
		get => _selectedStatusFilter;
		set
		{
			if (_selectedStatusFilter == value) return;
			_selectedStatusFilter = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = LoadPageAsync();
		}
	}

	public StockTransferOverviewItem? SelectedTransfer
	{
		get => _selectedTransfer;
		set
		{
			if (_selectedTransfer == value) return;
			_selectedTransfer = value;
			OnPropertyChanged();
			_selectionCancellation?.Cancel();
			_selectionCancellation?.Dispose();
			_selectionCancellation = new CancellationTokenSource();
			_ = LoadSelectedTransferAsync(value, _selectionCancellation.Token);
		}
	}

	public Warehouse? SelectedSourceWarehouse
	{
		get => _selectedSourceWarehouse;
		set
		{
			if (_selectedSourceWarehouse == value) return;
			_selectedSourceWarehouse = value;
			Draft.SourceWarehouseId = value?.Id ?? 0;
			OnPropertyChanged();
			OnPropertyChanged(nameof(DestinationWarehouses));
			if (SelectedDestinationWarehouse?.Id == value?.Id) SelectedDestinationWarehouse = null;
			_ = ReloadInventoryOptionsAsync();
		}
	}

	public Warehouse? SelectedDestinationWarehouse
	{
		get => _selectedDestinationWarehouse;
		set
		{
			if (_selectedDestinationWarehouse == value) return;
			_selectedDestinationWarehouse = value;
			Draft.DestinationWarehouseId = value?.Id ?? 0;
			OnPropertyChanged();
			_ = ReloadInventoryOptionsAsync();
		}
	}

	public StockTransferLineEditorViewModel? SelectedLine
	{
		get => _selectedLine;
		set
		{
			if (_selectedLine == value) return;
			_selectedLine = value;
			OnPropertyChanged();
			SelectedSourceInventory = value is null
				? null
				: SourceInventoryOptions.FirstOrDefault(option => option.InventoryId == value.SourceInventoryId);
			SelectedDestinationInventory = value is null
				? null
				: DestinationInventoryOptions.FirstOrDefault(option => option.InventoryId == value.DestinationInventoryId);
			LineQuantity = value?.Quantity ?? 1;
			OnPropertyChanged(nameof(SaveLineText));
			RaiseCommands();
		}
	}

	public StockTransferInventoryOption? SelectedSourceInventory
	{
		get => _selectedSourceInventory;
		set
		{
			if (_selectedSourceInventory == value) return;
			_selectedSourceInventory = value;
			OnPropertyChanged();
			FilterDestinationInventories(value?.ItemId);
			RaiseCommands();
		}
	}

	public StockTransferInventoryOption? SelectedDestinationInventory
	{
		get => _selectedDestinationInventory;
		set
		{
			if (_selectedDestinationInventory == value) return;
			_selectedDestinationInventory = value;
			OnPropertyChanged();
			RaiseCommands();
		}
	}

	public int LineQuantity
	{
		get => _lineQuantity;
		set
		{
			if (_lineQuantity == value) return;
			_lineQuantity = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedSourceRemainingStock));
			OnPropertyChanged(nameof(HasSufficientSelectedStock));
			RaiseCommands();
		}
	}

	public long? SelectedSourceRemainingStock => SelectedSourceInventory is null
		? null
		: SelectedSourceInventory.CurrentStock -
			Lines.Where(line => line != SelectedLine && line.SourceInventoryId == SelectedSourceInventory.InventoryId)
				.Sum(line => (long)line.Quantity) -
			LineQuantity;
	public bool HasSufficientSelectedStock =>
		SelectedSourceRemainingStock is >= 0;
	public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";

	public int PageNumber
	{
		get => _pageNumber;
		private set
		{
			if (_pageNumber == value) return;
			_pageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			OnPropertyChanged(nameof(PageDisplay));
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
			OnPropertyChanged(nameof(PageDisplay));
			RaisePagingCommands();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Transfers werden geladen");
		try
		{
			var warehousesTask = _warehouses.SearchAsync(null, cancellationToken);
			var transfersTask = _transfers.SearchAsync(
				SearchText,
				SelectedStatusFilter.Status,
				PageNumber,
				PageSize,
				cancellationToken);
			var reasonCodesTask = _reasonCodes.GetActiveAsync(cancellationToken);
			await Task.WhenAll(warehousesTask, transfersTask, reasonCodesTask);
			Warehouses.Clear();
			foreach (var warehouse in (await warehousesTask).Where(warehouse => warehouse.IsActive))
				Warehouses.Add(warehouse);
			ReversalReasonCodes.Clear();
			foreach (var reasonCode in await reasonCodesTask) ReversalReasonCodes.Add(reasonCode);
			ApplyPage(await transfersTask);
			CompleteOperation(Transfers.Count == 0, $"{TotalCount:N0} transfers");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(Transfers.Count == 0);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Transfers could not be loaded");
		}
	}

	private async Task LoadPageAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Transfers werden geladen");
		try
		{
			var page = await _transfers.SearchAsync(
				SearchText,
				SelectedStatusFilter.Status,
				PageNumber,
				PageSize,
				cancellationToken);
			ApplyPage(page);
			CompleteOperation(Transfers.Count == 0, $"{TotalCount:N0} transfers");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Transfers could not be loaded");
		}
	}

	private async Task LoadSelectedTransferAsync(
		StockTransferOverviewItem? overview,
		CancellationToken cancellationToken)
	{
		if (overview is null)
		{
			NewTransfer();
			return;
		}

		BeginOperation("Transfer wird geladen");
		try
		{
			var transfer = await _transfers.GetByIdAsync(overview.Id, cancellationToken)
				?? throw new InvalidOperationException("The stock transfer was not found.");
			await LoadEditorAsync(transfer, cancellationToken);
			CompleteOperation(false, $"{transfer.TransferNumber} loaded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Transfer could not be loaded");
		}
	}

	private async Task LoadEditorAsync(StockTransfer transfer, CancellationToken cancellationToken)
	{
		Draft = Copy(transfer);
		_selectedSourceWarehouse = Warehouses.FirstOrDefault(warehouse => warehouse.Id == transfer.SourceWarehouseId);
		_selectedDestinationWarehouse = Warehouses.FirstOrDefault(warehouse => warehouse.Id == transfer.DestinationWarehouseId);
		OnPropertyChanged(nameof(SelectedSourceWarehouse));
		OnPropertyChanged(nameof(SelectedDestinationWarehouse));
		OnPropertyChanged(nameof(DestinationWarehouses));
		var sourceTask = _transfers.GetInventoryOptionsAsync(transfer.SourceWarehouseId, null, cancellationToken);
		var destinationTask = _transfers.GetInventoryOptionsAsync(transfer.DestinationWarehouseId, null, cancellationToken);
		var movementsTask = transfer.Status == StockTransferStatus.Posted
			? _transfers.GetMovementsAsync(transfer.Id, cancellationToken)
			: Task.FromResult<IReadOnlyList<MovementOverviewItem>>([]);
		await Task.WhenAll(sourceTask, destinationTask, movementsTask);
		Replace(SourceInventoryOptions, await sourceTask);
		_allDestinationOptions = await destinationTask;
		DestinationInventoryOptions.Clear();
		Lines.Clear();
		foreach (var line in transfer.Lines)
		{
			var source = SourceInventoryOptions.FirstOrDefault(option => option.InventoryId == line.SourceInventoryId);
			var destination = _allDestinationOptions.FirstOrDefault(option => option.InventoryId == line.DestinationInventoryId);
			Lines.Add(new StockTransferLineEditorViewModel(line, source, destination));
		}
		RefreshLineStockProjections();
		Replace(Movements, await movementsTask);
		OnPropertyChanged(nameof(HasMovements));
		SelectedLine = null;
		RaiseCommands();
	}

	private IReadOnlyList<StockTransferInventoryOption> _allDestinationOptions = [];

	private async Task ReloadInventoryOptionsAsync()
	{
		_inventoryCancellation?.Cancel();
		_inventoryCancellation?.Dispose();
		_inventoryCancellation = new CancellationTokenSource();
		var cancellationToken = _inventoryCancellation.Token;
		BeginOperation("Bestände werden geladen");
		try
		{
			var sourceTask = SelectedSourceWarehouse is null
				? Task.FromResult<IReadOnlyList<StockTransferInventoryOption>>([])
				: _transfers.GetInventoryOptionsAsync(SelectedSourceWarehouse.Id, null, cancellationToken);
			var destinationTask = SelectedDestinationWarehouse is null
				? Task.FromResult<IReadOnlyList<StockTransferInventoryOption>>([])
				: _transfers.GetInventoryOptionsAsync(SelectedDestinationWarehouse.Id, null, cancellationToken);
			await Task.WhenAll(sourceTask, destinationTask);
			Replace(SourceInventoryOptions, await sourceTask);
			_allDestinationOptions = await destinationTask;
			SelectedSourceInventory = null;
			FilterDestinationInventories(null);
			CompleteOperation(false, "Inventory options loaded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory options could not be loaded");
		}
	}

	private void FilterDestinationInventories(long? itemId)
	{
		var selectedId = SelectedLine?.DestinationInventoryId;
		DestinationInventoryOptions.Clear();
		if (itemId is not null)
		{
			foreach (var option in _allDestinationOptions.Where(option => option.ItemId == itemId))
				DestinationInventoryOptions.Add(option);
		}
		_selectedDestinationInventory = DestinationInventoryOptions.FirstOrDefault(option => option.InventoryId == selectedId);
		OnPropertyChanged(nameof(SelectedDestinationInventory));
		OnPropertyChanged(nameof(SelectedSourceRemainingStock));
		OnPropertyChanged(nameof(HasSufficientSelectedStock));
	}

	private void NewTransfer()
	{
		_selectedTransfer = null;
		OnPropertyChanged(nameof(SelectedTransfer));
		Draft = NewDraft();
		Lines.Clear();
		Movements.Clear();
		OnPropertyChanged(nameof(HasMovements));
		SelectedLine = null;
		SelectedSourceWarehouse = null;
		SelectedDestinationWarehouse = null;
		CompleteOperation(false, "New transfer");
	}

	private bool CanAddLine() =>
		IsDraft && SelectedSourceInventory is not null && SelectedDestinationInventory is not null && LineQuantity > 0;

	private void AddOrUpdateLine()
	{
		if (!CanAddLine()) return;
		var model = SelectedLine?.ToModel() ?? new StockTransferLine();
		model.SourceInventoryId = SelectedSourceInventory?.InventoryId ?? 0;
		model.DestinationInventoryId = SelectedDestinationInventory?.InventoryId ?? 0;
		model.Quantity = LineQuantity;
		model.LineNumber = SelectedLine?.LineNumber ?? Lines.Count + 1;
		var editor = new StockTransferLineEditorViewModel(
			model,
			SelectedSourceInventory,
			SelectedDestinationInventory);
		if (SelectedLine is null)
			Lines.Add(editor);
		else
			Lines[Lines.IndexOf(SelectedLine)] = editor;
		SelectedLine = null;
		RefreshLineStockProjections();
		RaiseCommands();
	}

	private void RemoveLine()
	{
		if (SelectedLine is null) return;
		Lines.Remove(SelectedLine);
		var lineNumber = 1;
		foreach (var line in Lines) line.LineNumber = lineNumber++;
		SelectedLine = null;
		RefreshLineStockProjections();
		RaiseCommands();
	}

	private void RefreshLineStockProjections()
	{
		foreach (var group in Lines.GroupBy(line => line.SourceInventoryId))
		{
			var allocated = group.Sum(line => (long)line.Quantity);
			foreach (var line in group) line.SetAllocatedQuantity(allocated);
		}
		OnPropertyChanged(nameof(SelectedSourceRemainingStock));
		OnPropertyChanged(nameof(HasSufficientSelectedStock));
	}

	private async Task SaveTransferAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Transfer wird gespeichert");
		try
		{
			var candidate = Copy(Draft);
			candidate.Lines = Lines.Select(line => line.ToModel()).ToArray();
			var saved = await _transfers.SaveDraftAsync(candidate, cancellationToken);
			await ApplySavedTransferAsync(saved, cancellationToken);
			CompleteOperation(false, $"{saved.TransferNumber} saved");
		}
		catch (ConcurrencyConflictException)
		{
			FailOperation(
				new InvalidOperationException("The transfer was changed by another user. Reload it before continuing."),
				"Transfer could not be saved");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Transfer could not be saved");
		}
	}

	private async Task PostTransferAsync(CancellationToken cancellationToken)
	{
		if (!_dialogs.Confirm(new ConfirmationDialogRequest(
			"Post Stock Transfer",
			$"Post {Draft.TransferNumber}? This creates the stock movements and cannot be undone.",
			true))) return;
		BeginOperation("Transfer wird gebucht – Bestände werden geprüft");
		try
		{
			var posted = await _transfers.PostAsync(Draft.Id, Draft.Version, cancellationToken);
			await ApplySavedTransferAsync(posted, cancellationToken);
			CompleteOperation(false, $"{posted.TransferNumber} posted");
		}
		catch (ConcurrencyConflictException)
		{
			FailOperation(
				new InvalidOperationException("The transfer was changed or posted by another user. Reload it before continuing."),
				"Transfer could not be posted");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Transfer could not be posted");
		}
	}

	private async Task CancelTransferAsync(CancellationToken cancellationToken)
	{
		if (!_dialogs.Confirm(new ConfirmationDialogRequest(
			"Cancel Stock Transfer",
			$"Cancel draft {Draft.TransferNumber}?",
			true))) return;
		BeginOperation("Transfer wird storniert");
		try
		{
			var cancelled = await _transfers.CancelAsync(Draft.Id, Draft.Version, cancellationToken);
			await ApplySavedTransferAsync(cancelled, cancellationToken);
			CompleteOperation(false, $"{cancelled.TransferNumber} cancelled");
		}
		catch (ConcurrencyConflictException)
		{
			FailOperation(
				new InvalidOperationException("The transfer was changed by another user. Reload it before continuing."),
				"Transfer could not be cancelled");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Transfer could not be cancelled");
		}
	}

	private async Task ReverseTransferAsync(CancellationToken cancellationToken)
	{
		if (SelectedReversalReasonCode is null) return;
		if (!_dialogs.Confirm(new ConfirmationDialogRequest("Reverse Stock Transfer", $"Create counter-movements for {Draft.TransferNumber}?", true))) return;
		BeginOperation("Transfer wird gegengebucht");
		try
		{
			var reversed = await _transfers.ReverseAsync(Draft.Id, Draft.Version, SelectedReversalReasonCode.Id, ReversalReason, cancellationToken);
			await ApplySavedTransferAsync(reversed, cancellationToken);
			CompleteOperation(false, $"{reversed.TransferNumber} reversed");
		}
		catch (ConcurrencyConflictException)
		{
			FailOperation(new InvalidOperationException("The transfer was changed or reversed by another user. Reload it before continuing."), "Transfer could not be reversed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Transfer could not be reversed");
		}
	}

	private async Task ApplySavedTransferAsync(StockTransfer saved, CancellationToken cancellationToken)
	{
		var overview = await _transfers.GetOverviewByIdAsync(saved.Id, cancellationToken)
			?? throw new InvalidOperationException("The saved transfer could not be reloaded.");
		var existing = Transfers.FirstOrDefault(transfer => transfer.Id == overview.Id);
		if (MatchesCurrentFilter(overview))
		{
			if (existing is null)
			{
				Transfers.Insert(0, overview);
				TotalCount++;
			}
			else
			{
				Transfers[Transfers.IndexOf(existing)] = overview;
			}
		}
		else if (existing is not null)
		{
			Transfers.Remove(existing);
			TotalCount--;
		}

		_selectedTransfer = overview;
		OnPropertyChanged(nameof(SelectedTransfer));
		await LoadEditorAsync(saved, cancellationToken);
	}

	private bool MatchesCurrentFilter(StockTransferOverviewItem transfer)
	{
		if (SelectedStatusFilter.Status is not null && transfer.Status != SelectedStatusFilter.Status) return false;
		if (string.IsNullOrWhiteSpace(SearchText)) return true;
		var search = SearchText.Trim();
		return transfer.TransferNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			transfer.SourceWarehouseName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			transfer.DestinationWarehouseName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			(transfer.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadPageAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadPageAsync(cancellationToken);
	}

	private void ApplyPage(PageResult<StockTransferOverviewItem> page)
	{
		var selectedId = SelectedTransfer?.Id;
		Replace(Transfers, page.Items);
		TotalCount = page.TotalCount;
		if (selectedId is not null)
		{
			var selected = Transfers.FirstOrDefault(transfer => transfer.Id == selectedId);
			if (selected is not null) _selectedTransfer = selected;
			OnPropertyChanged(nameof(SelectedTransfer));
		}
	}

	private void RaiseCommands()
	{
		SaveTransferCommand.RaiseCanExecuteChanged();
		PostTransferCommand.RaiseCanExecuteChanged();
		CancelTransferCommand.RaiseCanExecuteChanged();
		ReverseTransferCommand.RaiseCanExecuteChanged();
		AddLineCommand.RaiseCanExecuteChanged();
		RemoveLineCommand.RaiseCanExecuteChanged();
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}

	private static StockTransfer NewDraft() => new() { TransferDate = DateTime.Today };

	private static StockTransfer Copy(StockTransfer source) => new()
	{
		Id = source.Id,
		TransferNumber = source.TransferNumber,
		SourceWarehouseId = source.SourceWarehouseId,
		DestinationWarehouseId = source.DestinationWarehouseId,
		TransferDate = source.TransferDate,
		Status = source.Status,
		CreatedByUserId = source.CreatedByUserId,
		PostedByUserId = source.PostedByUserId,
		Notes = source.Notes,
		ReversedAtUtc = source.ReversedAtUtc,
		ReversedByUserId = source.ReversedByUserId,
		ReversalReason = source.ReversalReason,
		Version = source.Version,
		Lines = source.Lines.Select(line => new StockTransferLine
		{
			Id = line.Id,
			StockTransferId = line.StockTransferId,
			LineNumber = line.LineNumber,
			SourceInventoryId = line.SourceInventoryId,
			DestinationInventoryId = line.DestinationInventoryId,
			Quantity = line.Quantity,
			Version = line.Version
		}).ToArray()
	};

	private static string StatusLabel(StockTransferStatus status) => status switch
	{
		StockTransferStatus.Draft => "Draft",
		StockTransferStatus.Posted => "Posted",
		StockTransferStatus.Cancelled => "Cancelled",
		_ => status.ToString()
	};

	public void Dispose()
	{
		_search.Dispose();
		_selectionCancellation?.Cancel();
		_selectionCancellation?.Dispose();
		_inventoryCancellation?.Cancel();
		_inventoryCancellation?.Dispose();
		SaveTransferCommand.Dispose();
		PostTransferCommand.Dispose();
		CancelTransferCommand.Dispose();
		ReverseTransferCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
	}
}

public sealed record StockTransferStatusFilter(string Name, StockTransferStatus? Status);

public sealed class StockTransferLineEditorViewModel : BaseViewModel
{
	private int _lineNumber;
	private long _allocatedQuantity;

	public StockTransferLineEditorViewModel(
		StockTransferLine line,
		StockTransferInventoryOption? source,
		StockTransferInventoryOption? destination)
	{
		Id = line.Id;
		StockTransferId = line.StockTransferId;
		_lineNumber = line.LineNumber;
		SourceInventoryId = line.SourceInventoryId;
		DestinationInventoryId = line.DestinationInventoryId;
		Quantity = line.Quantity;
		Version = line.Version;
		Source = source;
		Destination = destination;
	}

	public long Id { get; }
	public long StockTransferId { get; }
	public int LineNumber { get => _lineNumber; set { if (_lineNumber == value) return; _lineNumber = value; OnPropertyChanged(); } }
	public long SourceInventoryId { get; }
	public long DestinationInventoryId { get; }
	public int Quantity { get; }
	public long Version { get; }
	public StockTransferInventoryOption? Source { get; }
	public StockTransferInventoryOption? Destination { get; }
	public string ItemDisplay => Source is null ? SourceInventoryId.ToString() : $"{Source.PartNumber} — {Source.Description}";
	public string SourceDisplay => Source?.DisplayName ?? SourceInventoryId.ToString();
	public string DestinationDisplay => Destination?.DisplayName ?? DestinationInventoryId.ToString();
	public long AvailableStock => Source?.CurrentStock ?? 0;
	public long RemainingStock => AvailableStock - _allocatedQuantity;
	public bool HasSufficientStock => RemainingStock >= 0;
	public string AvailabilityStatus => HasSufficientStock ? "Sufficient" : "Insufficient";

	public void SetAllocatedQuantity(long allocatedQuantity)
	{
		if (_allocatedQuantity == allocatedQuantity) return;
		_allocatedQuantity = allocatedQuantity;
		OnPropertyChanged(nameof(RemainingStock));
		OnPropertyChanged(nameof(HasSufficientStock));
		OnPropertyChanged(nameof(AvailabilityStatus));
	}

	public StockTransferLine ToModel() => new()
	{
		Id = Id,
		StockTransferId = StockTransferId,
		LineNumber = LineNumber,
		SourceInventoryId = SourceInventoryId,
		DestinationInventoryId = DestinationInventoryId,
		Quantity = Quantity,
		Version = Version
	};
}
