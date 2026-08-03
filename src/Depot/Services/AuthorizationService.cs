// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public sealed class AuthorizationService : IAuthorizationService
{
	private IReadOnlySet<ApplicationPermission> _effectivePermissions = new HashSet<ApplicationPermission>();

	public User? CurrentUser { get; private set; }
	public bool IsLoggedIn => CurrentUser is not null;
	public IReadOnlySet<ApplicationPermission> EffectivePermissions => _effectivePermissions;

	public void SignIn(User user) => SignIn(user, user.EffectivePermissions);

	public void SignIn(User user, IEnumerable<ApplicationPermission> permissions)
	{
		CurrentUser = user;
		_effectivePermissions = user.IsActive
			? permissions.ToHashSet()
			: new HashSet<ApplicationPermission>();
		user.EffectivePermissions = _effectivePermissions;
	}

	public void SignOut()
	{
		CurrentUser = null;
		_effectivePermissions = new HashSet<ApplicationPermission>();
	}

	public bool HasPermission(ApplicationPermission permission) =>
		CurrentUser is { IsActive: true } && _effectivePermissions.Contains(permission);

	public bool HasAnyPermission(params ApplicationPermission[] permissions) => permissions.Any(HasPermission);

	public void RequirePermission(ApplicationPermission permission)
	{
		if (!HasPermission(permission))
			throw new UnauthorizedAccessException($"The current user does not have the '{PermissionCatalog.Code(permission)}' permission.");
	}

}
