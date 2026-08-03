// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Models;

namespace Depot.Data;

internal static class RbacCatalogSeeder
{
	public static void EnsureCatalog(DbCommand command)
	{
		foreach (var permission in PermissionCatalog.Definitions)
		{
			Execute(command,
				"INSERT INTO Permissions (Code, Name, Module, Action) SELECT $Code, $Name, $Module, $Action WHERE NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = $Code);",
				("$Code", permission.Code), ("$Name", permission.Name), ("$Module", permission.Module), ("$Action", permission.Action));
			Execute(command,
				"UPDATE Permissions SET Name = $Name, Module = $Module, Action = $Action WHERE Code = $Code;",
				("$Code", permission.Code), ("$Name", permission.Name), ("$Module", permission.Module), ("$Action", permission.Action));
		}

		foreach (var role in SystemRoleCatalog.Definitions)
		{
			Execute(command,
				"INSERT INTO Roles (Code, Name, Description, IsSystem, IsActive, Version) SELECT $Code, $Name, $Description, 1, 1, 1 WHERE NOT EXISTS (SELECT 1 FROM Roles WHERE Code = $Code);",
				("$Code", role.Code), ("$Name", role.Name), ("$Description", role.Description));
			Execute(command,
				"UPDATE Roles SET Name = $Name, Description = $Description, IsSystem = 1, IsActive = 1 WHERE Code = $Code;",
				("$Code", role.Code), ("$Name", role.Name), ("$Description", role.Description));
			Execute(command, "DELETE FROM RolePermissions WHERE RoleId = (SELECT Id FROM Roles WHERE Code = $Code);", ("$Code", role.Code));
			foreach (var permission in role.Permissions.OrderBy(PermissionCatalog.Code, StringComparer.Ordinal))
			{
				Execute(command,
					"INSERT INTO RolePermissions (RoleId, PermissionId) SELECT r.Id, p.Id FROM Roles r, Permissions p WHERE r.Code = $RoleCode AND p.Code = $PermissionCode;",
					("$RoleCode", role.Code), ("$PermissionCode", PermissionCatalog.Code(permission)));
			}
		}
	}

	public static void MigrateLegacyUsers(
		DbCommand command,
		bool hasAdministratorColumn = true,
		bool hasRoleColumn = true,
		bool hasApprovalColumn = true)
	{
		var administrator = JoinOr(
			hasAdministratorColumn ? "u.IsAdministrator = 1" : null,
			hasRoleColumn ? "u.Role = 1" : null);
		var approver = JoinOr(
			hasRoleColumn ? "u.Role = 3" : null,
			hasApprovalColumn ? "u.CanApprovePurchaseOrders = 1" : null);
		if (administrator is not null) Assign(command, SystemRoleCatalog.AdministratorCode, administrator);
		if (hasRoleColumn) Assign(command, SystemRoleCatalog.PurchasingCode, "u.Role = 2");
		if (approver is not null) Assign(command, SystemRoleCatalog.ApproverCode, approver);
		if (hasRoleColumn) Assign(command, SystemRoleCatalog.WarehouseOperatorCode, "u.Role = 4");
		Assign(command, SystemRoleCatalog.UserCode, "NOT EXISTS (SELECT 1 FROM UserRoles existing WHERE existing.UserId = u.Id)");
	}

	private static string? JoinOr(string? first, string? second) => (first, second) switch
	{
		(not null, not null) => $"{first} OR {second}",
		(not null, null) => first,
		(null, not null) => second,
		_ => null
	};

	public static void EnsureDefaultAdministratorAssignment(DbCommand command) =>
		Execute(command,
			"INSERT INTO UserRoles (UserId, RoleId) SELECT u.Id, r.Id FROM Users u, Roles r WHERE u.Email = 'admin@depot.local' AND r.Code = $RoleCode AND NOT EXISTS (SELECT 1 FROM UserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);",
			("$RoleCode", SystemRoleCatalog.AdministratorCode));

	private static void Assign(DbCommand command, string roleCode, string predicate) =>
		Execute(command,
			$"INSERT INTO UserRoles (UserId, RoleId) SELECT u.Id, r.Id FROM Users u, Roles r WHERE r.Code = $RoleCode AND ({predicate}) AND NOT EXISTS (SELECT 1 FROM UserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);",
			("$RoleCode", roleCode));

	private static void Execute(DbCommand command, string sql, params (string Name, object? Value)[] parameters)
	{
		command.CommandText = sql;
		command.Parameters.Clear();
		foreach (var (name, value) in parameters)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.Value = value ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}
		command.ExecuteNonQuery();
	}
}
