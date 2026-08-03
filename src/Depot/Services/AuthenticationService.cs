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

	public AuthenticationService(
		UserRepository userRepository,
		RoleRepository roleRepository,
		PasswordHasher passwordHasher,
		AuthorizationService authorizationService)
	{
		_userRepository = userRepository;
		_roleRepository = roleRepository;
		_passwordHasher = passwordHasher;
		_authorizationService = authorizationService;
	}

	public async Task<bool> SignInAsync(
		string email,
		string password,
		CancellationToken cancellationToken)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		var authentication = await _userRepository.GetAuthenticationByEmailAsync(
			normalizedEmail,
			cancellationToken);
		if (authentication is null ||
			!authentication.User.IsActive ||
			!_passwordHasher.Verify(password, authentication.PasswordHash))
		{
			return false;
		}
		authentication.User.Roles = await _roleRepository.GetUserRolesAsync(authentication.User.Id, cancellationToken);
		authentication.User.EffectivePermissions = await _roleRepository.GetEffectivePermissionsAsync(authentication.User.Id, cancellationToken);
		_authorizationService.SignIn(authentication.User, authentication.User.EffectivePermissions);
		return true;
	}
}
