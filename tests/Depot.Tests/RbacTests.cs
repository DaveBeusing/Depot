// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class RbacTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-rbac-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;
	private readonly DatabaseAccess _data;

	public RbacTests()
	{
		_factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(_factory).Initialize();
		_data = new DatabaseAccess(_factory);
	}

	[Fact]
	public async Task EffectivePermissionsAreTheUnionOfAllActiveRoles()
	{
		var userId = await CreateUserAsync("union@depot.test");
		await AssignSystemRoleAsync(userId, SystemRoleCatalog.PurchasingCode);
		await AssignSystemRoleAsync(userId, SystemRoleCatalog.ApproverCode);
		var permissions = await new RoleRepository(_data).GetEffectivePermissionsAsync(userId, CancellationToken.None);
		Assert.Contains(ApplicationPermission.PurchaseOrdersCreate, permissions);
		Assert.Contains(ApplicationPermission.PurchaseOrdersApprove, permissions);
		Assert.DoesNotContain(ApplicationPermission.MaterialIssuesPost, permissions);
	}

	[Fact]
	public void SessionSwitchReplacesAndLogoutClearsPermissionCache()
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, IsActive = true }, [ApplicationPermission.PurchaseOrdersApprove]);
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove));
		authorization.SignIn(new User { Id = 2, IsActive = true }, [ApplicationPermission.MaterialIssuesPost]);
		Assert.False(authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove));
		Assert.True(authorization.HasPermission(ApplicationPermission.MaterialIssuesPost));
		new SessionService(authorization).Logout();
		Assert.False(authorization.IsLoggedIn);
		Assert.Empty(authorization.EffectivePermissions);
	}

	[Fact]
	public void InactiveUsersReceiveNoEffectivePermissions()
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, IsActive = false }, PermissionCatalog.All);
		Assert.False(authorization.HasPermission(ApplicationPermission.InventoryView));
		Assert.False(authorization.HasAnyPermission(ApplicationPermission.InventoryView, ApplicationPermission.ItemsView));
		Assert.Throws<UnauthorizedAccessException>(() => authorization.RequirePermission(ApplicationPermission.InventoryView));
	}

	[Fact]
	public async Task ServicesRejectUsersWithoutTheRequiredPermission()
	{
		var roles = new RoleRepository(_data);
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 99, IsActive = true }, [ApplicationPermission.RolesView]);
		var auditRepository = new AuditRepository(_data);
		var service = new RoleService(new DatabaseTransactionRunner(_data), roles, auditRepository, new AuditService(auditRepository, authorization), authorization);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(
			new Role { Code = "DENIED", Name = "Denied", IsActive = true, Permissions = [ApplicationPermission.InventoryView] },
			CancellationToken.None));
	}

	[Fact]
	public async Task AdministratorRoleReceivesEveryCatalogPermissionFromDatabase()
	{
		var administrator = await new UserRepository(_data).GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException();
		var permissions = await new RoleRepository(_data).GetEffectivePermissionsAsync(administrator.Id, CancellationToken.None);
		Assert.Equal(PermissionCatalog.All.Count, permissions.Count);
		Assert.All(PermissionCatalog.All, permission => Assert.Contains(permission, permissions));
	}

	[Fact]
	public async Task CurrentSchemaRefreshesSystemRolePermissionsFromTheCatalog()
	{
		await _data.ExecuteAsync(
			"DELETE FROM RolePermissions WHERE PermissionId IN (SELECT Id FROM Permissions WHERE Code = $Code);",
			CancellationToken.None,
			new DatabaseParameter("$Code", PermissionCatalog.Code(ApplicationPermission.PurchasingView)));

		new DepotDatabase(_factory).Initialize();

		var purchasing = await new RoleRepository(_data).GetByCodeAsync(SystemRoleCatalog.PurchasingCode, CancellationToken.None)
			?? throw new InvalidOperationException();
		var permissions = await new RoleRepository(_data).GetEffectivePermissionsAsync(
			await CreateUserWithRoleAsync(purchasing.Id),
			CancellationToken.None);
		Assert.Contains(ApplicationPermission.PurchasingView, permissions);
	}

	[Fact]
	public async Task SystemRolesAreProtectedAndCustomRoleChangesAreAuditedWithConcurrency()
	{
		var (roles, service, _) = await CreateRoleServiceAsync();
		var administrator = await roles.GetByCodeAsync(SystemRoleCatalog.AdministratorCode, CancellationToken.None) ?? throw new InvalidOperationException();
		administrator.Permissions = PermissionCatalog.All.ToArray();
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(administrator, CancellationToken.None));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetActiveAsync(administrator.Id, administrator.Version, false, CancellationToken.None));

		var created = await service.SaveAsync(new Role { Code = "QUALITY_REVIEW", Name = "Quality Review", IsActive = true, Permissions = [ApplicationPermission.InventoryView, ApplicationPermission.InventoryCountsView] }, CancellationToken.None);
		var stale = Copy(created);
		created.Description = "Updated description";
		var updated = await service.SaveAsync(created, CancellationToken.None);
		Assert.Equal(2, updated.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.SaveAsync(stale, CancellationToken.None));
		Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'Role' AND EntityId = $Id;", new DatabaseParameter("$Id", created.Id)));
	}

	[Fact]
	public async Task UserCanReceiveMultipleRolesAndEffectiveRightsWithoutUpdatingLegacyFields()
	{
		var (roleRepository, _, authorization) = await CreateRoleServiceAsync();
		var auditRepository = new AuditRepository(_data);
		var userService = new UserService(new DatabaseTransactionRunner(_data), new UserRepository(_data), roleRepository, auditRepository, new PasswordHasher(), authorization, new AuditService(auditRepository, authorization));
		var purchasing = await roleRepository.GetByCodeAsync(SystemRoleCatalog.PurchasingCode, CancellationToken.None) ?? throw new InvalidOperationException();
		var approver = await roleRepository.GetByCodeAsync(SystemRoleCatalog.ApproverCode, CancellationToken.None) ?? throw new InvalidOperationException();
		var user = await userService.CreateUserAsync("multi-role@depot.test", "Multi Role", "DepotMultiRole123!", [purchasing.Id, approver.Id], CancellationToken.None);
		Assert.Equal(2, user.Roles.Count);
		Assert.Contains(ApplicationPermission.PurchaseOrdersCreate, user.EffectivePermissions);
		Assert.Contains(ApplicationPermission.PurchaseOrdersApprove, user.EffectivePermissions);
		Assert.False(user.IsAdministrator);
		Assert.False(user.CanApprovePurchaseOrders);
		Assert.Equal(UserRole.User, user.Role);
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'User' AND EntityId = $Id;", new DatabaseParameter("$Id", user.Id)));
	}

	[Fact]
	public async Task Version27MigrationMapsEveryLegacyRoleWithoutRightsLoss()
	{
		await _data.ExecuteAsync("DELETE FROM UserRoles;", CancellationToken.None);
		await InsertLegacyUserAsync("legacy-admin@depot.test", true, false, UserRole.User);
		await InsertLegacyUserAsync("legacy-purchasing@depot.test", false, true, UserRole.Purchasing);
		await InsertLegacyUserAsync("legacy-approver@depot.test", false, true, UserRole.User);
		await InsertLegacyUserAsync("legacy-warehouse@depot.test", false, false, UserRole.WarehouseOperator);
		await InsertLegacyUserAsync("legacy-user@depot.test", false, false, UserRole.User);
		await _data.ExecuteAsync("UPDATE DatabaseInfo SET Version = 27;", CancellationToken.None);
		new DepotDatabase(_factory).Initialize();

		Assert.Equal([SystemRoleCatalog.AdministratorCode], await RoleCodesAsync("legacy-admin@depot.test"));
		Assert.Equal([SystemRoleCatalog.ApproverCode, SystemRoleCatalog.PurchasingCode], await RoleCodesAsync("legacy-purchasing@depot.test"));
		Assert.Equal([SystemRoleCatalog.ApproverCode], await RoleCodesAsync("legacy-approver@depot.test"));
		Assert.Equal([SystemRoleCatalog.WarehouseOperatorCode], await RoleCodesAsync("legacy-warehouse@depot.test"));
		Assert.Equal([SystemRoleCatalog.UserCode], await RoleCodesAsync("legacy-user@depot.test"));
		Assert.Equal(DatabaseVersion.CurrentVersion, await ScalarAsync("SELECT Version FROM DatabaseInfo;"));
	}

	private async Task<(RoleRepository Roles, RoleService Service, AuthorizationService Authorization)> CreateRoleServiceAsync()
	{
		var roles = new RoleRepository(_data);
		var users = new UserRepository(_data);
		var administrator = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException();
		administrator.Roles = await roles.GetUserRolesAsync(administrator.Id, CancellationToken.None);
		administrator.EffectivePermissions = await roles.GetEffectivePermissionsAsync(administrator.Id, CancellationToken.None);
		var authorization = new AuthorizationService();
		authorization.SignIn(administrator, administrator.EffectivePermissions);
		var auditRepository = new AuditRepository(_data);
		var audit = new AuditService(auditRepository, authorization);
		return (roles, new RoleService(new DatabaseTransactionRunner(_data), roles, auditRepository, audit, authorization), authorization);
	}

	private Task<long> CreateUserAsync(string email) => _data.InsertAsync("INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc) VALUES ($Email, 'RBAC Test', 'unused', 0, 0, 0, 1, '2026-01-01T00:00:00Z');", CancellationToken.None, new DatabaseParameter("$Email", email));
	private async Task<long> CreateUserWithRoleAsync(long roleId)
	{
		var userId = await CreateUserAsync($"catalog-{Guid.NewGuid():N}@depot.test");
		await _data.ExecuteAsync("INSERT INTO UserRoles (UserId, RoleId) VALUES ($UserId, $RoleId);", CancellationToken.None, new DatabaseParameter("$UserId", userId), new DatabaseParameter("$RoleId", roleId));
		return userId;
	}
	private async Task AssignSystemRoleAsync(long userId, string code) => await _data.ExecuteAsync("INSERT INTO UserRoles (UserId, RoleId) SELECT $UserId, Id FROM Roles WHERE Code = $Code;", CancellationToken.None, new DatabaseParameter("$UserId", userId), new DatabaseParameter("$Code", code));
	private async Task InsertLegacyUserAsync(string email, bool administrator, bool approver, UserRole role) => await _data.ExecuteAsync("INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc) VALUES ($Email, 'Legacy', 'unused', $Administrator, $Approver, $Role, 1, '2026-01-01T00:00:00Z');", CancellationToken.None, new DatabaseParameter("$Email", email), new DatabaseParameter("$Administrator", administrator), new DatabaseParameter("$Approver", approver), new DatabaseParameter("$Role", (int)role));
	private Task<IReadOnlyList<string>> RoleCodesAsync(string email) => _data.QueryAsync("SELECT r.Code FROM Roles r INNER JOIN UserRoles ur ON ur.RoleId = r.Id INNER JOIN Users u ON u.Id = ur.UserId WHERE u.Email = $Email ORDER BY r.Code;", reader => reader.GetString(0), CancellationToken.None, new DatabaseParameter("$Email", email));
	private async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters) => Convert.ToInt64(await _data.ExecuteScalarAsync(sql, CancellationToken.None, parameters));
	private static Role Copy(Role role) => new() { Id = role.Id, Code = role.Code, Name = role.Name, Description = role.Description, IsSystem = role.IsSystem, IsActive = role.IsActive, Version = role.Version, Permissions = role.Permissions.ToArray() };

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
