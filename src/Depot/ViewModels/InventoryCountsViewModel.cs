// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class InventoryCountsViewModel : BaseViewModel, IDisposable
{
	private const int CountPageSize = 50;
	private const int LinePageSize = 100;
	private readonly InventoryCountService _counts;
	private readonly WarehouseService _warehouses;
	private readonly IFileDialogService _dialogs;
	private readonly ReasonCodeService _reasonCodes;
	private readonly AsyncDebouncer _countSearch = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _lineSearch = new(TimeSpan.FromMilliseconds(300));
	private CancellationTokenSource? _selectionCancellation;
	private InventoryCountOverviewItem? _selectedCount;
	private InventoryCount _editor = NewDraft();
	private Warehouse? _selectedDraftWarehouse;
	private InventoryCountStatusFilter _selectedStatusFilter;
	private InventoryCountWarehouseFilter _selectedWarehouseFilter;
	private InventoryCountLineRowViewModel? _selectedLine;
	private string _searchText = string.Empty;
	private string _lineSearchText = string.Empty;
	private bool _uncountedOnly;
	private bool _differencesOnly;
	private int _pageNumber = 1;
	private long _totalCount;
	private int _linePageNumber = 1;
	private long _lineTotalCount;
	private ReasonCode? _selectedReversalReasonCode;
	private string _reversalReason = string.Empty;

	public InventoryCountsViewModel(
		InventoryCountService counts,
		WarehouseService warehouses,
		IFileDialogService dialogs,
		ReasonCodeService reasonCodes)
	{
		_counts = counts;
		_warehouses = warehouses;
		_dialogs = dialogs;
		_reasonCodes = reasonCodes;
		StatusFilters =
		[
			new InventoryCountStatusFilter("All statuses", null),
			.. Enum.GetValues<InventoryCountStatus>()
				.Select(status => new InventoryCountStatusFilter(status.ToString(), status))
		];
		_selectedStatusFilter = StatusFilters[0];
		WarehouseFilters = [new InventoryCountWarehouseFilter("All warehouses", null)];
		_selectedWarehouseFilter = WarehouseFilters[0];
		NewCommand = new RelayCommand(NewCount);
		SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => IsDraft && SelectedDraftWarehouse is not null);
		StartCommand = new AsyncRelayCommand(StartAsync, () => Editor.Id > 0 && IsDraft);
		CancelCommand = new AsyncRelayCommand(CancelAsync, () => CanCancel);
		MoveToReviewCommand = new AsyncRelayCommand(MoveToReviewAsync, () => IsCounting && UncountedLineCount == 0);
		ReturnToCountingCommand = new AsyncRelayCommand(ReturnToCountingAsync, () => IsReview);
		PostCommand = new AsyncRelayCommand(PostAsync, () => IsReview);
		ReverseCommand = new AsyncRelayCommand(ReverseAsync, () => CanReverse && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason));
		SaveQuantityCommand = new AsyncRelayCommand(SaveQuantityAsync, () => IsCounting && SelectedLine is not null);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		PreviousLinePageCommand = new AsyncRelayCommand(PreviousLinePageAsync, () => LinePageNumber > 1);
		NextLinePageCommand = new AsyncRelayCommand(NextLinePageAsync, () => HasNextLinePage);
	}

	public ObservableCollection<InventoryCountOverviewItem> InventoryCounts { get; } = new();
	public ObservableCollection<Warehouse> Warehouses { get; } = new();
	public ObservableCollection<InventoryCountWarehouseFilter> WarehouseFilters { get; } = new();
	public ObservableCollection<InventoryCountLineRowViewModel> Lines { get; } = new();
	public ObservableCollection<ReasonCode> ReversalReasonCodes { get; } = new();
	public IReadOnlyList<InventoryCountStatusFilter> StatusFilters { get; }

	public RelayCommand NewCommand { get; }
	public AsyncRelayCommand SaveDraftCommand { get; }
	public AsyncRelayCommand StartCommand { get; }
	public AsyncRelayCommand CancelCommand { get; }
	public AsyncRelayCommand MoveToReviewCommand { get; }
	public AsyncRelayCommand ReturnToCountingCommand { get; }
	public AsyncRelayCommand PostCommand { get; }
	public AsyncRelayCommand ReverseCommand { get; }
	public AsyncRelayCommand SaveQuantityCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public AsyncRelayCommand PreviousLinePageCommand { get; }
	public AsyncRelayCommand NextLinePageCommand { get; }

	public InventoryCount Editor
	{
		get => _editor;
		private set
		{
			_editor = value;
			OnPropertyChanged();
			RaiseEditorProperties();
		}
	}

	public InventoryCountOverviewItem? SelectedCount
	{
		get => _selectedCount;
		set
		{
			if (_selectedCount == value) return;
			_selectedCount = value;
			OnPropertyChanged();
			_selectionCancellation?.Cancel();
			_selectionCancellation?.Dispose();
			_selectionCancellation = new CancellationTokenSource();
			_ = LoadSelectedAsync(value, _selectionCancellation.Token);
		}
	}

	public Warehouse? SelectedDraftWarehouse
	{
		get => _selectedDraftWarehouse;
		set
		{
			if (_selectedDraftWarehouse == value) return;
			_selectedDraftWarehouse = value;
			Editor.WarehouseId = value?.Id ?? 0;
			OnPropertyChanged();
			RaiseCommands();
		}
	}

	public InventoryCountLineRowViewModel? SelectedLine
	{
		get => _selectedLine;
		set
		{
			if (_selectedLine == value) return;
			_selectedLine = value;
			OnPropertyChanged();
			RaiseCommands();
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
			_ = _countSearch.DebounceAsync(LoadCountPageAsync);
		}
	}

	public string LineSearchText
	{
		get => _lineSearchText;
		set
		{
			if (_lineSearchText == value) return;
			_lineSearchText = value;
			OnPropertyChanged();
			LinePageNumber = 1;
			_ = _lineSearch.DebounceAsync(LoadLinePageAsync);
		}
	}

	public InventoryCountStatusFilter SelectedStatusFilter
	{
		get => _selectedStatusFilter;
		set
		{
			if (_selectedStatusFilter == value) return;
			_selectedStatusFilter = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = LoadCountPageAsync();
		}
	}

	public InventoryCountWarehouseFilter SelectedWarehouseFilter
	{
		get => _selectedWarehouseFilter;
		set
		{
			if (_selectedWarehouseFilter == value) return;
			_selectedWarehouseFilter = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = LoadCountPageAsync();
		}
	}

	public bool UncountedOnly
	{
		get => _uncountedOnly;
		set
		{
			if (_uncountedOnly == value) return;
			_uncountedOnly = value;
			if (value && _differencesOnly)
			{
				_differencesOnly = false;
				OnPropertyChanged(nameof(DifferencesOnly));
			}
			OnPropertyChanged();
			LinePageNumber = 1;
			_ = LoadLinePageAsync();
		}
	}

	public bool DifferencesOnly
	{
		get => _differencesOnly;
		set
		{
			if (_differencesOnly == value) return;
			_differencesOnly = value;
			if (value && _uncountedOnly)
			{
				_uncountedOnly = false;
				OnPropertyChanged(nameof(UncountedOnly));
			}
			OnPropertyChanged();
			LinePageNumber = 1;
			_ = LoadLinePageAsync();
		}
	}

	public bool IsDraft => Editor.Status == InventoryCountStatus.Draft;
	public bool IsCounting => Editor.Status == InventoryCountStatus.Counting;
	public bool IsReview => Editor.Status == InventoryCountStatus.Review;
	public bool HasSelectedCount => Editor.Id > 0;
	public bool HasCountLines => Editor.StartedAtUtc is not null &&
		Editor.Status is (InventoryCountStatus.Counting or InventoryCountStatus.Review or InventoryCountStatus.Posted or InventoryCountStatus.Cancelled);
	public bool CanCancel => Editor.Id > 0 && Editor.Status is not InventoryCountStatus.Posted and not InventoryCountStatus.Cancelled;
	public bool CanReverse => Editor.Status == InventoryCountStatus.Posted && !Editor.IsReversed;
	public bool IsDraftReadOnly => !IsDraft;
	public string EditorTitle => Editor.Id == 0 ? "New Inventory Count" : Editor.CountNumber;
	public int UncountedLineCount => SelectedCount is null ? 0 : SelectedCount.TotalLineCount - SelectedCount.CountedLineCount;
	public bool HasNextPage => (long)PageNumber * CountPageSize < TotalCount;
	public bool HasNextLinePage => (long)LinePageNumber * LinePageSize < LineTotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} counts";
	public string LinePageDisplay => $"Page {LinePageNumber} · {LineTotalCount:N0} positions";
	public string CountProgressDisplay => SelectedCount?.ProgressDisplay ?? "Not started";
	public string DifferenceSummary => SelectedCount is null ? string.Empty : $"{SelectedCount.DifferenceLineCount:N0} differences · {UncountedLineCount:N0} uncounted";
	public ReasonCode? SelectedReversalReasonCode { get => _selectedReversalReasonCode; set { if (_selectedReversalReasonCode == value) return; _selectedReversalReasonCode = value; OnPropertyChanged(); ReverseCommand.RaiseCanExecuteChanged(); } }
	public string ReversalReason { get => _reversalReason; set { if (_reversalReason == value) return; _reversalReason = value; OnPropertyChanged(); ReverseCommand.RaiseCanExecuteChanged(); } }

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

	public int LinePageNumber
	{
		get => _linePageNumber;
		private set
		{
			if (_linePageNumber == value) return;
			_linePageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextLinePage));
			OnPropertyChanged(nameof(LinePageDisplay));
			RaisePagingCommands();
		}
	}

	public long LineTotalCount
	{
		get => _lineTotalCount;
		private set
		{
			if (_lineTotalCount == value) return;
			_lineTotalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextLinePage));
			OnPropertyChanged(nameof(LinePageDisplay));
			RaisePagingCommands();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Inventuren werden geladen");
		try
		{
			var warehousesTask = _warehouses.GetActiveOptionsAsync(200, cancellationToken);
			var countsTask = SearchCountsAsync(cancellationToken);
			var reasonCodesTask = _reasonCodes.GetActiveAsync(cancellationToken);
			await Task.WhenAll(warehousesTask, countsTask, reasonCodesTask);
			Warehouses.Clear();
			WarehouseFilters.Clear();
			WarehouseFilters.Add(new InventoryCountWarehouseFilter("All warehouses", null));
			foreach (var warehouse in await warehousesTask)
			{
				if (warehouse.IsActive) Warehouses.Add(warehouse);
				WarehouseFilters.Add(new InventoryCountWarehouseFilter(warehouse.Name, warehouse.Id));
			}
			_selectedWarehouseFilter = WarehouseFilters[0];
			ReversalReasonCodes.Clear();
			foreach (var reasonCode in await reasonCodesTask) ReversalReasonCodes.Add(reasonCode);
			OnPropertyChanged(nameof(SelectedWarehouseFilter));
			ApplyCountPage(await countsTask);
			CompleteOperation(InventoryCounts.Count == 0, $"{TotalCount:N0} inventory counts");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(InventoryCounts.Count == 0);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory counts could not be loaded");
		}
	}

	private Task<PageResult<InventoryCountOverviewItem>> SearchCountsAsync(CancellationToken cancellationToken) =>
		_counts.SearchAsync(
			SearchText,
			SelectedStatusFilter.Status,
			SelectedWarehouseFilter.WarehouseId,
			PageNumber,
			CountPageSize,
			cancellationToken);

	private async Task LoadCountPageAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Inventuren werden geladen");
		try
		{
			ApplyCountPage(await SearchCountsAsync(cancellationToken));
			CompleteOperation(InventoryCounts.Count == 0, $"{TotalCount:N0} inventory counts");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory counts could not be loaded");
		}
	}

	private async Task LoadSelectedAsync(
		InventoryCountOverviewItem? overview,
		CancellationToken cancellationToken)
	{
		if (overview is null)
		{
			NewCount();
			return;
		}

		BeginOperation("Inventur wird geladen");
		try
		{
			var count = await _counts.GetHeaderByIdAsync(overview.Id, cancellationToken)
				?? throw new InvalidOperationException("The inventory count was not found.");
			Editor = Copy(count);
			_selectedDraftWarehouse = Warehouses.FirstOrDefault(warehouse => warehouse.Id == count.WarehouseId);
			OnPropertyChanged(nameof(SelectedDraftWarehouse));
			LinePageNumber = 1;
			await LoadLinePageCoreAsync(cancellationToken);
			CompleteOperation(false, $"{count.CountNumber} loaded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory count could not be loaded");
		}
	}

	private async Task LoadLinePageAsync(CancellationToken cancellationToken = default)
	{
		if (Editor.Id == 0)
		{
			Lines.Clear();
			LineTotalCount = 0;
			return;
		}

		BeginOperation("Zählpositionen werden geladen");
		try
		{
			await LoadLinePageCoreAsync(cancellationToken);
			CompleteOperation(Lines.Count == 0, $"{LineTotalCount:N0} positions");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Count positions could not be loaded");
		}
	}

	private async Task LoadLinePageCoreAsync(CancellationToken cancellationToken)
	{
		if (!HasCountLines)
		{
			Lines.Clear();
			LineTotalCount = 0;
			SelectedLine = null;
			return;
		}

		var page = await _counts.SearchLinesAsync(
			Editor.Id,
			LineSearchText,
			UncountedOnly,
			DifferencesOnly,
			LinePageNumber,
			LinePageSize,
			cancellationToken);
		Lines.Clear();
		foreach (var line in page.Items) Lines.Add(new InventoryCountLineRowViewModel(line));
		LineTotalCount = page.TotalCount;
		SelectedLine = Lines.FirstOrDefault();
	}

	private void NewCount()
	{
		_selectedCount = null;
		OnPropertyChanged(nameof(SelectedCount));
		Editor = NewDraft();
		_selectedDraftWarehouse = null;
		OnPropertyChanged(nameof(SelectedDraftWarehouse));
		Lines.Clear();
		LineTotalCount = 0;
		SelectedLine = null;
		CompleteOperation(false, "New inventory count");
	}

	private async Task SaveDraftAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Inventur wird gespeichert");
		try
		{
			var saved = await _counts.SaveDraftAsync(Copy(Editor), cancellationToken);
			await ApplySavedCountAsync(saved, false, cancellationToken);
			CompleteOperation(false, $"{saved.CountNumber} saved");
		}
		catch (ConcurrencyConflictException)
		{
			FailConcurrency("Inventory count could not be saved");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory count could not be saved");
		}
	}

	private async Task StartAsync(CancellationToken cancellationToken)
	{
		if (!_dialogs.Confirm(new ConfirmationDialogRequest(
			"Start Inventory Count",
			$"Start {Editor.CountNumber}? The current warehouse quantities will be frozen as the expected snapshot.",
			true))) return;
		BeginOperation("Inventur wird gestartet — Bestände werden ermittelt");
		try
		{
			var started = await _counts.StartAsync(Editor.Id, Editor.Version, cancellationToken);
			await ApplySavedCountAsync(started, true, cancellationToken);
			CompleteOperation(false, $"{started.CountNumber} started");
		}
		catch (ConcurrencyConflictException)
		{
			FailConcurrency("Inventory count could not be started");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory count could not be started");
		}
	}

	private async Task SaveQuantityAsync(CancellationToken cancellationToken)
	{
		if (SelectedLine is null) return;
		var selectedId = SelectedLine.Id;
		var selectedIndex = Lines.IndexOf(SelectedLine);
		BeginOperation("Zählmenge wird gespeichert");
		try
		{
			await _counts.RecordCountAsync(
				Editor.Id,
				SelectedLine.Id,
				SelectedLine.Version,
				SelectedLine.CountedQuantity,
				cancellationToken);
			var details = await _counts.GetLineDetailsByIdAsync(selectedId, cancellationToken)
				?? throw new InvalidOperationException("The saved count position was not found.");
			if (MatchesLineFilter(details))
			{
				var row = new InventoryCountLineRowViewModel(details);
				Lines[selectedIndex] = row;
				SelectedLine = Lines.Skip(selectedIndex + 1).FirstOrDefault() ?? Lines.FirstOrDefault(line => !line.IsCounted) ?? row;
			}
			else
			{
				Lines.RemoveAt(selectedIndex);
				LineTotalCount--;
				SelectedLine = Lines.Count == 0
					? null
					: Lines[Math.Min(selectedIndex, Lines.Count - 1)];
			}
			await RefreshOverviewAsync(cancellationToken);
			CompleteOperation(false, "Quantity saved");
		}
		catch (ConcurrencyConflictException)
		{
			FailConcurrency("Quantity could not be saved");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Quantity could not be saved");
		}
	}

	private async Task MoveToReviewAsync(CancellationToken cancellationToken) =>
		await ChangeStatusAsync(
			"Inventur wird zur Prüfung übergeben",
			"Move to Review",
			$"Move {Editor.CountNumber} to Review? Quantities become read-only until the count is returned to Counting.",
			(token => _counts.MoveToReviewAsync(Editor.Id, Editor.Version, token)),
			cancellationToken);

	private async Task ReturnToCountingAsync(CancellationToken cancellationToken) =>
		await ChangeStatusAsync(
			"Inventur wird zur Zählung zurückgegeben",
			"Return to Counting",
			$"Return {Editor.CountNumber} to Counting so quantities can be corrected?",
			(token => _counts.ReturnToCountingAsync(Editor.Id, Editor.Version, token)),
			cancellationToken);

	private async Task PostAsync(CancellationToken cancellationToken) =>
		await ChangeStatusAsync(
			"Inventur wird gebucht",
			"Post Inventory Count",
			$"Post {Editor.CountNumber}? Corrections will be calculated against the current stock and cannot be undone.",
			(token => _counts.PostInventoryCountAsync(Editor.Id, Editor.Version, token)),
			cancellationToken);

	private async Task ReverseAsync(CancellationToken cancellationToken)
	{
		if (SelectedReversalReasonCode is null) return;
		if (!_dialogs.Confirm(new ConfirmationDialogRequest("Reverse Inventory Count", $"Create counter-movements for {Editor.CountNumber}?", true))) return;
		BeginOperation("Inventurbuchung wird storniert");
		try
		{
			var reversed = await _counts.ReverseAsync(Editor.Id, Editor.Version, SelectedReversalReasonCode.Id, ReversalReason, cancellationToken);
			await ApplySavedCountAsync(reversed, false, cancellationToken);
			CompleteOperation(false, $"{reversed.CountNumber} reversed");
		}
		catch (ConcurrencyConflictException)
		{
			FailConcurrency("Inventory count could not be reversed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Inventory count could not be reversed");
		}
	}

	private async Task CancelAsync(CancellationToken cancellationToken) =>
		await ChangeStatusAsync(
			"Inventur wird storniert",
			"Cancel Inventory Count",
			$"Cancel {Editor.CountNumber}? Existing snapshot and count data remain available for audit.",
			(token => _counts.CancelAsync(Editor.Id, Editor.Version, token)),
			cancellationToken);

	private async Task ChangeStatusAsync(
		string busyText,
		string title,
		string message,
		Func<CancellationToken, Task<InventoryCount>> operation,
		CancellationToken cancellationToken)
	{
		if (!_dialogs.Confirm(new ConfirmationDialogRequest(title, message, true))) return;
		BeginOperation(busyText);
		try
		{
			var changed = await operation(cancellationToken);
			await ApplySavedCountAsync(changed, false, cancellationToken);
			CompleteOperation(false, $"{changed.CountNumber}: {changed.Status}");
		}
		catch (ConcurrencyConflictException)
		{
			FailConcurrency("Inventory-count status could not be changed");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(false);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Inventory-count status could not be changed");
		}
	}

	private async Task ApplySavedCountAsync(
		InventoryCount saved,
		bool reloadLines,
		CancellationToken cancellationToken)
	{
		Editor = Copy(saved);
		_selectedDraftWarehouse = Warehouses.FirstOrDefault(warehouse => warehouse.Id == saved.WarehouseId);
		OnPropertyChanged(nameof(SelectedDraftWarehouse));
		await RefreshOverviewAsync(cancellationToken);
		if (reloadLines)
		{
			LinePageNumber = 1;
			await LoadLinePageCoreAsync(cancellationToken);
		}
		else if (!HasCountLines)
		{
			Lines.Clear();
			LineTotalCount = 0;
		}
		RaiseEditorProperties();
	}

	private async Task RefreshOverviewAsync(CancellationToken cancellationToken)
	{
		var overview = await _counts.GetOverviewByIdAsync(Editor.Id, cancellationToken)
			?? throw new InvalidOperationException("The inventory count could not be reloaded.");
		var existing = InventoryCounts.FirstOrDefault(count => count.Id == overview.Id);
		if (MatchesCountFilter(overview))
		{
			if (existing is null)
			{
				InventoryCounts.Insert(0, overview);
				TotalCount++;
			}
			else
			{
				InventoryCounts[InventoryCounts.IndexOf(existing)] = overview;
			}
		}
		else if (existing is not null)
		{
			InventoryCounts.Remove(existing);
			TotalCount--;
		}
		_selectedCount = overview;
		OnPropertyChanged(nameof(SelectedCount));
		OnPropertyChanged(nameof(UncountedLineCount));
		OnPropertyChanged(nameof(CountProgressDisplay));
		OnPropertyChanged(nameof(DifferenceSummary));
		RaiseCommands();
	}

	private bool MatchesCountFilter(InventoryCountOverviewItem count)
	{
		if (SelectedStatusFilter.Status is not null && count.Status != SelectedStatusFilter.Status) return false;
		if (SelectedWarehouseFilter.WarehouseId is not null && count.WarehouseId != SelectedWarehouseFilter.WarehouseId) return false;
		if (string.IsNullOrWhiteSpace(SearchText)) return true;
		var search = SearchText.Trim();
		return count.CountNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			count.WarehouseName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			(count.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	private bool MatchesLineFilter(InventoryCountLineDetails line)
	{
		if (UncountedOnly && line.CountedQuantity is not null) return false;
		if (DifferencesOnly && line.Difference is null or 0) return false;
		if (string.IsNullOrWhiteSpace(LineSearchText)) return true;
		var search = LineSearchText.Trim();
		return line.PartNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			line.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			line.StorageLocationName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			line.PurposeName.Contains(search, StringComparison.OrdinalIgnoreCase);
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadCountPageAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadCountPageAsync(cancellationToken);
	}

	private async Task PreviousLinePageAsync(CancellationToken cancellationToken)
	{
		if (LinePageNumber <= 1) return;
		LinePageNumber--;
		await LoadLinePageAsync(cancellationToken);
	}

	private async Task NextLinePageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextLinePage) return;
		LinePageNumber++;
		await LoadLinePageAsync(cancellationToken);
	}

	private void ApplyCountPage(PageResult<InventoryCountOverviewItem> page)
	{
		var selectedId = SelectedCount?.Id;
		CollectionSynchronizer.Replace(InventoryCounts, page.Items);
		TotalCount = page.TotalCount;
		if (selectedId is not null)
		{
			_selectedCount = InventoryCounts.FirstOrDefault(count => count.Id == selectedId);
			OnPropertyChanged(nameof(SelectedCount));
		}
	}

	private void RaiseEditorProperties()
	{
		OnPropertyChanged(nameof(IsDraft));
		OnPropertyChanged(nameof(IsCounting));
		OnPropertyChanged(nameof(IsReview));
		OnPropertyChanged(nameof(HasSelectedCount));
		OnPropertyChanged(nameof(HasCountLines));
		OnPropertyChanged(nameof(CanCancel));
		OnPropertyChanged(nameof(CanReverse));
		OnPropertyChanged(nameof(IsDraftReadOnly));
		OnPropertyChanged(nameof(EditorTitle));
		RaiseCommands();
	}

	private void RaiseCommands()
	{
		SaveDraftCommand.RaiseCanExecuteChanged();
		StartCommand.RaiseCanExecuteChanged();
		CancelCommand.RaiseCanExecuteChanged();
		MoveToReviewCommand.RaiseCanExecuteChanged();
		ReturnToCountingCommand.RaiseCanExecuteChanged();
		PostCommand.RaiseCanExecuteChanged();
		ReverseCommand.RaiseCanExecuteChanged();
		SaveQuantityCommand.RaiseCanExecuteChanged();
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
		PreviousLinePageCommand.RaiseCanExecuteChanged();
		NextLinePageCommand.RaiseCanExecuteChanged();
	}

	private void FailConcurrency(string statusText) =>
		FailOperation(
			new InvalidOperationException("The inventory count was changed by another user. Reload it before continuing."),
			statusText);

	private static InventoryCount NewDraft() => new() { CreatedAtUtc = DateTime.UtcNow };

	private static InventoryCount Copy(InventoryCount source) => new()
	{
		Id = source.Id,
		CountNumber = source.CountNumber,
		WarehouseId = source.WarehouseId,
		Status = source.Status,
		CreatedAtUtc = source.CreatedAtUtc,
		StartedAtUtc = source.StartedAtUtc,
		CompletedAtUtc = source.CompletedAtUtc,
		CreatedByUserId = source.CreatedByUserId,
		PostedByUserId = source.PostedByUserId,
		Notes = source.Notes,
		ReversedAtUtc = source.ReversedAtUtc,
		ReversedByUserId = source.ReversedByUserId,
		ReversalReason = source.ReversalReason,
		Version = source.Version,
		Lines = []
	};

	public void Dispose()
	{
		_countSearch.Dispose();
		_lineSearch.Dispose();
		_selectionCancellation?.Cancel();
		_selectionCancellation?.Dispose();
		SaveDraftCommand.Dispose();
		StartCommand.Dispose();
		CancelCommand.Dispose();
		MoveToReviewCommand.Dispose();
		ReturnToCountingCommand.Dispose();
		PostCommand.Dispose();
		ReverseCommand.Dispose();
		SaveQuantityCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
		PreviousLinePageCommand.Dispose();
		NextLinePageCommand.Dispose();
	}
}

