// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public interface IRoleEditorService
{
	IReadOnlyList<PermissionDefinition> Permissions { get; }
	Task<PageResult<Role>> SearchAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken);
	Task<IReadOnlyList<Role>> ListDesignerRolesAsync(CancellationToken cancellationToken);
	Task<IReadOnlyList<RoleEffectivePermissionUserProjection>> GetEffectivePermissionUsersAsync(long roleId, CancellationToken cancellationToken);
	Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken);
	Task<Role> SaveAsync(Role value, CancellationToken cancellationToken);
	Task<Role> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken);
}
