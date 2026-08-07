// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

namespace Depot.ViewModels;

public sealed class NotificationSummaryViewModel : BaseViewModel, IDisposable
{
	private readonly INotificationService _notifications;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(60));
	private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
	private readonly SemaphoreSlim _refreshGate = new(1, 1);
	private long _unreadCount;
	private bool _isApplicationActive = true;
	private int _refreshRequested;
	private int _consecutiveFailures;
	private DateTime _nextAttemptUtc;

	public NotificationSummaryViewModel(INotificationService notifications)
	{
		_notifications = notifications;
		_notifications.NotificationsChanged += OnNotificationsChanged;
		_ = PollAsync(_lifetime.Token);
	}

	public long UnreadCount
	{
		get => _unreadCount;
		private set
		{
			if (_unreadCount == value) return;
			_unreadCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasUnreadNotifications));
			OnPropertyChanged(nameof(UnreadBadgeText));
		}
	}

	public bool HasUnreadNotifications => UnreadCount > 0;
	public string UnreadBadgeText => UnreadCount > 99
		? "99+"
		: UnreadCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

	public void SetApplicationActive(bool isActive)
	{
		_isApplicationActive = isActive;
		if (isActive) _ = RefreshAsync(_lifetime.Token);
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		Interlocked.Exchange(ref _refreshRequested, 1);
		if (!await _refreshGate.WaitAsync(0, cancellationToken)) return;
		try
		{
			do
			{
				Interlocked.Exchange(ref _refreshRequested, 0);
				if (!_isApplicationActive || DateTime.UtcNow < _nextAttemptUtc) return;
				try
				{
					UnreadCount = await _notifications.GetUnreadCountAsync(cancellationToken);
					_consecutiveFailures = 0;
					_nextAttemptUtc = DateTime.MinValue;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception)
				{
					_consecutiveFailures++;
					var delayMinutes = Math.Min(15, 1 << Math.Min(_consecutiveFailures - 1, 4));
					_nextAttemptUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
					return;
				}
			}
			while (Interlocked.Exchange(ref _refreshRequested, 0) == 1);
		}
		finally
		{
			_refreshGate.Release();
			if (_isApplicationActive && Volatile.Read(ref _refreshRequested) == 1 && !_lifetime.IsCancellationRequested)
				_ = RefreshAsync(_lifetime.Token);
		}
	}

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		await RefreshAsync(cancellationToken);
		try
		{
			while (await _timer.WaitForNextTickAsync(cancellationToken))
				if (_isApplicationActive) await RefreshAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private void OnNotificationsChanged(object? sender, EventArgs e)
	{
		if (_synchronizationContext is null)
		{
			_ = RefreshAsync(_lifetime.Token);
			return;
		}
		_synchronizationContext.Post(_ => _ = RefreshAsync(_lifetime.Token), null);
	}

	public void Dispose()
	{
		_notifications.NotificationsChanged -= OnNotificationsChanged;
		_lifetime.Cancel();
		_timer.Dispose();
		_lifetime.Dispose();
	}
}
