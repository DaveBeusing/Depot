// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class AuthorizationServiceTests
{
	[Fact]
	public void AdministratorHasEveryDefinedPermission()
	{
		var authorization = SignIn(UserRole.Administrator);
		foreach (var permission in Enum.GetValues<ApplicationPermission>()) Assert.True(authorization.HasPermission(permission));
	}

	[Fact]
	public void PurchasingHasOnlyPurchaseOrderExecutionPermissions()
	{
		var authorization = SignIn(UserRole.Purchasing);
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersCreate));
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersEdit));
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersSubmit));
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersOrder));
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersClose));
		Assert.False(authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove));
		Assert.False(authorization.HasPermission(ApplicationPermission.MaterialIssuesPost));
	}

	[Fact]
	public void ApproverCanOnlyApprovePurchaseOrders()
	{
		var authorization = SignIn(UserRole.Approver);
		Assert.True(authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove));
		Assert.False(authorization.HasPermission(ApplicationPermission.PurchaseOrdersCreate));
		Assert.False(authorization.HasPermission(ApplicationPermission.PurchaseOrdersOrder));
	}

	[Fact]
	public void WarehouseOperatorHasMaterialAndSupplierReturnPermissionsOnly()
	{
		var authorization = SignIn(UserRole.WarehouseOperator);
		Assert.True(authorization.HasPermission(ApplicationPermission.MaterialIssuesCreate));
		Assert.True(authorization.HasPermission(ApplicationPermission.MaterialIssuesPost));
		Assert.True(authorization.HasPermission(ApplicationPermission.MaterialIssuesReverse));
		Assert.True(authorization.HasPermission(ApplicationPermission.MaterialReturnsCreate));
		Assert.True(authorization.HasPermission(ApplicationPermission.MaterialReturnsPost));
		Assert.True(authorization.HasPermission(ApplicationPermission.SupplierReturnsCreate));
		Assert.True(authorization.HasPermission(ApplicationPermission.SupplierReturnsPost));
		Assert.False(authorization.HasPermission(ApplicationPermission.PurchaseOrdersCreate));
	}

	[Fact]
	public void InactiveAndNormalUsersHaveNoWorkflowPermissions()
	{
		var normal = SignIn(UserRole.User);
		Assert.All(Enum.GetValues<ApplicationPermission>(), permission => Assert.False(normal.HasPermission(permission)));
		var inactive = new AuthorizationService();
		inactive.SignIn(new User { Id = 2, Role = UserRole.Administrator, IsAdministrator = true, IsActive = false });
		Assert.All(Enum.GetValues<ApplicationPermission>(), permission => Assert.False(inactive.HasPermission(permission)));
	}

	[Fact]
	public async Task ServicesRejectUnauthorizedWorkflowChangesBeforeDatabaseMutation()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		context.Authorization.SignIn(new User { Id = 900001, Role = UserRole.Approver, IsActive = true });
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Orders.SaveDraftAsync(context.NewOrder()));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.MaterialIssues.SaveDraftAsync(new MaterialIssue()));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.MaterialReturns.SaveDraftAsync(new MaterialReturn()));
		context.Authorization.SignIn(new User { Id = 900002, Role = UserRole.Purchasing, IsActive = true });
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Orders.ApproveAsync(1, 1));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.SupplierReturns.PostAsync(1, 1));
	}

	private static AuthorizationService SignIn(UserRole role)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, Role = role, IsAdministrator = role == UserRole.Administrator, CanApprovePurchaseOrders = role == UserRole.Approver, IsActive = true });
		return authorization;
	}
}
