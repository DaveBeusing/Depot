// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed record SecurityAdministrationSnapshot(SecurityCenterMetrics Metrics, IReadOnlyList<SecurityEventListItem> Events, AuthenticationSecurityPolicy AuthenticationPolicy);
public sealed record SecurityInvestigationContext(long? UserId, string? UserDisplayName, string? UserEmail, bool? UserIsActive, IReadOnlyList<SecurityEventListItem> RelatedEvents, IReadOnlyList<UserSession> OpenSessions);

public sealed class SecurityAdministrationService
{
	private readonly SecurityEventService _securityEvents;
	private readonly AuthenticationSecurityService _authenticationSecurity;
	private readonly UserSessionAdministrationService _sessionAdministration;
	private readonly UserSessionRepository _sessions;
	private readonly UserRepository _users;
	private readonly UserService _userService;
	private readonly IAuthorizationService _authorization;

	public SecurityAdministrationService(SecurityEventService securityEvents, AuthenticationSecurityService authenticationSecurity, UserSessionAdministrationService sessionAdministration, UserSessionRepository sessions, UserRepository users, UserService userService, IAuthorizationService authorization)
	{
		_securityEvents = securityEvents;
		_authenticationSecurity = authenticationSecurity;
		_sessionAdministration = sessionAdministration;
		_sessions = sessions;
		_users = users;
		_userService = userService;
		_authorization = authorization;
	}

	public bool CanManageEvents => _securityEvents.CanManage;
	public bool CanManageAuthenticationPolicy => _authenticationSecurity.CanManagePolicy;
	public bool CanTerminateSessions => _sessionAdministration.CanTerminateSessions;
	public bool CanDeactivateUsers => _authorization.HasPermission(ApplicationPermission.UsersManage);

	public async Task<SecurityAdministrationSnapshot> GetSnapshotAsync(SecurityEventFilter filter, CancellationToken cancellationToken)
	{
		var metricsTask = _securityEvents.GetMetricsAsync(cancellationToken);
		var eventsTask = _securityEvents.GetRecentAsync(filter, cancellationToken);
		var policyTask = _authenticationSecurity.GetPolicyAsync(cancellationToken);
		await Task.WhenAll(metricsTask, eventsTask, policyTask);
		return new SecurityAdministrationSnapshot(await metricsTask, await eventsTask, await policyTask);
	}

	public async Task<SecurityInvestigationContext> GetInvestigationAsync(SecurityEventListItem anchor, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(anchor);
		var relatedTask = _securityEvents.GetRelatedAsync(anchor, cancellationToken);
		var user = CanResolveUser() ? await ResolveUserAsync(anchor, cancellationToken) : null;
		IReadOnlyList<UserSession> openSessions = [];
		if (user is not null && _authorization.HasPermission(ApplicationPermission.UsersView)) openSessions = await _sessions.GetOpenSessionsForUserAsync(user.Id, cancellationToken);
		return new SecurityInvestigationContext(user?.Id, user?.DisplayName, user?.Email, user?.IsActive, await relatedTask, openSessions);
	}

	public Task MarkReviewedAsync(long id, long expectedVersion, CancellationToken cancellationToken) => _securityEvents.MarkReviewedAsync(id, expectedVersion, cancellationToken);
	public Task<AuthenticationSecurityPolicy> SaveAuthenticationPolicyAsync(int failureWindowMinutes, int lockoutThreshold, int lockoutDurationMinutes, int securityEventRetentionDays, long expectedVersion, CancellationToken cancellationToken) => _authenticationSecurity.SavePolicyAsync(failureWindowMinutes, lockoutThreshold, lockoutDurationMinutes, securityEventRetentionDays, expectedVersion, cancellationToken);

	public Task<bool> TerminateSelectedSessionAsync(SecurityEventListItem anchor, CancellationToken cancellationToken)
	{
		if (anchor.SessionId is not { } sessionId) throw new InvalidOperationException("The selected event does not reference a session.");
		return _sessionAdministration.TerminateSessionAsync(sessionId, cancellationToken);
	}

	public async Task<int> TerminateSelectedUserSessionsAsync(SecurityEventListItem anchor, CancellationToken cancellationToken)
	{
		var user = await ResolveUserAsync(anchor, cancellationToken) ?? throw new InvalidOperationException("The selected event could not be resolved to a Depot user.");
		return await _sessionAdministration.TerminateUserSessionsAsync(user.Id, cancellationToken);
	}

	public async Task<User> DeactivateSelectedUserAsync(SecurityEventListItem anchor, CancellationToken cancellationToken)
	{
		var user = await ResolveUserAsync(anchor, cancellationToken) ?? throw new InvalidOperationException("The selected event could not be resolved to a Depot user.");
		return await _userService.SetActiveAsync(user.Id, false, user.Version, cancellationToken);
	}

	private bool CanResolveUser() => _authorization.HasAnyPermission(ApplicationPermission.UsersView, ApplicationPermission.UsersManage, ApplicationPermission.UserSessionsTerminate);
	private async Task<User?> ResolveUserAsync(SecurityEventListItem anchor, CancellationToken cancellationToken)
	{
		if (!CanResolveUser()) return null;
		if (anchor.UserId is { } userId) return await _users.GetByIdAsync(userId, cancellationToken);
		if (!string.IsNullOrWhiteSpace(anchor.AccountIdentifier)) return await _users.GetByEmailAsync(anchor.AccountIdentifier, cancellationToken);
		return null;
	}
}
