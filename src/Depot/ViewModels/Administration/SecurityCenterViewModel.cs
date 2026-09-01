// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class SecurityCenterViewModel : BaseViewModel, IDisposable
{
	private readonly SecurityAdministrationService _service;
	private readonly IFileDialogService? _dialogs;
	private string _searchText = string.Empty;
	private SecurityEventSeverity? _minimumSeverity;
	private bool _onlyUnreviewed = true;
	private SecurityEventRowViewModel? _selectedEvent;
	private CancellationTokenSource? _investigationCancellation;
	private long _events24Hours;
	private long _suspicious24Hours;
	private long _highRiskOpen;
	private long _blocked24Hours;
	private long _reviewed24Hours;
	private long _openUnreviewed;
	private long? _investigationUserId;
	private string _investigationUser = "No Depot user resolved";
	private string _investigationUserStatus = "—";
	private int _failureWindowMinutes = AuthenticationSecurityPolicy.DefaultFailureWindowMinutes;
	private int _lockoutThreshold = AuthenticationSecurityPolicy.DefaultLockoutThreshold;
	private int _lockoutDurationMinutes = AuthenticationSecurityPolicy.DefaultLockoutDurationMinutes;
	private int _securityEventRetentionDays = AuthenticationSecurityPolicy.DefaultSecurityEventRetentionDays;
	private long _authenticationPolicyVersion = 1;
	private bool _authenticationPolicyDirty;
	private bool _loadingAuthenticationPolicy;
	private bool _disposed;

	public SecurityCenterViewModel(SecurityEventService service, IFileDialogService? dialogs = null)
		: this(service.Administration, dialogs) { }

	public SecurityCenterViewModel(SecurityAdministrationService service, IFileDialogService? dialogs = null)
	{
		_service = service;
		_dialogs = dialogs;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		MarkReviewedCommand = new AsyncRelayCommand(MarkReviewedAsync, CanMarkReviewed);
		SaveAuthenticationPolicyCommand = new AsyncRelayCommand(SaveAuthenticationPolicyAsync, () => CanManageAuthenticationPolicy && IsAuthenticationPolicyDirty);
		TerminateSessionCommand = new AsyncRelayCommand(TerminateSelectedSessionAsync, () => CanTerminateSessions && SelectedEvent?.HasSession == true);
		TerminateUserSessionsCommand = new AsyncRelayCommand(TerminateSelectedUserSessionsAsync, () => CanTerminateSessions && InvestigationUserId is not null);
		DeactivateUserCommand = new AsyncRelayCommand(DeactivateSelectedUserAsync, () => CanDeactivateUsers && InvestigationUserId is not null && InvestigationUserStatus == "Active");
	}

	public ObservableCollection<SecurityEventRowViewModel> Events { get; } = [];
	public ObservableCollection<SecurityEventRowViewModel> RelatedEvents { get; } = [];
	public ObservableCollection<SecurityInvestigationSessionRow> InvestigationSessions { get; } = [];
	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand MarkReviewedCommand { get; }
	public AsyncRelayCommand SaveAuthenticationPolicyCommand { get; }
	public AsyncRelayCommand TerminateSessionCommand { get; }
	public AsyncRelayCommand TerminateUserSessionsCommand { get; }
	public AsyncRelayCommand DeactivateUserCommand { get; }
	public bool CanManage => _service.CanManageEvents;
	public bool CanManageAuthenticationPolicy => _service.CanManageAuthenticationPolicy;
	public bool CanTerminateSessions => _service.CanTerminateSessions;
	public bool CanDeactivateUsers => _service.CanDeactivateUsers;
	public bool HasEvents => Events.Count > 0;
	public bool HasNoEvents => !HasEvents;
	public bool HasRelatedEvents => RelatedEvents.Count > 0;
	public bool HasInvestigationSessions => InvestigationSessions.Count > 0;
	public string AuthenticationPolicyRangeHint => $"Failure window {AuthenticationSecurityPolicy.MinimumFailureWindowMinutes}-{AuthenticationSecurityPolicy.MaximumFailureWindowMinutes} min · threshold {AuthenticationSecurityPolicy.MinimumLockoutThreshold}-{AuthenticationSecurityPolicy.MaximumLockoutThreshold} · lockout {AuthenticationSecurityPolicy.MinimumLockoutDurationMinutes}-{AuthenticationSecurityPolicy.MaximumLockoutDurationMinutes} min · retention {AuthenticationSecurityPolicy.MinimumSecurityEventRetentionDays}-{AuthenticationSecurityPolicy.MaximumSecurityEventRetentionDays} days";

	public long Events24Hours { get => _events24Hours; private set { _events24Hours = value; OnPropertyChanged(); } }
	public long Suspicious24Hours { get => _suspicious24Hours; private set { _suspicious24Hours = value; OnPropertyChanged(); } }
	public long HighRiskOpen { get => _highRiskOpen; private set { _highRiskOpen = value; OnPropertyChanged(); } }
	public long Blocked24Hours { get => _blocked24Hours; private set { _blocked24Hours = value; OnPropertyChanged(); } }
	public long Reviewed24Hours { get => _reviewed24Hours; private set { _reviewed24Hours = value; OnPropertyChanged(); } }
	public long OpenUnreviewed { get => _openUnreviewed; private set { _openUnreviewed = value; OnPropertyChanged(); } }
	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); } }
	public SecurityEventSeverity? MinimumSeverity { get => _minimumSeverity; set { if (_minimumSeverity == value) return; _minimumSeverity = value; OnPropertyChanged(); } }
	public bool OnlyUnreviewed { get => _onlyUnreviewed; set { if (_onlyUnreviewed == value) return; _onlyUnreviewed = value; OnPropertyChanged(); } }
	public IReadOnlyList<SecurityEventSeverity?> SeverityOptions { get; } = [null, SecurityEventSeverity.Warning, SecurityEventSeverity.High, SecurityEventSeverity.Critical];
	public long? InvestigationUserId { get => _investigationUserId; private set { _investigationUserId = value; OnPropertyChanged(); RaiseResponseCanExecuteChanged(); } }
	public string InvestigationUser { get => _investigationUser; private set { _investigationUser = value; OnPropertyChanged(); } }
	public string InvestigationUserStatus { get => _investigationUserStatus; private set { _investigationUserStatus = value; OnPropertyChanged(); RaiseResponseCanExecuteChanged(); } }

	public SecurityEventRowViewModel? SelectedEvent
	{
		get => _selectedEvent;
		set
		{
			if (_selectedEvent == value) return;
			_selectedEvent = value;
			OnPropertyChanged();
			MarkReviewedCommand.RaiseCanExecuteChanged();
			RaiseResponseCanExecuteChanged();
			StartInvestigation(value);
		}
	}

	public int FailureWindowMinutes { get => _failureWindowMinutes; set { if (_failureWindowMinutes == value) return; _failureWindowMinutes = value; OnPropertyChanged(); MarkAuthenticationPolicyDirty(); } }
	public int LockoutThreshold { get => _lockoutThreshold; set { if (_lockoutThreshold == value) return; _lockoutThreshold = value; OnPropertyChanged(); MarkAuthenticationPolicyDirty(); } }
	public int LockoutDurationMinutes { get => _lockoutDurationMinutes; set { if (_lockoutDurationMinutes == value) return; _lockoutDurationMinutes = value; OnPropertyChanged(); MarkAuthenticationPolicyDirty(); } }
	public int SecurityEventRetentionDays { get => _securityEventRetentionDays; set { if (_securityEventRetentionDays == value) return; _securityEventRetentionDays = value; OnPropertyChanged(); MarkAuthenticationPolicyDirty(); } }
	public bool IsAuthenticationPolicyDirty
	{
		get => _authenticationPolicyDirty;
		private set
		{
			if (_authenticationPolicyDirty == value) return;
			_authenticationPolicyDirty = value;
			OnPropertyChanged();
			SaveAuthenticationPolicyCommand.RaiseCanExecuteChanged();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		BeginOperation("Refreshing security center");
		try
		{
			var selectedId = SelectedEvent?.Id;
			var snapshot = await _service.GetSnapshotAsync(new SecurityEventFilter(SearchText, MinimumSeverity, OnlyUnreviewed ? false : null), cancellationToken);
			ApplyMetrics(snapshot.Metrics);
			CollectionSynchronizer.Replace(Events, snapshot.Events.Select(item => new SecurityEventRowViewModel(item)).ToArray());
			if (!IsAuthenticationPolicyDirty) ApplyAuthenticationPolicy(snapshot.AuthenticationPolicy);
			OnPropertyChanged(nameof(HasEvents));
			OnPropertyChanged(nameof(HasNoEvents));
			SelectedEvent = selectedId is null ? null : Events.FirstOrDefault(item => item.Id == selectedId);
			CompleteOperation(Events.Count == 0, "Security center refreshed");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { CompleteOperation(Events.Count == 0); }
		catch (Exception exception) { FailOperation(exception, "Security center could not be loaded"); }
	}

	private async Task MarkReviewedAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedEvent;
		if (selected is null || selected.IsReviewed || !CanManage) return;
		BeginOperation("Marking security event reviewed");
		try
		{
			await _service.MarkReviewedAsync(selected.Id, selected.Version, cancellationToken);
			await LoadAsync(cancellationToken);
			CompleteOperation(Events.Count == 0, "Security event reviewed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Security event could not be reviewed"); }
	}

	private async Task SaveAuthenticationPolicyAsync(CancellationToken cancellationToken)
	{
		if (!CanManageAuthenticationPolicy || !IsAuthenticationPolicyDirty) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest("Save authentication security policy?", "Apply the login-failure window, lockout and security-event retention policy for all Depot clients?", true))) return;
		BeginOperation("Saving authentication security policy");
		try
		{
			var saved = await _service.SaveAuthenticationPolicyAsync(FailureWindowMinutes, LockoutThreshold, LockoutDurationMinutes, SecurityEventRetentionDays, _authenticationPolicyVersion, cancellationToken);
			ApplyAuthenticationPolicy(saved);
			CompleteOperation(false, "Authentication security policy saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Authentication security policy could not be saved"); }
	}

	private async Task TerminateSelectedSessionAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedEvent;
		if (selected is null || !CanTerminateSessions || !selected.HasSession) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest("Terminate referenced session?", "End the active session referenced by this security event?", true))) return;
		await ExecuteResponseAsync(() => _service.TerminateSelectedSessionAsync(selected.Source, cancellationToken), "Referenced session terminated", cancellationToken);
	}

	private async Task TerminateSelectedUserSessionsAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedEvent;
		if (selected is null || InvestigationUserId is null || !CanTerminateSessions) return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest("Terminate all sessions?", $"End every open session for {InvestigationUser}?", true))) return;
		await ExecuteResponseAsync(async () => await _service.TerminateSelectedUserSessionsAsync(selected.Source, cancellationToken) > 0, "User sessions terminated", cancellationToken);
	}

	private async Task DeactivateSelectedUserAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedEvent;
		if (selected is null || InvestigationUserId is null || !CanDeactivateUsers || InvestigationUserStatus != "Active") return;
		if (_dialogs is not null && !_dialogs.Confirm(new ConfirmationDialogRequest("Deactivate user?", $"Deactivate {InvestigationUser} and revoke all open sessions?", true))) return;
		await ExecuteResponseAsync(async () => { await _service.DeactivateSelectedUserAsync(selected.Source, cancellationToken); return true; }, "User deactivated", cancellationToken);
	}

	private async Task ExecuteResponseAsync(Func<Task<bool>> action, string success, CancellationToken cancellationToken)
	{
		BeginOperation("Applying security response");
		try
		{
			var changed = await action();
			await LoadAsync(cancellationToken);
			CompleteOperation(Events.Count == 0, changed ? success : "No active target remained");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Security response could not be completed"); }
	}

	private void StartInvestigation(SecurityEventRowViewModel? selected)
	{
		_investigationCancellation?.Cancel();
		_investigationCancellation?.Dispose();
		_investigationCancellation = null;
		CollectionSynchronizer.Replace(RelatedEvents, Array.Empty<SecurityEventRowViewModel>());
		CollectionSynchronizer.Replace(InvestigationSessions, Array.Empty<SecurityInvestigationSessionRow>());
		InvestigationUserId = null;
		InvestigationUser = "No Depot user resolved";
		InvestigationUserStatus = "—";
		OnPropertyChanged(nameof(HasRelatedEvents));
		OnPropertyChanged(nameof(HasInvestigationSessions));
		if (selected is null) return;
		_investigationCancellation = new CancellationTokenSource();
		_ = LoadInvestigationAsync(selected, _investigationCancellation.Token);
	}

	private async Task LoadInvestigationAsync(SecurityEventRowViewModel selected, CancellationToken cancellationToken)
	{
		try
		{
			var context = await _service.GetInvestigationAsync(selected.Source, cancellationToken);
			if (cancellationToken.IsCancellationRequested || SelectedEvent?.Id != selected.Id) return;
			InvestigationUserId = context.UserId;
			InvestigationUser = context.UserId is null ? "No Depot user resolved" : $"{context.UserDisplayName} · {context.UserEmail}";
			InvestigationUserStatus = context.UserIsActive is null ? "—" : context.UserIsActive.Value ? "Active" : "Inactive";
			CollectionSynchronizer.Replace(RelatedEvents, context.RelatedEvents.Select(item => new SecurityEventRowViewModel(item)).ToArray());
			CollectionSynchronizer.Replace(InvestigationSessions, context.OpenSessions.Select(item => new SecurityInvestigationSessionRow(item)).ToArray());
			OnPropertyChanged(nameof(HasRelatedEvents));
			OnPropertyChanged(nameof(HasInvestigationSessions));
			RaiseResponseCanExecuteChanged();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Security investigation context could not be loaded"); }
	}

	private void ApplyMetrics(SecurityCenterMetrics metrics)
	{
		Events24Hours = metrics.Events24Hours;
		Suspicious24Hours = metrics.Suspicious24Hours;
		HighRiskOpen = metrics.OpenHighRisk;
		Blocked24Hours = metrics.Blocked24Hours;
		Reviewed24Hours = metrics.Reviewed24Hours;
		OpenUnreviewed = metrics.OpenUnreviewed;
	}

	private void ApplyAuthenticationPolicy(AuthenticationSecurityPolicy policy)
	{
		_loadingAuthenticationPolicy = true;
		try
		{
			_authenticationPolicyVersion = policy.Version;
			FailureWindowMinutes = policy.FailureWindowMinutes;
			LockoutThreshold = policy.LockoutThreshold;
			LockoutDurationMinutes = policy.LockoutDurationMinutes;
			SecurityEventRetentionDays = policy.SecurityEventRetentionDays;
			IsAuthenticationPolicyDirty = false;
		}
		finally { _loadingAuthenticationPolicy = false; }
	}

	private void MarkAuthenticationPolicyDirty()
	{
		if (_loadingAuthenticationPolicy) return;
		IsAuthenticationPolicyDirty = true;
	}

	private bool CanMarkReviewed() => SelectedEvent is { IsReviewed: false } && CanManage;
	private void RaiseResponseCanExecuteChanged()
	{
		TerminateSessionCommand.RaiseCanExecuteChanged();
		TerminateUserSessionsCommand.RaiseCanExecuteChanged();
		DeactivateUserCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_investigationCancellation?.Cancel();
		_investigationCancellation?.Dispose();
		RefreshCommand.Dispose();
		MarkReviewedCommand.Dispose();
		SaveAuthenticationPolicyCommand.Dispose();
		TerminateSessionCommand.Dispose();
		TerminateUserSessionsCommand.Dispose();
		DeactivateUserCommand.Dispose();
	}
}

public sealed class SecurityEventRowViewModel
{
	internal SecurityEventRowViewModel(SecurityEventListItem item)
	{
		Source = item;
		Id = item.Id;
		Version = item.Version;
		TimestampLocal = item.TimestampLocal;
		EventType = Split(item.EventType.ToString());
		Severity = item.Severity.ToString();
		Account = string.IsNullOrWhiteSpace(item.AccountIdentifier) ? "—" : item.AccountIdentifier;
		MachineName = string.IsNullOrWhiteSpace(item.MachineName) ? "—" : item.MachineName;
		Summary = item.Summary;
		Details = string.IsNullOrWhiteSpace(item.Details) ? "—" : item.Details;
		IsReviewed = item.IsReviewed;
		Status = item.IsReviewed ? "Reviewed" : "Open";
		SessionId = item.SessionId?.ToString("D") ?? "—";
		ClientInstanceId = item.ClientInstanceId?.ToString("D") ?? "—";
		HasSession = item.SessionId is not null;
	}
	internal SecurityEventListItem Source { get; }
	public long Id { get; }
	public long Version { get; }
	public DateTime TimestampLocal { get; }
	public string EventType { get; }
	public string Severity { get; }
	public string Account { get; }
	public string MachineName { get; }
	public string Summary { get; }
	public string Details { get; }
	public bool IsReviewed { get; }
	public string Status { get; }
	public string SessionId { get; }
	public string ClientInstanceId { get; }
	public bool HasSession { get; }
	private static string Split(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : c.ToString()));
}

public sealed class SecurityInvestigationSessionRow
{
	public SecurityInvestigationSessionRow(UserSession session)
	{
		SessionId = session.SessionId.ToString("D");
		Client = string.IsNullOrWhiteSpace(session.MachineName) ? "Unknown client" : session.MachineName;
		Started = session.StartedUtc.ToLocalTime().ToString("g");
		LastSeen = session.LastSeenUtc.ToLocalTime().ToString("g");
	}
	public string SessionId { get; }
	public string Client { get; }
	public string Started { get; }
	public string LastSeen { get; }
}
