// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels.Users;

public sealed class UserListItemViewModel : BaseViewModel
{
	public UserListItemViewModel(User user)
	{
		Id = user.Id;
		Email = user.Email;
		DisplayName = user.DisplayName;
		RoleIds = user.Roles.Select(role => role.Id).ToArray();
		Roles = user.Roles.Count == 0 ? "No roles" : string.Join(", ", user.Roles.Select(role => role.Name));
		EffectivePermissions = user.EffectivePermissions.Count == 0 ? "No effective permissions" : string.Join(", ", user.EffectivePermissions.OrderBy(PermissionCatalog.Code).Select(PermissionCatalog.Code));
		Status = user.IsActive ? "Active" : "Inactive";
		CreatedUtc = user.CreatedUtc;
		IsActive = user.IsActive;
		Version = user.Version;
	}

	public long Id { get; }
	public string Email { get; }
	public string DisplayName { get; }
	public string Roles { get; }
	public IReadOnlyList<long> RoleIds { get; }
	public string EffectivePermissions { get; }
	public string Status { get; }
	public DateTime CreatedUtc { get; }
	public bool IsActive { get; }
	public long Version { get; }

}
