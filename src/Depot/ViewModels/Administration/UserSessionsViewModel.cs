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
	private IReadOnlyList<EndedUserSession> _allHistory = [];
	private CancellationTokenSource? _pollingCancellation;
	private string _searchText = string.Empty;
	private UserSessionRowViewModel? _selectedSession;
	private long _onlineUsers;
	private long _activeSessions;
	private DateTime _asOfUtc;
	private int _idleTimeoutMinutes = UserSessionPolicy.DefaultIdleTimeoutMinutes;
	private int _maximumSessionAgeHours = UserSessionPolicy.DefaultMaximumSessionAgeHours;
	private ConcurrentSessionMode _concurrentSessionMode = ConcurrentSessionMode.Unlimited;
	private int _maximumConcurrentSessions = UserSessionPolicy.DefaultMaximumConcurrentSessions;
	private ConcurrentSessionLimitAction _concurrentSessionLimitAction = ConcurrentSessionLimitAction.RejectNewSession;
	private int _sessionHistoryRetentionDays = UserSessionPolicy.DefaultSessionHistoryRetentionDays;
	private long _policyVersion = 1;
	private bool _policyDirty;
	private bool _loadingPolicy;
	private bool _disposed;

	public UserSessionsViewModel(UserSessionAdministrationService service, IFileDialogService? dialogs = null, UserSessionPresenceOptions? options = null)
	{
		_service = service;
		_dialogs = dialogs;
		_options = options ?? UserSessionPresenceOptions.Default;
		TerminateSessionCommand = new AsyncRelayCommand(TerminateSelectedSessionAsync, CanTerminateSelectedSession);
		TerminateUserSessionsCommand = new AsyncRelayCommand(TerminateSelectedUserSessionsAsync, CanTerminateSelectedSession);
		SavePolicyCommand = new AsyncRelayCommand(SavePolicyAsync, CanSavePolicy);
	}

	public ObservableCollection<UserSessionRowViewModel> Sessions { get; } = [];
	public ObservableCollection<UserSessionHistoryRowViewModel> History { get; } = [];
	public AsyncRelayCommand TerminateSessionCommand { get; }
	public AsyncRelayCommand TerminateUserSessionsCommand { get; }
	public AsyncRelayCommand SavePolicyCommand { get; }
	public bool CanTerminateSessions => _service.CanTerminateSessions;
	public bool CanManagePolicy => _service.CanManagePolicy;
	public IReadOnlyList<ConcurrentSessionMode> ConcurrentSessionModeOptions { get; } = Enum.GetValues<ConcurrentSessionMode>();
	public IReadOnlyList<ConcurrentSessionLimitAction> ConcurrentSessionLimitActionOptions { get; } = Enum.GetValues<ConcurrentSessionLimitAction>();
	public string PolicyRangeHint => $"Idle {UserSessionPolicy.MinimumIdleTimeoutMinutes}-{UserSessionPolicy.MaximumIdleTimeoutMinutes} min · lifetime {UserSessionPolicy.MinimumMaximumSessionAgeHours}-{UserSessionPolicy.MaximumMaximumSessionAgeHours} h · concurrent {UserSessionPolicy.MinimumConcurrentSessions}-{UserSessionPolicy.MaximumAllowedConcurrentSessions} · history {UserSessionPolicy.MinimumSessionHistoryRetentionDays}-{UserSessionPolicy.MaximumSessionHistoryRetentionDays} days";
	public bool IsMaximumConcurrentSessionsEnabled => ConcurrentSessionMode == ConcurrentSessionMode.MaximumSessions;

	public string SearchText
	{
		get => _searchText;
		set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); ApplyFilter(); }
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
			TerminateUserSessionsCommand.RaiseCanExecuteChanged();
		}
	}

	public long OnlineUsers { get => _onlineUsers; private set { _onlineUsers = value; OnPropertyChanged(); } }
	public long ActiveSessions { get => _activeSessions; private set { _activeSessions = value; OnPropertyChanged(); } }

	public int IdleTimeoutMinutes
	{
		get => _idleTimeoutMinutes;
		set { if (_idleTimeoutMinutes == value) return; _idleTimeoutMinutes = value; OnPropertyChanged(); MarkPolicyDirty(); }
	}

	public int MaximumSessionAgeHours
	{
		get => _maximumSessionAgeHours;
		set { if (_maximumSessionAgeHours == value) return; _maximumSessionAgeHours = value; OnPropertyChanged(); MarkPolicyDirty(); }
	}

	public ConcurrentSessionMode ConcurrentSessionMode
	{
		get => _concurrentSessionMode;
		set
		{
			if (_concurrentSessionMode == value) return;
			_concurrentSessionMode = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsMaximumConcurrentSessionsEnabled));
			MarkPolicyDirty();
		}
	}

	public int MaximumConcurrentSessions
	{
		get => _maximumConcurrentSessions;
		set { if (_maximumConcurrentSessions == value) return; _maximumConcurrentSessions = value; OnPropertyChanged(); MarkPolicyDirty(); }
	}

	public ConcurrentSessionLimitAction ConcurrentSessionLimitAction
	{
		get => _concurrentSessionLimitAction;
		set { if (_concurrentSessionLimitAction == value) return; _concurrentSessionLimitAction = value; OnPropertyChanged(); MarkPolicyDirty(); }
	}

	public int SessionHistoryRetentionDays
	{
		get => _sessionHistoryRetentionDays;
		set { if (_sessionHistoryRetentionDays == value) return; _sessionHistoryRetentionDays = value; OnPropertyChanged(); MarkPolicyDirty(); }
	}

	public bool IsPolicyDirty
	{
		get => _policyDirty;
		private set
		{
			if (_policyDirty == value) return;
			_policyDirty = value;
			OnPropertyChanged();
			SavePolicyCommand.RaiseCanExecuteChanged();
		}
	}

	public bool HasSessions => Sessions.Count > 0;
	public bool HasNoSessions => !HasSessions;
	public bool HasHistory => History.Count > 0;
	public bool HasNoHistory => !HasHistory;
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
			_allHistory = snapshot.History;
			_asOfUtc = snapshot.AsOfUtc;
			OnlineUsers = snapshot.Metrics.OnlineUsers;
			ActiveSessions = snapshot.Metrics.ActiveSessions;
			if (!IsPolicyDirty) ApplyPolicy(snapshot.Policy);
			ApplyFilter();
			SelectedSession = selectedId is null ? null : Sessions.FirstOrDefault(row => row.SessionId == selectedId);
			CompleteOperation(Sessions.Count == 0, "User sessions refreshed");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { if (!_disposed) CompleteOperation(Sessions.Count == 0); }
		catch (Exception exception) { if (!_disposed) FailOperation(exception, "User sessions could not be loaded"); }
		finally { _refreshGate.Release(); }
	}

	public void StartPolling()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		StopPolling();
		_pollingCancellation = new CancellationTokenSource();
		_ = RunPollingAsync(_pollingCancellation.Token);
	}

	public void StopPolling()
	{
		_pollingCancellation?.Cancel();
		_pollingCancellation?.Dispose();
		_pollingCancellation = null;
	}

	private async Task SavePolicyAsync(CancellationToken cancellationToken)
	{
		if (!CanManagePolicy || !IsPolicyDirty) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest(
			"Save session policy?",
			"Apply the new lifetime, concurrent-session and history-retention policy? Stricter limits may expire, reject or supersede sessions.",
			true))) return;

		BeginOperation("Saving session policy");
		try
		{
			var saved = await _service.SavePolicyAsync(
				IdleTimeoutMinutes,
				MaximumSessionAgeHours,
				ConcurrentSessionMode,
				MaximumConcurrentSessions,
				ConcurrentSessionLimitAction,
				SessionHistoryRetentionDays,
				_policyVersion,
				cancellationToken);
			ApplyPolicy(saved);
			await LoadAsync(cancellationToken);
			CompleteOperation(Sessions.Count == 0, "Session policy saved");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Session policy could not be saved"); }
	}

	private async Task TerminateSelectedSessionAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedSession;
		if (selected is null || !CanTerminateSessions) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest("Terminate session?", $"End the active session for {selected.UserDisplayName} on {selected.MachineName}?", true))) return;
		await ExecuteTerminationAsync(() => _service.TerminateSessionAsync(selected.SessionId, cancellationToken), "Session terminated", cancellationToken);
	}

	private async Task TerminateSelectedUserSessionsAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedSession;
		if (selected is null || !CanTerminateSessions) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest("Terminate all user sessions?", $"End every open session for {selected.UserDisplayName}?", true))) return;
		BeginOperation("Terminating user sessions");
		try
		{
			var count = await _service.TerminateUserSessionsAsync(selected.UserId, cancellationToken);
			SelectedSession = null;
			await LoadAsync(cancellationToken);
			CompleteOperation(Sessions.Count == 0, count == 0 ? "No open sessions remained" : $"{count:N0} sessions terminated");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "User sessions could not be terminated"); }
	}

	private async Task ExecuteTerminationAsync(Func<Task<bool>> terminate, string successText, CancellationToken cancellationToken)
	{
		BeginOperation("Terminating user session");
		try
		{
			var terminated = await terminate();
			SelectedSession = null;
			await LoadAsync(cancellationToken);
			CompleteOperation(Sessions.Count == 0, terminated ? successText : "Session was already ended");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Session could not be terminated"); }
	}

	private bool CanTerminateSelectedSession() => SelectedSession is not null && CanTerminateSessions;
	private bool CanSavePolicy() => CanManagePolicy && IsPolicyDirty;

	private async Task RunPollingAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_options.AdministrationRefreshInterval);
		try
		{
			await LoadAsync(cancellationToken);
			while (await timer.WaitForNextTickAsync(cancellationToken)) await LoadAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
	}

	private void ApplyPolicy(UserSessionPolicy policy)
	{
		_loadingPolicy = true;
		try
		{
			_policyVersion = policy.Version;
			IdleTimeoutMinutes = policy.IdleTimeoutMinutes;
			MaximumSessionAgeHours = policy.MaximumSessionAgeHours;
			ConcurrentSessionMode = policy.ConcurrentSessionMode;
			MaximumConcurrentSessions = policy.MaximumConcurrentSessions;
			ConcurrentSessionLimitAction = policy.ConcurrentSessionLimitAction;
			SessionHistoryRetentionDays = policy.SessionHistoryRetentionDays;
			IsPolicyDirty = false;
		}
		finally { _loadingPolicy = false; }
	}

	private void MarkPolicyDirty()
	{
		if (_loadingPolicy) return;
		IsPolicyDirty = true;
	}

	private void ApplyFilter()
	{
		var search = SearchText.Trim();
		var activeRows = _allSessions.Where(session => Matches(search, session.UserDisplayName, session.UserEmail, session.MachineName)).Select(session => new UserSessionRowViewModel(session, _asOfUtc)).ToArray();
		var historyRows = _allHistory.Where(session => Matches(search, session.UserDisplayName, session.UserEmail, session.MachineName)).Select(session => new UserSessionHistoryRowViewModel(session)).ToArray();
		CollectionSynchronizer.Replace(Sessions, activeRows);
		CollectionSynchronizer.Replace(History, historyRows);
		OnPropertyChanged(nameof(HasSessions));
		OnPropertyChanged(nameof(HasNoSessions));
		OnPropertyChanged(nameof(HasHistory));
		OnPropertyChanged(nameof(HasNoHistory));
	}

	private static bool Matches(string search, string name, string email, string? machine) => string.IsNullOrEmpty(search)
		|| name.Contains(search, StringComparison.OrdinalIgnoreCase)
		|| email.Contains(search, StringComparison.OrdinalIgnoreCase)
		|| (machine?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		StopPolling();
		TerminateSessionCommand.Dispose();
		TerminateUserSessionsCommand.Dispose();
		SavePolicyCommand.Dispose();
		_refreshGate.Dispose();
	}
}

