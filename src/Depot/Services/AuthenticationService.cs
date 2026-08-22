// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Repositories;

namespace Depot.Services;

public sealed class AuthenticationService
{
	private readonly UserRepository _userRepository;
	private readonly RoleRepository _roleRepository;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuthorizationService _authorizationService;
	private readonly LoginAttemptLimiter _attemptLimiter;

	public AuthenticationService(
		UserRepository userRepository,
		RoleRepository roleRepository,
		PasswordHasher passwordHasher,
		AuthorizationService authorizationService,
		LoginAttemptLimiter? attemptLimiter = null)
	{
		_userRepository = userRepository;
		_roleRepository = roleRepository;
		_passwordHasher = passwordHasher;
		_authorizationService = authorizationService;
		_attemptLimiter = attemptLimiter ?? new LoginAttemptLimiter();
	}

	public async Task<bool> SignInAsync(string email, string password, CancellationToken cancellationToken)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		if (_attemptLimiter.IsBlocked(normalizedEmail, out _)) return false;

		var authentication = await _userRepository.GetAuthenticationByEmailAsync(normalizedEmail, cancellationToken);
		var valid = authentication is not null && authentication.User.IsActive && _passwordHasher.Verify(password, authentication.PasswordHash);
		if (!valid)
		{
			_attemptLimiter.RecordFailure(normalizedEmail);
			return false;
		}

		_attemptLimiter.RecordSuccess(normalizedEmail);
		authentication!.User.Roles = await _roleRepository.GetUserRolesAsync(authentication.User.Id, cancellationToken);
		authentication.User.EffectivePermissions = await _roleRepository.GetEffectivePermissionsAsync(authentication.User.Id, cancellationToken);
		_authorizationService.SignIn(authentication.User, authentication.User.EffectivePermissions);
		return true;
	}
}
