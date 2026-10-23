// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public sealed partial class DatabaseManagementService
{
	private readonly IAuthorizationService _authorization;

	public bool CanView => _authorization.HasAnyPermission(ApplicationPermission.DatabaseView, ApplicationPermission.DatabaseManage);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.DatabaseManage);

	public DatabaseConnectionSettings SaveSettings(DatabaseConnectionSettings settings)
	{
		RequireManage();
		ArgumentNullException.ThrowIfNull(settings);
		return _settingsService.Save(settings);
	}

	private void RequireView()
	{
		if (!CanView) _authorization.RequirePermission(ApplicationPermission.DatabaseView);
	}

	private void RequireManage() => _authorization.RequirePermission(ApplicationPermission.DatabaseManage);
}
