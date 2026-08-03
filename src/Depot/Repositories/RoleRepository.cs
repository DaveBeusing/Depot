// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class RoleRepository : DatabaseRepository
{
	private const string Columns = "r.Id, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive, r.Version";

	public RoleRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<PageResult<Role>> SearchPageAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var where = string.IsNullOrWhiteSpace(search) ? string.Empty : "WHERE r.Code LIKE $Search OR r.Name LIKE $Search";
		var parameters = string.IsNullOrWhiteSpace(search) ? [] : new[] { Parameter("$Search", $"%{search}%") };
		return Database.QueryPageAsync(
			$"SELECT {Columns} FROM Roles r {where} ORDER BY r.IsSystem DESC, r.Name, r.Id",
			$"SELECT COUNT(*) FROM Roles r {where};",
			ReadRole,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<Role>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync($"SELECT {Columns} FROM Roles r WHERE r.IsActive = 1 ORDER BY r.IsSystem DESC, r.Name, r.Id;", ReadRole, cancellationToken);

	public async Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var role = await Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM Roles r WHERE r.Id = $Id;",
			ReadRole,
			cancellationToken,
			Parameter("$Id", id));
		if (role is null) return null;
		role.Permissions = await GetRolePermissionsAsync(id, cancellationToken);
		return role;
	}

	public Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} FROM Roles r WHERE r.Code = $Code;",
			ReadRole,
			cancellationToken,
			Parameter("$Code", code));

	public Task<IReadOnlyList<Role>> GetUserRolesAsync(long userId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {Columns} FROM Roles r INNER JOIN UserRoles ur ON ur.RoleId = r.Id WHERE ur.UserId = $UserId ORDER BY r.Name, r.Id;",
			ReadRole,
			cancellationToken,
			Parameter("$UserId", userId));

	public async Task<IReadOnlyDictionary<long, IReadOnlyList<Role>>> GetUserRolesAsync(IEnumerable<long> userIds, CancellationToken cancellationToken)
	{
		var values = userIds.Distinct().OrderBy(id => id).ToArray();
		if (values.Length == 0) return new Dictionary<long, IReadOnlyList<Role>>();
		var (placeholders, parameters) = IdParameters(values);
		var rows = await Database.QueryAsync(
			$"SELECT ur.UserId, {Columns} FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId IN ({placeholders}) ORDER BY ur.UserId, r.Name, r.Id;",
			reader => new UserRoleRow(reader.GetInt64(0), ReadRole(reader, 1)),
			cancellationToken,
			parameters);
		return rows.GroupBy(row => row.UserId).ToDictionary(group => group.Key, group => (IReadOnlyList<Role>)group.Select(row => row.Role).ToArray());
	}

	public async Task<IReadOnlySet<ApplicationPermission>> GetEffectivePermissionsAsync(long userId, CancellationToken cancellationToken)
	{
		var codes = await Database.QueryAsync(
			"SELECT DISTINCT p.Code FROM Permissions p INNER JOIN RolePermissions rp ON rp.PermissionId = p.Id INNER JOIN Roles r ON r.Id = rp.RoleId INNER JOIN UserRoles ur ON ur.RoleId = r.Id WHERE ur.UserId = $UserId AND r.IsActive = 1 ORDER BY p.Code;",
			reader => reader.GetString(0),
			cancellationToken,
			Parameter("$UserId", userId));
		return codes.Select(code => PermissionCatalog.TryParse(code, out var permission) ? permission : (ApplicationPermission?)null)
			.Where(permission => permission.HasValue)
			.Select(permission => permission.GetValueOrDefault())
			.ToHashSet();
	}

	public async Task<IReadOnlyDictionary<long, IReadOnlySet<ApplicationPermission>>> GetEffectivePermissionsAsync(IEnumerable<long> userIds, CancellationToken cancellationToken)
	{
		var values = userIds.Distinct().OrderBy(id => id).ToArray();
		if (values.Length == 0) return new Dictionary<long, IReadOnlySet<ApplicationPermission>>();
		var (placeholders, parameters) = IdParameters(values);
		var rows = await Database.QueryAsync(
			$"SELECT DISTINCT ur.UserId, p.Code FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId INNER JOIN RolePermissions rp ON rp.RoleId = r.Id INNER JOIN Permissions p ON p.Id = rp.PermissionId WHERE ur.UserId IN ({placeholders}) AND r.IsActive = 1 ORDER BY ur.UserId, p.Code;",
			reader => new UserPermissionRow(reader.GetInt64(0), reader.GetString(1)),
			cancellationToken,
			parameters);
		return rows.GroupBy(row => row.UserId).ToDictionary(
			group => group.Key,
			group => (IReadOnlySet<ApplicationPermission>)group.Select(row => PermissionCatalog.TryParse(row.Code, out var permission) ? permission : (ApplicationPermission?)null).Where(permission => permission.HasValue).Select(permission => permission.GetValueOrDefault()).ToHashSet());
	}

	public async Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
	{
		var values = ids.Distinct().OrderBy(id => id).ToArray();
		if (values.Length == 0) return [];
		var parameters = values.Select((id, index) => Parameter($"$Id{index}", id)).ToArray();
		var placeholders = string.Join(", ", parameters.Select(parameter => parameter.Name));
		return await Database.QueryAsync(
			$"SELECT {Columns} FROM Roles r WHERE r.Id IN ({placeholders}) ORDER BY r.Name, r.Id;",
			ReadRole,
			cancellationToken,
			parameters);
	}

	public static Task<long> CreateAsync(DatabaseTransactionContext transaction, Role role, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO Roles (Code, Name, Description, IsSystem, IsActive, Version) VALUES ($Code, $Name, $Description, $IsSystem, $IsActive, 1);",
			cancellationToken,
			Parameters(role));

	public static async Task<bool> UpdateAsync(DatabaseTransactionContext transaction, Role role, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE Roles SET Name = $Name, Description = $Description, IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameters(role)) == 1;

	public static Task ReplacePermissionsAsync(DatabaseTransactionContext transaction, long roleId, IEnumerable<ApplicationPermission> permissions, CancellationToken cancellationToken) =>
		ReplacePermissionsCoreAsync(transaction, roleId, permissions, cancellationToken);

	public static async Task ReplaceUserRolesAsync(DatabaseTransactionContext transaction, long userId, IEnumerable<long> roleIds, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("DELETE FROM UserRoles WHERE UserId = $UserId;", cancellationToken, Parameter("$UserId", userId));
		foreach (var roleId in roleIds.Distinct().OrderBy(id => id))
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO UserRoles (UserId, RoleId) VALUES ($UserId, $RoleId);",
				cancellationToken,
				Parameter("$UserId", userId),
				Parameter("$RoleId", roleId));
		}
	}

	private async Task<IReadOnlyList<ApplicationPermission>> GetRolePermissionsAsync(long roleId, CancellationToken cancellationToken)
	{
		var codes = await Database.QueryAsync(
			"SELECT p.Code FROM Permissions p INNER JOIN RolePermissions rp ON rp.PermissionId = p.Id WHERE rp.RoleId = $RoleId ORDER BY p.Code;",
			reader => reader.GetString(0),
			cancellationToken,
			Parameter("$RoleId", roleId));
		return codes.Select(code => PermissionCatalog.TryParse(code, out var permission) ? permission : (ApplicationPermission?)null)
			.Where(permission => permission.HasValue)
			.Select(permission => permission.GetValueOrDefault())
			.ToArray();
	}

	private static async Task ReplacePermissionsCoreAsync(DatabaseTransactionContext transaction, long roleId, IEnumerable<ApplicationPermission> permissions, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("DELETE FROM RolePermissions WHERE RoleId = $RoleId;", cancellationToken, Parameter("$RoleId", roleId));
		foreach (var permission in permissions.Distinct().OrderBy(PermissionCatalog.Code, StringComparer.Ordinal))
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO RolePermissions (RoleId, PermissionId) SELECT $RoleId, Id FROM Permissions WHERE Code = $Code;",
				cancellationToken,
				Parameter("$RoleId", roleId),
				Parameter("$Code", PermissionCatalog.Code(permission)));
		}
	}

	private static Role ReadRole(DbDataReader reader) => ReadRole(reader, 0);

	private static Role ReadRole(DbDataReader reader, int offset) => new()
	{
		Id = reader.GetInt64(offset),
		Code = reader.GetString(offset + 1),
		Name = reader.GetString(offset + 2),
		Description = reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
		IsSystem = reader.GetBoolean(offset + 4),
		IsActive = reader.GetBoolean(offset + 5),
		Version = reader.GetInt64(offset + 6)
	};

	private static (string Placeholders, DatabaseParameter[] Parameters) IdParameters(IReadOnlyList<long> values)
	{
		var parameters = values.Select((id, index) => Parameter($"$Id{index}", id)).ToArray();
		return (string.Join(", ", parameters.Select(parameter => parameter.Name)), parameters);
	}

	private sealed record UserRoleRow(long UserId, Role Role);
	private sealed record UserPermissionRow(long UserId, string Code);

	private static DatabaseParameter[] Parameters(Role role) =>
	[
		Parameter("$Id", role.Id),
		Parameter("$Code", role.Code),
		Parameter("$Name", role.Name),
		Parameter("$Description", role.Description),
		Parameter("$IsSystem", role.IsSystem),
		Parameter("$IsActive", role.IsActive),
		Parameter("$Version", role.Version)
	];
}
