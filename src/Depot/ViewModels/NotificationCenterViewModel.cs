// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed record NotificationTypeFilterOption(string Name, NotificationType? Value);
public sealed record NotificationSeverityFilterOption(string Name, NotificationSeverity? Value);

public sealed class NotificationCenterViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 50;
	private readonly INotificationService _notifications;
	private readonly INotificationNavigationService _navigation;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _listRequest = new();
	private readonly LatestRequest _detailsRequest = new();
	private NotificationListItem? _selectedItem;
	private NotificationDetails? _details;
	private string _searchText = string.Empty;
	private NotificationInboxFilter _inboxFilter;
	private NotificationTypeFilterOption _selectedType;
	private NotificationSeverityFilterOption _selectedSeverity;
	private DateTime? _fromDate;
	private DateTime? _toDate;
	private int _pageNumber = 1;
	private long _totalCount;
	private bool _isLoadingDetails;

	public NotificationCenterViewModel(INotificationService notifications, INotificationNavigationService navigation)
	{
		_notifications = notifications;
		_navigation = navigation;
		TypeFilters = [new("All types", null), .. Enum.GetValues<NotificationType>().Select(value => new NotificationTypeFilterOption(value.ToString(), value))];
		SeverityFilters = [new("All severities", null), .. Enum.GetValues<NotificationSeverity>().Select(value => new NotificationSeverityFilterOption(value.ToString(), value))];
		_selectedType = TypeFilters[0];
		_selectedSeverity = SeverityFilters[0];
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		MarkReadCommand = new AsyncRelayCommand(MarkReadAsync, () => Details?.IsUnread == true);
		MarkUnreadCommand = new AsyncRelayCommand(MarkUnreadAsync, () => Details is { IsUnread: false });
		ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => Details?.IsArchived == false);
		RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => Details?.IsArchived == true);
		MarkPageReadCommand = new AsyncRelayCommand(MarkPageReadAsync, () => Items.Any(item => item.IsUnread));
		OpenRelatedCommand = new AsyncRelayCommand(OpenRelatedAsync, () => Details?.SourceType is not null);
		CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
	}

	public ObservableCollection<NotificationListItem> Items { get; } = new();
	public IReadOnlyList<NotificationInboxFilter> InboxFilters { get; } = Enum.GetValues<NotificationInboxFilter>();
	public IReadOnlyList<NotificationTypeFilterOption> TypeFilters { get; }
	public IReadOnlyList<NotificationSeverityFilterOption> SeverityFilters { get; }
	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public AsyncRelayCommand MarkReadCommand { get; }
	public AsyncRelayCommand MarkUnreadCommand { get; }
	public AsyncRelayCommand ArchiveCommand { get; }
	public AsyncRelayCommand RestoreCommand { get; }
	public AsyncRelayCommand MarkPageReadCommand { get; }
	public AsyncRelayCommand OpenRelatedCommand { get; }
	public RelayCommand CloseCommand { get; }
	public event EventHandler? CloseRequested;

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); QueueFilterReload(); } }
	public NotificationInboxFilter InboxFilter { get => _inboxFilter; set { if (_inboxFilter == value) return; _inboxFilter = value; OnPropertyChanged(); QueueFilterReload(); } }
	public NotificationTypeFilterOption SelectedType { get => _selectedType; set { if (_selectedType == value) return; _selectedType = value; OnPropertyChanged(); QueueFilterReload(); } }
	public NotificationSeverityFilterOption SelectedSeverity { get => _selectedSeverity; set { if (_selectedSeverity == value) return; _selectedSeverity = value; OnPropertyChanged(); QueueFilterReload(); } }
	public DateTime? FromDate { get => _fromDate; set { if (_fromDate == value) return; _fromDate = value; OnPropertyChanged(); QueueFilterReload(); } }
	public DateTime? ToDate { get => _toDate; set { if (_toDate == value) return; _toDate = value; OnPropertyChanged(); QueueFilterReload(); } }

	public NotificationListItem? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (_selectedItem == value) return;
			_selectedItem = value;
			OnPropertyChanged();
			_ = LoadDetailsAsync(value);
		}
	}

	public NotificationDetails? Details
	{
		get => _details;
		private set
		{
			_details = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasDetails));
			OnPropertyChanged(nameof(HasNoDetails));
			RaiseDetailCommands();
		}
	}

	public bool HasDetails => Details is not null;
	public bool HasNoDetails => Details is null;
	public bool IsLoadingDetails { get => _isLoadingDetails; private set { if (_isLoadingDetails == value) return; _isLoadingDetails = value; OnPropertyChanged(); } }
	public int PageNumber { get => _pageNumber; private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); RaisePagingCommands(); } }
	public long TotalCount { get => _totalCount; private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaisePagingCommands(); } }
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} notifications";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _listRequest.Begin(cancellationToken);
		BeginOperation("Loading notifications");
		try
		{
			var page = await _notifications.GetPageAsync(CreateFilter(), PageNumber, PageSize, request.Token);
			if (!request.IsCurrent) return;
			var selectedId = SelectedItem?.RecipientId;
			CollectionSynchronizer.Replace(Items, page.Items);
			TotalCount = page.TotalCount;
			SelectedItem = selectedId is null ? null : Items.FirstOrDefault(item => item.RecipientId == selectedId);
			MarkPageReadCommand.RaiseCanExecuteChanged();
			CompleteOperation(Items.Count == 0, Items.Count == 0 ? "No notifications found" : $"{TotalCount:N0} notifications");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Notifications could not be loaded"); }
	}

	public void SetApplicationActive(bool isActive) { }

	private NotificationFilter CreateFilter() => new(
		SearchText, InboxFilter, SelectedType.Value, SelectedSeverity.Value,
		FromDate?.Date.ToUniversalTime(), ToDate?.Date.AddDays(1).ToUniversalTime());

	private void QueueFilterReload()
	{
		PageNumber = 1;
		_ = _searchDebouncer.DebounceAsync(LoadAsync);
	}

	private async Task LoadDetailsAsync(NotificationListItem? item)
	{
		var request = _detailsRequest.Begin();
		Details = null;
		IsLoadingDetails = false;
		if (item is null) return;
		IsLoadingDetails = true;
		try
		{
			var details = await _notifications.GetDetailsAsync(item.RecipientId, request.Token);
			if (!request.IsCurrent || SelectedItem?.RecipientId != item.RecipientId) return;
			Details = details;
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Notification details could not be loaded"); }
		finally { if (request.IsCurrent) IsLoadingDetails = false; }
	}

	private async Task ChangeReadStateAsync(bool read, CancellationToken cancellationToken)
	{
		var details = Details;
		var item = SelectedItem;
		if (details is null || item is null) return;
		try
		{
			if (read) await _notifications.MarkReadAsync(details.RecipientId, details.RecipientVersion, cancellationToken);
			else await _notifications.MarkUnreadAsync(details.RecipientId, details.RecipientVersion, cancellationToken);
			item.ReadAtUtc = read ? DateTime.UtcNow : null;
			item.RecipientVersion++;
			OnPropertyChanged(nameof(Items));
			Details = details with { ReadAtUtc = item.ReadAtUtc, RecipientVersion = item.RecipientVersion };
			if (read && InboxFilter == NotificationInboxFilter.Unread)
				RemoveCurrentItem(item);
			MarkPageReadCommand.RaiseCanExecuteChanged();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Notification state could not be changed"); }
	}

	private Task MarkReadAsync(CancellationToken token) => ChangeReadStateAsync(true, token);
	private Task MarkUnreadAsync(CancellationToken token) => ChangeReadStateAsync(false, token);

	private async Task ChangeArchiveStateAsync(bool archive, CancellationToken cancellationToken)
	{
		var details = Details;
		var item = SelectedItem;
		if (details is null || item is null) return;
		try
		{
			if (archive) await _notifications.ArchiveAsync(details.RecipientId, details.RecipientVersion, cancellationToken);
			else await _notifications.RestoreAsync(details.RecipientId, details.RecipientVersion, cancellationToken);
			if ((archive && InboxFilter != NotificationInboxFilter.Archived) || (!archive && InboxFilter == NotificationInboxFilter.Archived))
			{
				RemoveCurrentItem(item);
			}
			else
			{
				item.ArchivedAtUtc = archive ? DateTime.UtcNow : null; item.RecipientVersion++; OnPropertyChanged(nameof(Items)); Details = details with { ArchivedAtUtc = item.ArchivedAtUtc, RecipientVersion = item.RecipientVersion };
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Notification state could not be changed"); }
	}

	private Task ArchiveAsync(CancellationToken token) => ChangeArchiveStateAsync(true, token);
	private Task RestoreAsync(CancellationToken token) => ChangeArchiveStateAsync(false, token);

	private async Task MarkPageReadAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _notifications.MarkVisiblePageReadAsync(Items.Where(item => item.IsUnread).Select(item => item.RecipientId), cancellationToken);
			foreach (var item in Items.Where(item => item.IsUnread)) { item.ReadAtUtc = DateTime.UtcNow; item.RecipientVersion++; }
			OnPropertyChanged(nameof(Items));
			if (Details is { } details && SelectedItem is { } selected)
				Details = details with { ReadAtUtc = selected.ReadAtUtc, RecipientVersion = selected.RecipientVersion };
			if (InboxFilter == NotificationInboxFilter.Unread)
			{
				var removedCount = Items.Count;
				Items.Clear();
				SelectedItem = null;
				TotalCount = Math.Max(0, TotalCount - removedCount);
			}
			MarkPageReadCommand.RaiseCanExecuteChanged();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Notifications could not be marked as read"); }
	}

	private async Task OpenRelatedAsync(CancellationToken cancellationToken)
	{
		if (Details is null) return;
		try { await _navigation.NavigateAsync(Details, cancellationToken); }
		catch (Exception exception) { FailOperation(exception, "Related record could not be opened"); }
	}

	private async Task PreviousPageAsync(CancellationToken token) { if (PageNumber <= 1) return; PageNumber--; await LoadAsync(token); }
	private async Task NextPageAsync(CancellationToken token) { if (!HasNextPage) return; PageNumber++; await LoadAsync(token); }
	private void RaisePagingCommands() { PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
	private void RaiseDetailCommands() { MarkReadCommand.RaiseCanExecuteChanged(); MarkUnreadCommand.RaiseCanExecuteChanged(); ArchiveCommand.RaiseCanExecuteChanged(); RestoreCommand.RaiseCanExecuteChanged(); OpenRelatedCommand.RaiseCanExecuteChanged(); }

	private void RemoveCurrentItem(NotificationListItem item)
	{
		var index = Items.IndexOf(item);
		Items.Remove(item);
		TotalCount = Math.Max(0, TotalCount - 1);
		SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
	}

	public void Dispose()
	{
		_listRequest.Dispose();
		_detailsRequest.Dispose();
		_searchDebouncer.Dispose();
		RefreshCommand.Dispose(); PreviousPageCommand.Dispose(); NextPageCommand.Dispose();
		MarkReadCommand.Dispose(); MarkUnreadCommand.Dispose(); ArchiveCommand.Dispose(); RestoreCommand.Dispose(); MarkPageReadCommand.Dispose();
		OpenRelatedCommand.Dispose();
	}
}