public sealed record InventoryCountStatusFilter(string Name, InventoryCountStatus? Status);
public sealed record InventoryCountWarehouseFilter(string Name, long? WarehouseId);

public sealed class InventoryCountLineRowViewModel : BaseViewModel
{
	private long? _countedQuantity;

	public InventoryCountLineRowViewModel(InventoryCountLineDetails line)
	{
		Id = line.Id;
		InventoryCountId = line.InventoryCountId;
		InventoryId = line.InventoryId;
		PartNumber = line.PartNumber;
		Description = line.Description;
		StorageLocationName = line.StorageLocationName;
		PurposeName = line.PurposeName;
		ExpectedQuantity = line.ExpectedQuantity;
		_countedQuantity = line.CountedQuantity;
		CountedByUserName = line.CountedByUserName;
		CountedAtUtc = line.CountedAtUtc;
		Version = line.Version;
	}

	public long Id { get; }
	public long InventoryCountId { get; }
	public long InventoryId { get; }
	public string PartNumber { get; }
	public string Description { get; }
	public string StorageLocationName { get; }
	public string PurposeName { get; }
	public long ExpectedQuantity { get; }
	public string? CountedByUserName { get; }
	public DateTime? CountedAtUtc { get; }
	public long Version { get; }

	public long? CountedQuantity
	{
		get => _countedQuantity;
		set
		{
			if (_countedQuantity == value) return;
			_countedQuantity = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsCounted));
			OnPropertyChanged(nameof(Difference));
		}
	}

	public bool IsCounted => CountedQuantity is not null;
	public long? Difference => CountedQuantity - ExpectedQuantity;
}
