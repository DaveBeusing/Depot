// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed record UserSessionPresenceSnapshot(
	IReadOnlyList<ActiveUserSession> Sessions,
	UserSessionPresenceMetrics Metrics,
	DateTime AsOfUtc);

public sealed class UserSessionAdministrationService
{
	private readonly UserSessionRepository _repository;
	private readonly IAuthorizationService _authorization;
	private readonly TimeProvider _timeProvider;
	private readonly UserSessionPresenceOptions _options;
	private readonly AuditService? _audit;

	public UserSessionAdministrationService(
		UserSessionRepository repository,
		IAuthorizationService authorization,
		TimeProvider? timeProvider = null,
		UserSessionPresenceOptions? options = null,
		AuditService? audit = null)
	{
		_repository = repository;
		_authorization = authorization;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_options = options ?? UserSessionPresenceOptions.Default;
		_audit = audit;
	}

	public bool CanTerminateSessions => _authorization.HasPermission(ApplicationPermission.UserSessionsTerminate);

	public async Task<UserSessionPresenceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersView);
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var cutoff = now - _options.PresenceTimeout;
		var sessionsTask = _repository.GetActiveSessionsAsync(cutoff, null, cancellationToken);
		var metricsTask = _repository.GetPresenceMetricsAsync(cutoff, cancellationToken);
		await Task.WhenAll(sessionsTask, metricsTask);
		return new UserSessionPresenceSnapshot(
			await sessionsTask,
			await metricsTask ?? new UserSessionPresenceMetrics(0, 0),
			now);
	}

	public async Task<bool> TerminateSessionAsync(Guid sessionId, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UserSessionsTerminate);
		var before = await _repository.GetBySessionIdAsync(sessionId, cancellationToken)
			?? throw new InvalidOperationException("The session was not found.");
		if (before.EndedUtc is not null) return false;

		var endedUtc = _timeProvider.GetUtcNow().UtcDateTime;
		if (!await _repository.EndAsync(sessionId, endedUtc, UserSessionEndReason.AdministrativeLogout, cancellationToken)) return false;

		if (_audit is not null)
		{
			var after = Copy(before);
			after.EndedUtc = endedUtc;
			after.EndReason = UserSessionEndReason.AdministrativeLogout;
			after.Version++;
			await _audit.RecordActionAsync(before.Id, "AdministrativeLogout", before, after, cancellationToken);
		}
		return true;
	}

	private static UserSession Copy(UserSession session) => new()
	{
		Id = session.Id,
		SessionId = session.SessionId,
		UserId = session.UserId,
		StartedUtc = session.StartedUtc,
		LastSeenUtc = session.LastSeenUtc,
		LastActivityUtc = session.LastActivityUtc,
		EndedUtc = session.EndedUtc,
		EndReason = session.EndReason,
		ClientInstanceId = session.ClientInstanceId,
		MachineName = session.MachineName,
		AppVersion = session.AppVersion,
		Version = session.Version
	};
}
