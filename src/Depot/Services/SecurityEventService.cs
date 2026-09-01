// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SecurityEventService
{
	private const int RecentLimit = 250;
	private readonly SecurityEventRepository _repository;
	private readonly IAuthorizationService _authorization;
	private readonly INotificationService? _notifications;
	private readonly TimeProvider _timeProvider;

	public SecurityEventService(SecurityEventRepository repository, IAuthorizationService authorization, INotificationService? notifications = null, TimeProvider? timeProvider = null)
	{
		_repository = repository;
		_authorization = authorization;
		_notifications = notifications;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public bool CanManage => _authorization.HasPermission(ApplicationPermission.SecurityEventsManage);

	public Task<IReadOnlyList<SecurityEventListItem>> GetRecentAsync(SecurityEventFilter filter, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsView);
		return _repository.GetRecentAsync(filter, RecentLimit, cancellationToken);
	}

	public async Task<SecurityCenterMetrics> GetMetricsAsync(CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsView);
		var since = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-24);
		return await _repository.GetMetricsAsync(since, cancellationToken) ?? new SecurityCenterMetrics(0, 0, 0, 0);
	}

	public async Task MarkReviewedAsync(long id, long expectedVersion, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SecurityEventsManage);
		var userId = _authorization.CurrentUser?.Id ?? throw new UnauthorizedAccessException("An authenticated user is required.");
		if (!await _repository.MarkReviewedAsync(id, expectedVersion, userId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
			throw new ConcurrencyConflictException("security event");
	}

	public Task RecordAuthenticationFailureAsync(string accountIdentifier, long? userId, LoginAttemptStatus status, CancellationToken cancellationToken)
	{
		var type = status.IsBlocked ? SecurityEventType.AuthenticationBlocked : status.FailureCount >= 3 ? SecurityEventType.SuspiciousAuthenticationFailures : SecurityEventType.AuthenticationFailed;
		var severity = status.IsBlocked ? SecurityEventSeverity.Critical : status.FailureCount >= 4 ? SecurityEventSeverity.High : status.FailureCount >= 3 ? SecurityEventSeverity.Warning : SecurityEventSeverity.Information;
		var summary = status.IsBlocked ? "Authentication blocked after repeated failures" : status.FailureCount >= 3 ? "Repeated authentication failures detected" : "Authentication failed";
		var details = status.IsBlocked ? $"{status.FailureCount} failures in the active throttling window; retry after approximately {Math.Ceiling(status.RetryAfter.TotalMinutes):N0} minutes." : $"Failure {status.FailureCount} in the active 15-minute throttling window.";
		return RecordAsync(new SecurityEvent
		{
			TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime, EventType = type, Severity = severity, UserId = userId,
			AccountIdentifier = NormalizeAccount(accountIdentifier), Summary = summary, Details = details
		}, cancellationToken);
	}

	public Task RecordBlockedAttemptAsync(string accountIdentifier, TimeSpan retryAfter, CancellationToken cancellationToken) =>
		RecordAsync(new SecurityEvent
		{
			TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime, EventType = SecurityEventType.AuthenticationBlocked,
			Severity = SecurityEventSeverity.Critical, AccountIdentifier = NormalizeAccount(accountIdentifier),
			Summary = "Authentication attempt rejected during lockout",
			Details = $"The account key remained throttled; retry after approximately {Math.Ceiling(retryAfter.TotalMinutes):N0} minutes."
		}, cancellationToken);

	public Task RecordAuthenticationSuccessAsync(User user, int priorFailures, Guid? sessionId, string? machineName, CancellationToken cancellationToken)
	{
		var afterFailures = priorFailures > 0;
		return RecordAsync(new SecurityEvent
		{
			TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime,
			EventType = afterFailures ? SecurityEventType.AuthenticationSucceededAfterFailures : SecurityEventType.AuthenticationSucceeded,
			Severity = priorFailures >= 3 ? SecurityEventSeverity.High : afterFailures ? SecurityEventSeverity.Warning : SecurityEventSeverity.Information,
			UserId = user.Id, AccountIdentifier = NormalizeAccount(user.Email), SessionId = sessionId, MachineName = NormalizeMachine(machineName),
			Summary = afterFailures ? "Authentication succeeded after recent failures" : "Authentication succeeded",
			Details = afterFailures ? $"The account authenticated successfully after {priorFailures} recent failed attempt(s)." : null
		}, cancellationToken);
	}

	public Task RecordSessionEventAsync(UserSession session, SecurityEventType type, SecurityEventSeverity severity, string summary, string? details, CancellationToken cancellationToken) =>
		RecordAsync(CreateSessionEvent(session, type, severity, summary, details), cancellationToken);

	public Task RecordPolicyChangedAsync(UserSessionPolicy before, UserSessionPolicy after, CancellationToken cancellationToken) =>
		RecordAsync(CreatePolicyChangedEvent(before, after), cancellationToken);

	internal SecurityEvent CreateSessionEvent(UserSession session, SecurityEventType type, SecurityEventSeverity severity, string summary, string? details) => new()
	{
		TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime,
		EventType = type,
		Severity = severity,
		UserId = session.UserId,
		SessionId = session.SessionId,
		MachineName = NormalizeMachine(session.MachineName),
		Summary = summary,
		Details = details
	};

	internal SecurityEvent CreatePolicyChangedEvent(UserSessionPolicy before, UserSessionPolicy after) => new()
	{
		TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime,
		EventType = SecurityEventType.SessionPolicyChanged,
		Severity = SecurityEventSeverity.Warning,
		UserId = _authorization.CurrentUser?.Id,
		AccountIdentifier = _authorization.CurrentUser?.Email,
		Summary = "Session security policy changed",
		Details = $"Idle timeout {before.IdleTimeoutMinutes}→{after.IdleTimeoutMinutes} minutes; maximum age {before.MaximumSessionAgeHours}→{after.MaximumSessionAgeHours} hours."
	};

	internal async Task NotifyPersistedAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
	{
		if (_notifications is null || securityEvent.Severity < SecurityEventSeverity.High) return;
		try
		{
			await _notifications.NotifyPermissionHoldersAsync(
				new NotificationRequest(NotificationType.System, NotificationSeverity.Warning, "Security event", securityEvent.Summary, SourceId: securityEvent.Id),
				ApplicationPermission.SecurityEventsView, cancellationToken: cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception) { StartupDiagnostics.LogException(exception); }
	}

	private async Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
	{
		try
		{
			securityEvent.Id = await _repository.CreateAsync(securityEvent, cancellationToken);
			await NotifyPersistedAsync(securityEvent, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
		}
	}

	private static string NormalizeAccount(string value) => value.Trim().ToLowerInvariant();
	private static string? NormalizeMachine(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
