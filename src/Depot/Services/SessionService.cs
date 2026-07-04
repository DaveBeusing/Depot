// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

/// <summary>
/// Manages the current user session.
/// </summary>
public sealed class SessionService
{
	private readonly AuthorizationService _authorizationService;

	public SessionService(AuthorizationService authorizationService)
	{
		_authorizationService = authorizationService;
	}

    public bool LogoutRequestedByUser { get; private set; }

	public void Logout()
	{
        LogoutRequestedByUser = true;
		_authorizationService.SignOut();
	}

    public void Reset()
	{
		LogoutRequestedByUser = false;
	}
    
}