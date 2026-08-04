// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public interface IAuthorizationService
{
	User? CurrentUser { get; }
	bool IsLoggedIn { get; }
	IReadOnlySet<ApplicationPermission> EffectivePermissions { get; }
	bool IsInRole(string roleCode);
	bool HasPermission(ApplicationPermission permission);
	bool HasAnyPermission(params ApplicationPermission[] permissions);
	void RequirePermission(ApplicationPermission permission);
}
