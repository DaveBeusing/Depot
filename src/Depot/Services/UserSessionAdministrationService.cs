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

	public UserSessionAdministrationService(
		UserSessionRepository repository,
		IAuthorizationService authorization,
		TimeProvider? timeProvider = null,
		UserSessionPresenceOptions? options = null)
	{
		_repository = repository;
		_authorization = authorization;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_options = options ?? UserSessionPresenceOptions.Default;
	}

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
}
