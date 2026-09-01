// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class AuthenticationService
{
	private readonly IAuthenticationProvider _authenticationProvider;
	private readonly RoleRepository _roleRepository;
	private readonly AuthorizationService _authorizationService;
	private readonly LoginAttemptLimiter? _attemptLimiter;
	private AuthenticationSecurityService? _authenticationSecurity;
	private SecurityEventService? _securityEvents;
	private SessionService? _sessionService;

	public AuthenticationService(UserRepository userRepository,RoleRepository roleRepository,PasswordHasher passwordHasher,AuthorizationService authorizationService,LoginAttemptLimiter? attemptLimiter=null,SecurityEventService? securityEvents=null)
	{_authenticationProvider=new LocalAuthenticationProvider(userRepository,passwordHasher);_roleRepository=roleRepository;_authorizationService=authorizationService;_attemptLimiter=attemptLimiter??new LoginAttemptLimiter();_securityEvents=securityEvents;}
	public AuthenticationService(IAuthenticationProvider authenticationProvider,RoleRepository roleRepository,AuthorizationService authorizationService,AuthenticationSecurityService authenticationSecurity,SecurityEventService securityEvents)
	{_authenticationProvider=authenticationProvider;_roleRepository=roleRepository;_authorizationService=authorizationService;_authenticationSecurity=authenticationSecurity;_securityEvents=securityEvents;}

	internal void ConfigureSession(SessionService sessionService){ArgumentNullException.ThrowIfNull(sessionService);if(_sessionService is not null)throw new InvalidOperationException("Authentication session integration is already configured.");_sessionService=sessionService;}
	internal void ConfigureSecurityEvents(SecurityEventService securityEvents){ArgumentNullException.ThrowIfNull(securityEvents);if(_securityEvents is not null)throw new InvalidOperationException("Authentication security event integration is already configured.");_securityEvents=securityEvents;}
	internal void ConfigureAuthenticationSecurity(AuthenticationSecurityService authenticationSecurity){ArgumentNullException.ThrowIfNull(authenticationSecurity);if(_authenticationSecurity is not null)throw new InvalidOperationException("Authentication security policy integration is already configured.");_authenticationSecurity=authenticationSecurity;}

	public async Task<bool> SignInAsync(string email,string password,CancellationToken cancellationToken)
	{
		var normalizedEmail=email.Trim().ToLowerInvariant();var blocked=await GetBlockStatusAsync(normalizedEmail,cancellationToken);if(blocked.IsBlocked){await TryRecordAsync(()=>_securityEvents?.RecordBlockedAttemptAsync(normalizedEmail,blocked.RetryAfter,cancellationToken));return false;}
		var user=await _authenticationProvider.AuthenticateAsync(normalizedEmail,password,cancellationToken);if(user is null){var status=await RecordFailureAsync(normalizedEmail,cancellationToken);await TryRecordAsync(()=>_securityEvents?.RecordAuthenticationFailureAsync(normalizedEmail,null,status,cancellationToken));return false;}
		var priorFailures=await RecordSuccessAsync(normalizedEmail,cancellationToken);user.Roles=await _roleRepository.GetUserRolesAsync(user.Id,cancellationToken);user.EffectivePermissions=await _roleRepository.GetEffectivePermissionsAsync(user.Id,cancellationToken);
		try{if(_sessionService is not null)await _sessionService.StartAuthenticatedSessionAsync(user.Id,cancellationToken);}catch(SessionLimitExceededException){return false;}
		_authorizationService.SignIn(user,user.EffectivePermissions);await TryRecordAsync(()=>_securityEvents?.RecordAuthenticationSuccessAsync(user,priorFailures,_sessionService?.CurrentSessionId,_sessionService?.CurrentClientInstanceId,_sessionService?.CurrentMachineName,cancellationToken));return true;
	}

	private async Task<AuthenticationThrottleSnapshot> GetBlockStatusAsync(string accountKey,CancellationToken cancellationToken){if(_authenticationSecurity is not null)return await _authenticationSecurity.GetStatusAsync(accountKey,cancellationToken);if(_attemptLimiter is not null&&_attemptLimiter.IsBlocked(accountKey,out var retryAfter))return new AuthenticationThrottleSnapshot(_attemptLimiter.GetFailureCount(accountKey),true,retryAfter);return new AuthenticationThrottleSnapshot(_attemptLimiter?.GetFailureCount(accountKey)??0,false,TimeSpan.Zero);}
	private async Task<LoginAttemptStatus> RecordFailureAsync(string accountKey,CancellationToken cancellationToken){if(_authenticationSecurity is not null)return await _authenticationSecurity.RecordFailureAsync(accountKey,cancellationToken);return _attemptLimiter?.RecordFailure(accountKey)??new LoginAttemptStatus(1,false,TimeSpan.Zero);}
	private async Task<int> RecordSuccessAsync(string accountKey,CancellationToken cancellationToken){if(_authenticationSecurity is not null)return await _authenticationSecurity.RecordSuccessAsync(accountKey,cancellationToken);var failures=_attemptLimiter?.GetFailureCount(accountKey)??0;_attemptLimiter?.RecordSuccess(accountKey);return failures;}
	private static async Task TryRecordAsync(Func<Task?> record){try{var task=record();if(task is not null)await task.ConfigureAwait(false);}catch(Exception exception){StartupDiagnostics.LogException(exception);}}
}

public sealed class SessionLimitExceededException:InvalidOperationException{public SessionLimitExceededException():base("The active session limit for this account has been reached.") {}}
