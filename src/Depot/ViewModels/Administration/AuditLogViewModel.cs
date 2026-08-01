// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.ViewModels.Shared;

namespace Depot.ViewModels.Administration;

public sealed class AuditLogViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 50;
	private readonly AuditLogService _service;
	private readonly IFileDialogService _fileDialogs;
	private readonly AsyncDebouncer _filterDebouncer = new(TimeSpan.FromMilliseconds(300));
	private CancellationTokenSource? _detailsCancellation;
	private string _searchText = string.Empty;
	private string _userFilter = string.Empty;
	private string _entityIdFilter = string.Empty;
	private string _selectedEntityType = string.Empty;
	private string _selectedAction = string.Empty;
	private DateTime? _fromDate;
	private DateTime? _toDate;
	private AuditLogListItem? _selectedEntry;
	private SanitizedAuditDetails? _details;
	private int _pageNumber = 1;
	private long _totalCount;
	private bool _isLoadingDetails;

	public AuditLogViewModel(AuditLogService service, IFileDialogService fileDialogs)
	{
		_service = service;
		_fileDialogs = fileDialogs;
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		ClearFiltersCommand = new RelayCommand(ClearFilters);
		ExportCommand = new AsyncRelayCommand(ExportAsync);
		EntityTypes.Add(string.Empty);
		Actions.Add(string.Empty);
	}

	public ObservableCollection<AuditLogListItem> Entries { get; } = new();
	public ObservableCollection<string> EntityTypes { get; } = new();
	public ObservableCollection<string> Actions { get; } = new();
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public RelayCommand ClearFiltersCommand { get; }
	public AsyncRelayCommand ExportCommand { get; }
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;

	public string SearchText { get => _searchText; set => SetFilter(ref _searchText, value); }
	public string UserFilter { get => _userFilter; set => SetFilter(ref _userFilter, value); }
	public string EntityIdFilter { get => _entityIdFilter; set => SetFilter(ref _entityIdFilter, value); }
	public string SelectedEntityType { get => _selectedEntityType; set => SetFilter(ref _selectedEntityType, value); }
	public string SelectedAction { get => _selectedAction; set => SetFilter(ref _selectedAction, value); }
	public DateTime? FromDate { get => _fromDate; set => SetFilter(ref _fromDate, value); }
	public DateTime? ToDate { get => _toDate; set => SetFilter(ref _toDate, value); }

	public int PageNumber
	{
		get => _pageNumber;
		private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); RaisePaging(); }
	}

	public long TotalCount
	{
		get => _totalCount;
		private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNextPage)); RaisePaging(); }
	}

	public AuditLogListItem? SelectedEntry
	{
		get => _selectedEntry;
		set
		{
			if (_selectedEntry == value) return;
			_selectedEntry = value;
			OnPropertyChanged();
			_ = LoadDetailsAsync(value);
		}
	}

	public SanitizedAuditDetails? Details
	{
		get => _details;
		private set { _details = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDetails)); OnPropertyChanged(nameof(HasNoDetails)); }
	}

	public bool HasDetails => Details is not null;
	public bool HasNoDetails => Details is null;
	public bool IsLoadingDetails
	{
		get => _isLoadingDetails;
		private set { _isLoadingDetails = value; OnPropertyChanged(); }
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var options = await _service.GetFilterOptionsAsync(cancellationToken);
			CollectionSynchronizer.Replace(EntityTypes, new[] { string.Empty }.Concat(options.EntityTypes).ToArray());
			CollectionSynchronizer.Replace(Actions, new[] { string.Empty }.Concat(options.Actions).ToArray());
			await LoadPageAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Audit log could not be loaded"); }
	}

	private async Task LoadPageAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Loading audit log");
		try
		{
			var page = await _service.SearchAsync(CreateFilter(), PageNumber, PageSize, cancellationToken);
			CollectionSynchronizer.Replace(Entries, page.Items);
			TotalCount = page.TotalCount;
			if (SelectedEntry is not null && Entries.All(entry => entry.Id != SelectedEntry.Id)) SelectedEntry = null;
			CompleteOperation(Entries.Count == 0, $"{page.TotalCount:N0} audit entries");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Audit log could not be loaded"); }
	}

	private async Task LoadDetailsAsync(AuditLogListItem? entry)
	{
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		Details = null;
		IsLoadingDetails = false;
		if (entry is null) return;
		_detailsCancellation = new CancellationTokenSource();
		var token = _detailsCancellation.Token;
		IsLoadingDetails = true;
		try { Details = await _service.GetDetailsAsync(entry.Id, token); }
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Audit details could not be loaded"); }
		finally { if (!token.IsCancellationRequested) IsLoadingDetails = false; }
	}

	private AuditLogFilter CreateFilter()
	{
		DateTime? fromUtc = FromDate is null ? null : DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Local).ToUniversalTime();
		DateTime? toUtc = ToDate is null ? null : DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
		return new AuditLogFilter(SearchText, fromUtc, toUtc, UserFilter, SelectedEntityType, SelectedAction,
			long.TryParse(EntityIdFilter, out var entityId) ? entityId : null);
	}

	private async Task PreviousPageAsync(CancellationToken token) { if (PageNumber > 1) { PageNumber--; await LoadPageAsync(token); } }
	private async Task NextPageAsync(CancellationToken token) { if (HasNextPage) { PageNumber++; await LoadPageAsync(token); } }

	private async Task ExportAsync(CancellationToken cancellationToken)
	{
		var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest(
			"Export audit log", "CSV files (*.csv)|*.csv", ".csv", $"depot-audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv"));
		if (string.IsNullOrWhiteSpace(path)) return;
		BeginOperation("Exporting audit log");
		try
		{
			var progress = new Progress<int>(count => UpdateOperationStatus($"Exporting audit log: {count:N0} entries"));
			await _service.ExportCsvAsync(CreateFilter(), path, progress, cancellationToken);
			CompleteOperation(Entries.Count == 0, "Audit log exported");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Audit export failed"); }
	}

	private void ClearFilters()
	{
		_searchText = _userFilter = _entityIdFilter = _selectedEntityType = _selectedAction = string.Empty;
		_fromDate = _toDate = null;
		OnPropertyChanged(string.Empty);
		PageNumber = 1;
		_ = LoadPageAsync(CancellationToken.None);
	}

	private void SetFilter<T>(ref T field, T value)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		OnPropertyChanged();
		PageNumber = 1;
		_ = _filterDebouncer.DebounceAsync(LoadPageAsync);
	}

	private void RaisePaging() { OnPropertyChanged(nameof(HasNextPage)); PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }

	public void Dispose()
	{
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		_filterDebouncer.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
		ExportCommand.Dispose();
	}
}
