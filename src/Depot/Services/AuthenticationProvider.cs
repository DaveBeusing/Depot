// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public interface IAuthenticationProvider
{
	Task<User?> AuthenticateAsync(string normalizedAccount, string credential, CancellationToken cancellationToken);
}

public sealed class LocalAuthenticationProvider : IAuthenticationProvider
{
	private readonly UserRepository _users;
	private readonly PasswordHasher _passwordHasher;

	public LocalAuthenticationProvider(UserRepository users, PasswordHasher passwordHasher)
	{
		_users = users;
		_passwordHasher = passwordHasher;
	}

	public async Task<User?> AuthenticateAsync(string normalizedAccount, string credential, CancellationToken cancellationToken)
	{
		var authentication = await _users.GetAuthenticationByEmailAsync(normalizedAccount, cancellationToken);
		if (authentication is null || !authentication.User.IsActive) return null;
		return _passwordHasher.Verify(credential, authentication.PasswordHash) ? authentication.User : null;
	}
}
