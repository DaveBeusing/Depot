// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed record UserSessionPresenceSnapshot(
	IReadOnlyList<ActiveUserSession> Sessions,
	IReadOnlyList<EndedUserSession> History,
	UserSessionPresenceMetrics Metrics,
	UserSessionPolicy Policy,
	DateTime AsOfUtc);

public sealed class UserSessionAdministrationService
{
	private const int HistoryLimit = 200;
	private readonly UserSessionRepository _repository;
	private readonly IAuthorizationService _authorization;
	private readonly TimeProvider _timeProvider;
	private readonly UserSessionPresenceOptions _options;
	private readonly AuditService? _audit;
	private readonly SecurityEventService? _securityEvents;

	public UserSessionAdministrationService(
		UserSessionRepository repository,
		IAuthorizationService authorization,
		TimeProvider? timeProvider = null,
		UserSessionPresenceOptions? options = null,
		AuditService? audit = null,
		SecurityEventService? securityEvents = null)
	{
		_repository = repository;
		_authorization = authorization;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_options = options ?? UserSessionPresenceOptions.Default;
		_audit = audit;
		_securityEvents = securityEvents;
	}

	public bool CanTerminateSessions => _authorization.HasPermission(ApplicationPermission.UserSessionsTerminate);
	public bool CanManagePolicy => _authorization.HasPermission(ApplicationPermission.SettingsManage);

	public async Task<UserSessionPresenceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersView);
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var policy = await _repository.GetPolicyAsync(cancellationToken);
		await _repository.ExpireSessionsByPolicyAsync(now, policy, cancellationToken);
		var cutoff = now - _options.PresenceTimeout;
		var sessionsTask = _repository.GetActiveSessionsAsync(cutoff, null, cancellationToken);
		var historyTask = _repository.GetRecentEndedSessionsAsync(HistoryLimit, cancellationToken);
		var metricsTask = _repository.GetPresenceMetricsAsync(cutoff, cancellationToken);
		await Task.WhenAll(sessionsTask, historyTask, metricsTask);
		return new UserSessionPresenceSnapshot(await sessionsTask, await historyTask, await metricsTask ?? new UserSessionPresenceMetrics(0, 0), policy, now);
	}

	public async Task<UserSessionPolicy> SavePolicyAsync(int idleTimeoutMinutes, int maximumSessionAgeHours, long expectedVersion, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.SettingsManage);
		ValidatePolicy(idleTimeoutMinutes, maximumSessionAgeHours);
		var before = await _repository.GetPolicyAsync(cancellationToken);
		if (before.Version != expectedVersion) throw new ConcurrencyConflictException("user session policy");
		var after = CopyPolicy(before);
		after.IdleTimeoutMinutes = idleTimeoutMinutes;
		after.MaximumSessionAgeHours = maximumSessionAgeHours;
		after.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
		if (!await _repository.UpdatePolicyAsync(after, expectedVersion, cancellationToken)) throw new ConcurrencyConflictException("user session policy");
		after.Version = expectedVersion + 1;
		await _repository.ExpireSessionsByPolicyAsync(after.UpdatedUtc, after, cancellationToken);
		if (_audit is not null) await _audit.RecordActionAsync(after.Id, "UpdateSessionPolicy", before, after, cancellationToken);
		if (_securityEvents is not null) await _securityEvents.RecordPolicyChangedAsync(before, after, cancellationToken);
		return after;
	}

	public async Task<bool> TerminateSessionAsync(Guid sessionId, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UserSessionsTerminate);
		var before = await _repository.GetBySessionIdAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("The session was not found.");
		if (before.EndedUtc is not null) return false;
		var endedUtc = _timeProvider.GetUtcNow().UtcDateTime;
		if (!await _repository.EndAsync(sessionId, endedUtc, UserSessionEndReason.AdministrativeLogout, cancellationToken)) return false;
		await AuditEndedSessionAsync(before, endedUtc, "AdministrativeLogout", cancellationToken);
		if (_securityEvents is not null)
			await _securityEvents.RecordSessionEventAsync(before, SecurityEventType.AdministrativeSessionTermination, SecurityEventSeverity.Warning, "User session terminated administratively", null, cancellationToken);
		return true;
	}

	public async Task<int> TerminateUserSessionsAsync(long userId, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UserSessionsTerminate);
		if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
		var openSessions = await _repository.GetOpenSessionsForUserAsync(userId, cancellationToken);
		if (openSessions.Count == 0) return 0;
		var endedUtc = _timeProvider.GetUtcNow().UtcDateTime;
		var ended = await _repository.EndActiveSessionsForUserAsync(userId, endedUtc, UserSessionEndReason.AdministrativeLogout, cancellationToken);
		foreach (var session in openSessions)
		{
			if (_audit is not null) await AuditEndedSessionAsync(session, endedUtc, "AdministrativeLogoutAll", cancellationToken);
			if (_securityEvents is not null)
				await _securityEvents.RecordSessionEventAsync(session, SecurityEventType.AdministrativeSessionTermination, SecurityEventSeverity.Warning, "User session terminated by bulk administrative action", null, cancellationToken);
		}
		return ended;
	}

	private static void ValidatePolicy(int idleTimeoutMinutes, int maximumSessionAgeHours)
	{
		if (idleTimeoutMinutes is < UserSessionPolicy.MinimumIdleTimeoutMinutes or > UserSessionPolicy.MaximumIdleTimeoutMinutes)
			throw new ArgumentOutOfRangeException(nameof(idleTimeoutMinutes), $"Idle timeout must be between {UserSessionPolicy.MinimumIdleTimeoutMinutes} and {UserSessionPolicy.MaximumIdleTimeoutMinutes} minutes.");
		if (maximumSessionAgeHours is < UserSessionPolicy.MinimumMaximumSessionAgeHours or > UserSessionPolicy.MaximumMaximumSessionAgeHours)
			throw new ArgumentOutOfRangeException(nameof(maximumSessionAgeHours), $"Maximum session age must be between {UserSessionPolicy.MinimumMaximumSessionAgeHours} and {UserSessionPolicy.MaximumMaximumSessionAgeHours} hours.");
	}

	private async Task AuditEndedSessionAsync(UserSession before, DateTime endedUtc, string action, CancellationToken cancellationToken)
	{
		if (_audit is null) return;
		var after = Copy(before); after.EndedUtc = endedUtc; after.EndReason = UserSessionEndReason.AdministrativeLogout; after.Version++;
		await _audit.RecordActionAsync(before.Id, action, before, after, cancellationToken);
	}

	private static UserSession Copy(UserSession session) => new()
	{
		Id = session.Id, SessionId = session.SessionId, UserId = session.UserId, StartedUtc = session.StartedUtc,
		LastSeenUtc = session.LastSeenUtc, LastActivityUtc = session.LastActivityUtc, EndedUtc = session.EndedUtc,
		EndReason = session.EndReason, ClientInstanceId = session.ClientInstanceId, MachineName = session.MachineName,
		AppVersion = session.AppVersion, Version = session.Version
	};

	private static UserSessionPolicy CopyPolicy(UserSessionPolicy policy) => new()
	{
		Id = policy.Id, IdleTimeoutMinutes = policy.IdleTimeoutMinutes, MaximumSessionAgeHours = policy.MaximumSessionAgeHours,
		UpdatedUtc = policy.UpdatedUtc, Version = policy.Version
	};
}
