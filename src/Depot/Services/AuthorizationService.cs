// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

/// <summary>
/// Provides authorization checks for the current user.
/// </summary>
public sealed class AuthorizationService
{
	public User? CurrentUser { get; private set; }

	public bool IsLoggedIn =>
		CurrentUser is not null;

	public void SignIn(
		User user)
	{
		CurrentUser = user;
	}

	public void SignOut()
	{
		CurrentUser = null;
	}

	public bool CanManageUsers() => CurrentUser?.IsAdministrator == true;

	public bool CanImport() => CurrentUser?.IsAdministrator == true;

	public bool CanManageMasterData() => CurrentUser?.IsAdministrator == true;

	public bool CanManageDatabase() => CurrentUser?.IsAdministrator == true;

	public bool CanOpenSettings() => CurrentUser?.IsAdministrator == true;
}