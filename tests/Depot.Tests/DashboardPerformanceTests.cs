// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class DashboardPerformanceTests
{
	[Fact]
	public async Task DashboardLoadsOnlyAggregatesAllowedByEffectivePermissions()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		context.SignInAdministrator();
		var user = context.Authorization.CurrentUser ?? throw new InvalidOperationException("The test user is not signed in.");
		var service = new DashboardService(
			new StockService(new InventoryRepository(context.Data), new StockMovementRepository(context.Data)),
			new DashboardRepository(context.Data),
			context.Authorization);

		context.Authorization.SignIn(user, [ApplicationPermission.DashboardView, ApplicationPermission.PurchaseOrdersApprove]);
		var approver = await service.GetAsync(CancellationToken.None);
		Assert.Null(approver.Inventory);
		Assert.NotNull(approver.Roles.Approvals);
		Assert.Null(approver.Roles.Purchasing);
		Assert.Null(approver.Roles.Warehouse);
		Assert.Null(approver.Roles.Administration);

		context.Authorization.SignIn(user, [ApplicationPermission.DashboardView, ApplicationPermission.PurchaseOrdersView]);
		var purchasing = await service.GetAsync(CancellationToken.None);
		Assert.NotNull(purchasing.Roles.Purchasing);
		Assert.Null(purchasing.Roles.Approvals);

		context.Authorization.SignIn(user, [ApplicationPermission.DashboardView, ApplicationPermission.InventoryCountsView]);
		var warehouse = await service.GetAsync(CancellationToken.None);
		Assert.NotNull(warehouse.Roles.Warehouse);
		Assert.Null(warehouse.Roles.Purchasing);

		context.Authorization.SignIn(user, [ApplicationPermission.DashboardView, ApplicationPermission.InventoryView]);
		var standardUser = await service.GetAsync(CancellationToken.None);
		Assert.NotNull(standardUser.Inventory);
		Assert.Null(standardUser.Roles.Approvals);
		Assert.Null(standardUser.Roles.Purchasing);
		Assert.Null(standardUser.Roles.Warehouse);

		context.Authorization.SignIn(user, PermissionCatalog.All);
		var administrator = await service.GetAsync(CancellationToken.None);
		Assert.NotNull(administrator.Inventory);
		Assert.NotNull(administrator.Roles.Approvals);
		Assert.NotNull(administrator.Roles.Purchasing);
		Assert.NotNull(administrator.Roles.Warehouse);
		Assert.NotNull(administrator.Roles.Administration);
	}
}