public sealed class UserSessionRowViewModel
{
	public UserSessionRowViewModel(ActiveUserSession session, DateTime asOfUtc)
	{
		UserId = session.UserId;
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
	public long UserId { get; }
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
	internal static string FormatDuration(DateTime startedUtc, DateTime asOfUtc)
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

public sealed class UserSessionHistoryRowViewModel
{
	public UserSessionHistoryRowViewModel(EndedUserSession session)
	{
		UserDisplayName = session.UserDisplayName;
		UserEmail = session.UserEmail;
		MachineName = string.IsNullOrWhiteSpace(session.MachineName) ? "Unknown client" : session.MachineName;
		AppVersion = string.IsNullOrWhiteSpace(session.AppVersion) ? "Unknown" : session.AppVersion;
		StartedLocal = session.StartedUtc.ToLocalTime();
		EndedLocal = session.EndedUtc.ToLocalTime();
		Started = StartedLocal.ToString("g");
		Ended = EndedLocal.ToString("g");
		Duration = UserSessionRowViewModel.FormatDuration(session.StartedUtc, session.EndedUtc);
		EndReason = session.EndReason.ToString();
	}
	public string UserDisplayName { get; }
	public string UserEmail { get; }
	public string MachineName { get; }
	public string AppVersion { get; }
	public DateTime StartedLocal { get; }
	public DateTime EndedLocal { get; }
	public string Started { get; }
	public string Ended { get; }
	public string Duration { get; }
	public string EndReason { get; }
}
