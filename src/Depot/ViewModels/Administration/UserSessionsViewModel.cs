// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class UserSessionsViewModel : BaseViewModel, IDisposable
{
	private readonly UserSessionAdministrationService _service;
	private readonly IFileDialogService? _dialogs;
	private readonly UserSessionPresenceOptions _options;
	private readonly SemaphoreSlim _refreshGate = new(1, 1);
	private IReadOnlyList<ActiveUserSession> _allSessions = [];
	private CancellationTokenSource? _pollingCancellation;
	private Task? _pollingTask;
	private string _searchText = string.Empty;
	private UserSessionRowViewModel? _selectedSession;
	private long _onlineUsers;
	private long _activeSessions;
	private DateTime _asOfUtc;
	private bool _disposed;

	public UserSessionsViewModel(UserSessionAdministrationService service, IFileDialogService? dialogs = null, UserSessionPresenceOptions? options = null)
	{
		_service = service;
		_dialogs = dialogs;
		_options = options ?? UserSessionPresenceOptions.Default;
		TerminateSessionCommand = new AsyncRelayCommand(TerminateSelectedSessionAsync, CanTerminateSelectedSession);
	}

	public ObservableCollection<UserSessionRowViewModel> Sessions { get; } = [];
	public AsyncRelayCommand TerminateSessionCommand { get; }
	public bool CanTerminateSessions => _service.CanTerminateSessions;
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
	public UserSessionRowViewModel? SelectedSession
	{
		get => _selectedSession;
		set
		{
			if (_selectedSession == value) return;
			_selectedSession = value;
			OnPropertyChanged();
			TerminateSessionCommand.RaiseCanExecuteChanged();
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
			var selectedId = SelectedSession?.SessionId;
			var snapshot = await _service.GetSnapshotAsync(cancellationToken);
			if (_disposed || cancellationToken.IsCancellationRequested) return;
			_allSessions = snapshot.Sessions;
			_asOfUtc = snapshot.AsOfUtc;
			OnlineUsers = snapshot.Metrics.OnlineUsers;
			ActiveSessions = snapshot.Metrics.ActiveSessions;
			ApplyFilter();
			SelectedSession = selectedId is null ? null : Sessions.FirstOrDefault(row => row.SessionId == selectedId);
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

	private async Task TerminateSelectedSessionAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedSession;
		if (selected is null || !CanTerminateSessions) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest(
			"Terminate session?",
			$"End the active session for {selected.UserDisplayName} on {selected.MachineName}? The client will be returned to sign-in on its next heartbeat.",
			true))) return;

		BeginOperation("Terminating user session");
		try
		{
			var terminated = await _service.TerminateSessionAsync(selected.SessionId, cancellationToken);
			SelectedSession = null;
			await LoadAsync(cancellationToken);
			CompleteOperation(Sessions.Count == 0, terminated ? "Session terminated" : "Session was already ended");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Session could not be terminated");
		}
	}

	private bool CanTerminateSelectedSession() => SelectedSession is not null && CanTerminateSessions;

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
		TerminateSessionCommand.Dispose();
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
		OnlineSince = StartedLocal.ToString("g");
		OnlineFor = FormatDuration(session.StartedUtc, asOfUtc);
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
	public string OnlineSince { get; }
	public string OnlineFor { get; }
	public string LastSeen { get; }
	public Guid SessionId { get; }
	public Guid ClientInstanceId { get; }
	public string StartedLocalText => StartedLocal.ToString("F");
	public string LastSeenLocalText => LastSeenLocal.ToString("F");

	private static string FormatDuration(DateTime startedUtc, DateTime asOfUtc)
	{
		var elapsed = asOfUtc - startedUtc;
		if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
		if (elapsed.TotalMinutes < 1) return "< 1 min";
		if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min";
		if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
		return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m";
	}

	private static string RelativeTime(DateTime lastSeenUtc, DateTime asOfUtc)
	{
		var elapsed = asOfUtc - lastSeenUtc;
		if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
		if (elapsed.TotalSeconds < 60) return $"{Math.Max(0, (int)elapsed.TotalSeconds)} sec ago";
		if (elapsed.TotalMinutes < 60) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
		return $"{Math.Max(1, (int)elapsed.TotalHours)} hr ago";
	}
}
