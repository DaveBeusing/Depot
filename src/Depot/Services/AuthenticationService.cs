// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;
using Depot.Repositories;

namespace Depot.Services;

public sealed class AuthenticationService
{
	private readonly UserRepository _userRepository;
	private readonly RoleRepository _roleRepository;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuthorizationService _authorizationService;
	private readonly LoginAttemptLimiter _attemptLimiter;
	private SecurityEventService? _securityEvents;
	private SessionService? _sessionService;

	public AuthenticationService(
		UserRepository userRepository,
		RoleRepository roleRepository,
		PasswordHasher passwordHasher,
		AuthorizationService authorizationService,
		LoginAttemptLimiter? attemptLimiter = null,
		SecurityEventService? securityEvents = null)
	{
		_userRepository = userRepository;
		_roleRepository = roleRepository;
		_passwordHasher = passwordHasher;
		_authorizationService = authorizationService;
		_attemptLimiter = attemptLimiter ?? new LoginAttemptLimiter();
		_securityEvents = securityEvents;
	}

	internal void ConfigureSession(SessionService sessionService)
	{
		ArgumentNullException.ThrowIfNull(sessionService);
		if (_sessionService is not null) throw new InvalidOperationException("Authentication session integration is already configured.");
		_sessionService = sessionService;
	}

	internal void ConfigureSecurityEvents(SecurityEventService securityEvents)
	{
		ArgumentNullException.ThrowIfNull(securityEvents);
		if (_securityEvents is not null) throw new InvalidOperationException("Authentication security event integration is already configured.");
		_securityEvents = securityEvents;
	}

	public async Task<bool> SignInAsync(string email, string password, CancellationToken cancellationToken)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		if (_attemptLimiter.IsBlocked(normalizedEmail, out var retryAfter))
		{
			await TryRecordAsync(() => _securityEvents?.RecordBlockedAttemptAsync(normalizedEmail, retryAfter, cancellationToken));
			return false;
		}

		var authentication = await _userRepository.GetAuthenticationByEmailAsync(normalizedEmail, cancellationToken);
		var valid = authentication is not null && authentication.User.IsActive && _passwordHasher.Verify(password, authentication.PasswordHash);
		if (!valid)
		{
			var status = _attemptLimiter.RecordFailure(normalizedEmail);
			await TryRecordAsync(() => _securityEvents?.RecordAuthenticationFailureAsync(normalizedEmail, authentication?.User.Id, status, cancellationToken));
			return false;
		}

		var priorFailures = _attemptLimiter.GetFailureCount(normalizedEmail);
		_attemptLimiter.RecordSuccess(normalizedEmail);
		authentication!.User.Roles = await _roleRepository.GetUserRolesAsync(authentication.User.Id, cancellationToken);
		authentication.User.EffectivePermissions = await _roleRepository.GetEffectivePermissionsAsync(authentication.User.Id, cancellationToken);
		if (_sessionService is not null) await _sessionService.StartAuthenticatedSessionAsync(authentication.User.Id, cancellationToken);
		_authorizationService.SignIn(authentication.User, authentication.User.EffectivePermissions);
		await TryRecordAsync(() => _securityEvents?.RecordAuthenticationSuccessAsync(authentication.User, priorFailures, _sessionService?.CurrentSessionId, _sessionService?.CurrentMachineName, cancellationToken));
		return true;
	}

	private static async Task TryRecordAsync(Func<Task?> record)
	{
		try
		{
			var task = record();
			if (task is not null) await task.ConfigureAwait(false);
		}
		catch (Exception exception) { StartupDiagnostics.LogException(exception); }
	}
}
