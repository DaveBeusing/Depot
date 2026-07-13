// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Repositories;

namespace Depot.Services;

public sealed class AuthenticationService
{
	private readonly UserRepository _userRepository;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuthorizationService _authorizationService;

	public AuthenticationService(
		UserRepository userRepository,
		PasswordHasher passwordHasher,
		AuthorizationService authorizationService)
	{
		_userRepository = userRepository;
		_passwordHasher = passwordHasher;
		_authorizationService = authorizationService;
	}

	public bool SignIn(string email, string password)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		var authentication = _userRepository.GetAuthenticationByEmail(normalizedEmail);

		if (authentication is null ||
			!authentication.User.IsActive ||
			!_passwordHasher.Verify(password, authentication.PasswordHash))
		{
			return false;
		}

		_authorizationService.SignIn(authentication.User);
		return true;
	}
}
