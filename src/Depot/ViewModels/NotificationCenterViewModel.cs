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
	private readonly CancellationTokenSource _lifetime = new();
	private readonly PeriodicTimer _pollingTimer = new(TimeSpan.FromSeconds(60));
	private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
	private CancellationTokenSource? _detailsCancellation;
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
	private long _unreadCount;
	private bool _isApplicationActive = true;

	public NotificationCenterViewModel(INotificationService notifications, INotificationNavigationService navigation)
	{
		_notifications = notifications;
		_navigation = navigation;
		_notifications.NotificationsChanged += OnNotificationsChanged;
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
		_ = PollAsync(_lifetime.Token);
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
	public int PageNumber { get => _pageNumber; private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); RaisePagingCommands(); } }
	public long TotalCount { get => _totalCount; private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaisePagingCommands(); } }
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} notifications";
	public long UnreadCount { get => _unreadCount; private set { if (_unreadCount == value) return; _unreadCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnreadNotifications)); OnPropertyChanged(nameof(UnreadBadgeText)); } }
	public bool HasUnreadNotifications => UnreadCount > 0;
	public string UnreadBadgeText => UnreadCount > 99 ? "99+" : UnreadCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading notifications");
		try
		{
			var page = await _notifications.GetPageAsync(CreateFilter(), PageNumber, PageSize, cancellationToken);
			var selectedId = SelectedItem?.RecipientId;
			CollectionSynchronizer.Replace(Items, page.Items);
			TotalCount = page.TotalCount;
			SelectedItem = selectedId is null ? null : Items.FirstOrDefault(item => item.RecipientId == selectedId);
			await RefreshUnreadCountAsync(cancellationToken);
			MarkPageReadCommand.RaiseCanExecuteChanged();
			CompleteOperation(Items.Count == 0, Items.Count == 0 ? "No notifications found" : $"{TotalCount:N0} notifications");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Notifications could not be loaded"); }
	}

	public async Task RefreshUnreadCountAsync(CancellationToken cancellationToken = default)
	{
		try { UnreadCount = await _notifications.GetUnreadCountAsync(cancellationToken); }
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception) { }
	}

	public void SetApplicationActive(bool isActive)
	{
		_isApplicationActive = isActive;
		if (isActive) _ = RefreshUnreadCountAsync(_lifetime.Token);
	}

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
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		_detailsCancellation = null;
		Details = null;
		if (item is null) return;
		_detailsCancellation = new CancellationTokenSource();
		try { Details = await _notifications.GetDetailsAsync(item.RecipientId, _detailsCancellation.Token); }
		catch (OperationCanceledException) when (_detailsCancellation.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Notification details could not be loaded"); }
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
			await LoadDetailsAsync(item);
			await RefreshUnreadCountAsync(cancellationToken);
		}
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
				Items.Remove(item); TotalCount--; SelectedItem = null;
			}
			else
			{
				item.ArchivedAtUtc = archive ? DateTime.UtcNow : null; item.RecipientVersion++; OnPropertyChanged(nameof(Items)); await LoadDetailsAsync(item);
			}
			await RefreshUnreadCountAsync(cancellationToken);
		}
		catch (Exception exception) { FailOperation(exception, "Notification state could not be changed"); }
	}

	private Task ArchiveAsync(CancellationToken token) => ChangeArchiveStateAsync(true, token);
	private Task RestoreAsync(CancellationToken token) => ChangeArchiveStateAsync(false, token);

	private async Task MarkPageReadAsync(CancellationToken cancellationToken)
	{
		await _notifications.MarkVisiblePageReadAsync(Items.Where(item => item.IsUnread).Select(item => item.RecipientId), cancellationToken);
		foreach (var item in Items.Where(item => item.IsUnread)) { item.ReadAtUtc = DateTime.UtcNow; item.RecipientVersion++; }
		OnPropertyChanged(nameof(Items));
		if (SelectedItem is not null) await LoadDetailsAsync(SelectedItem);
		await RefreshUnreadCountAsync(cancellationToken);
		MarkPageReadCommand.RaiseCanExecuteChanged();
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

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		await RefreshUnreadCountAsync(cancellationToken);
		try
		{
			while (await _pollingTimer.WaitForNextTickAsync(cancellationToken))
				if (_isApplicationActive) await RefreshUnreadCountAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
	}

	private void OnNotificationsChanged(object? sender, EventArgs e)
	{
		if (_synchronizationContext is null)
		{
			_ = RefreshUnreadCountAsync(_lifetime.Token);
			return;
		}
		_synchronizationContext.Post(_ => _ = RefreshUnreadCountAsync(_lifetime.Token), null);
	}

	public void Dispose()
	{
		_notifications.NotificationsChanged -= OnNotificationsChanged;
		_lifetime.Cancel();
		_pollingTimer.Dispose();
		_lifetime.Dispose();
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		_searchDebouncer.Dispose();
		RefreshCommand.Dispose(); PreviousPageCommand.Dispose(); NextPageCommand.Dispose();
		MarkReadCommand.Dispose(); MarkUnreadCommand.Dispose(); ArchiveCommand.Dispose(); RestoreCommand.Dispose(); MarkPageReadCommand.Dispose();
		OpenRelatedCommand.Dispose();
	}
}
