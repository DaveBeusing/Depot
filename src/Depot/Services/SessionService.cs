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

	public event EventHandler? LogoutRequested;

    public bool LogoutRequestedByUser { get; private set; }

	public void Logout()
	{
        LogoutRequestedByUser = true;
		_authorizationService.SignOut();
		LogoutRequested?.Invoke(this, EventArgs.Empty);
	}

    public void Reset()
	{
		LogoutRequestedByUser = false;
	}
    
}