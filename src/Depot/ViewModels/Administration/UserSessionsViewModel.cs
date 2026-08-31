// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class UserSessionsViewModel : BaseViewModel, IDisposable
{
	private readonly UserSessionAdministrationService _service;
	private readonly UserSessionPresenceOptions _options;
	private readonly SemaphoreSlim _refreshGate = new(1, 1);
	private IReadOnlyList<ActiveUserSession> _allSessions = [];
	private CancellationTokenSource? _pollingCancellation;
	private Task? _pollingTask;
	private string _searchText = string.Empty;
	private long _onlineUsers;
	private long _activeSessions;
	private DateTime _asOfUtc;
	private bool _disposed;

	public UserSessionsViewModel(UserSessionAdministrationService service, UserSessionPresenceOptions? options = null)
	{
		_service = service;
		_options = options ?? UserSessionPresenceOptions.Default;
	}

	public ObservableCollection<UserSessionRowViewModel> Sessions { get; } = [];
	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			ApplyFilter();
		}
	}
	public long OnlineUsers { get => _onlineUsers; private set { _onlineUsers = value; OnPropertyChanged(); } }
	public long ActiveSessions { get => _activeSessions; private set { _activeSessions = value; OnPropertyChanged(); } }
	public bool HasSessions => Sessions.Count > 0;
	public bool HasNoSessions => !HasSessions;
	internal bool IsPolling => _pollingCancellation is not null;

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!await _refreshGate.WaitAsync(0, cancellationToken)) return;
		BeginOperation("Refreshing user sessions");
		try
		{
			var snapshot = await _service.GetSnapshotAsync(cancellationToken);
			if (_disposed || cancellationToken.IsCancellationRequested) return;
			_allSessions = snapshot.Sessions;
			_asOfUtc = snapshot.AsOfUtc;
			OnlineUsers = snapshot.Metrics.OnlineUsers;
			ActiveSessions = snapshot.Metrics.ActiveSessions;
			ApplyFilter();
			CompleteOperation(Sessions.Count == 0, "User sessions refreshed");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			if (!_disposed) CompleteOperation(Sessions.Count == 0);
		}
		catch (Exception exception)
		{
			if (!_disposed) FailOperation(exception, "User sessions could not be loaded");
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	public void StartPolling()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		StopPolling();
		_pollingCancellation = new CancellationTokenSource();
		_pollingTask = RunPollingAsync(_pollingCancellation.Token);
	}

	public void StopPolling()
	{
		_pollingCancellation?.Cancel();
		_pollingCancellation?.Dispose();
		_pollingCancellation = null;
		_pollingTask = null;
	}

	private async Task RunPollingAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_options.AdministrationRefreshInterval);
		try
		{
			await LoadAsync(cancellationToken);
			while (await timer.WaitForNextTickAsync(cancellationToken)) await LoadAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private void ApplyFilter()
	{
		var search = SearchText.Trim();
		var rows = _allSessions
			.Where(session => string.IsNullOrEmpty(search)
				|| session.UserDisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| session.UserEmail.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| (session.MachineName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
			.Select(session => new UserSessionRowViewModel(session, _asOfUtc))
			.ToArray();
		CollectionSynchronizer.Replace(Sessions, rows);
		OnPropertyChanged(nameof(HasSessions));
		OnPropertyChanged(nameof(HasNoSessions));
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		StopPolling();
	}
}

public sealed class UserSessionRowViewModel
{
	public UserSessionRowViewModel(ActiveUserSession session, DateTime asOfUtc)
	{
		UserDisplayName = session.UserDisplayName;
		UserEmail = session.UserEmail;
		MachineName = string.IsNullOrWhiteSpace(session.MachineName) ? "Unknown client" : session.MachineName;
		AppVersion = string.IsNullOrWhiteSpace(session.AppVersion) ? "Unknown" : session.AppVersion;
		StartedLocal = session.StartedUtc.ToLocalTime();
		LastSeenLocal = session.LastSeenUtc.ToLocalTime();
		LastSeen = RelativeTime(session.LastSeenUtc, asOfUtc);
		SessionId = session.SessionId;
		ClientInstanceId = session.ClientInstanceId;
	}

	public string UserDisplayName { get; }
	public string UserEmail { get; }
	public string MachineName { get; }
	public string AppVersion { get; }
	public DateTime StartedLocal { get; }
	public DateTime LastSeenLocal { get; }
	public string LastSeen { get; }
	public Guid SessionId { get; }
	public Guid ClientInstanceId { get; }
	public string LastSeenLocalText => LastSeenLocal.ToString("F");

	private static string RelativeTime(DateTime lastSeenUtc, DateTime asOfUtc)
	{
		var elapsed = asOfUtc - lastSeenUtc;
		if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
		if (elapsed.TotalSeconds < 60) return $"{Math.Max(0, (int)elapsed.TotalSeconds)} sec ago";
		if (elapsed.TotalMinutes < 60) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
		return $"{Math.Max(1, (int)elapsed.TotalHours)} hr ago";
	}
}
