// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
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
		SalesSchemaMigration.Migrate(context.ConnectionFactory);
		UserSessionSchemaMigration.Migrate(context.ConnectionFactory);
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
		Assert.False(service.CanViewReports);

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

		context.Authorization.SignIn(user, [ApplicationPermission.DashboardView, ApplicationPermission.ReportsView]);
		Assert.True(service.CanViewReports);
		var reportingOnly = await service.GetAsync(CancellationToken.None);
		Assert.Null(reportingOnly.Roles.Administration);

		await SeedSessionMetricsAsync(context, user.Id);
		context.Authorization.SignIn(user, [ApplicationPermission.DashboardView, ApplicationPermission.UsersView]);
		var administrationOnly = await service.GetAsync(CancellationToken.None);
		var administrationMetrics = Assert.IsType<DashboardAdministrationMetrics>(administrationOnly.Roles.Administration);
		Assert.Equal(1, administrationMetrics.OnlineUsers);
		Assert.Equal(1, administrationMetrics.ActiveSessions);
		Assert.Equal(3, administrationMetrics.SessionsToday);
		Assert.Equal(1, administrationMetrics.AdministrativeLogoutsToday);
		Assert.Equal(1, administrationMetrics.RevokedSessionsToday);
		Assert.False(service.CanViewReports);

		context.Authorization.SignIn(user, PermissionCatalog.All);
		var administrator = await service.GetAsync(CancellationToken.None);
		Assert.NotNull(administrator.Inventory);
		Assert.NotNull(administrator.Roles.Approvals);
		Assert.NotNull(administrator.Roles.Purchasing);
		Assert.NotNull(administrator.Roles.Warehouse);
		Assert.NotNull(administrator.Roles.Administration);
		Assert.True(service.CanViewReports);
	}

	private static async Task SeedSessionMetricsAsync(ProcurementTestContext context, long userId)
	{
		var startedUtc = DateTime.Today.ToUniversalTime().AddMinutes(1);
		var activeSeenUtc = DateTime.UtcNow.AddMinutes(1);
		await InsertSessionAsync(context, userId, Guid.NewGuid(), Guid.NewGuid(), startedUtc, activeSeenUtc, null, null);
		await InsertSessionAsync(context, userId, Guid.NewGuid(), Guid.NewGuid(), startedUtc.AddMinutes(1), startedUtc.AddMinutes(2), startedUtc.AddMinutes(3), UserSessionEndReason.AdministrativeLogout);
		await InsertSessionAsync(context, userId, Guid.NewGuid(), Guid.NewGuid(), startedUtc.AddMinutes(4), startedUtc.AddMinutes(5), startedUtc.AddMinutes(6), UserSessionEndReason.Revoked);
	}

	private static Task<long> InsertSessionAsync(
		ProcurementTestContext context,
		long userId,
		Guid sessionId,
		Guid clientId,
		DateTime startedUtc,
		DateTime lastSeenUtc,
		DateTime? endedUtc,
		UserSessionEndReason? endReason) =>
		context.Data.InsertAsync(
			"INSERT INTO UserSessions (SessionId, UserId, StartedUtc, LastSeenUtc, LastActivityUtc, EndedUtc, EndReason, ClientInstanceId, MachineName, AppVersion, Version) VALUES ($SessionId, $UserId, $StartedUtc, $LastSeenUtc, NULL, $EndedUtc, $EndReason, $ClientInstanceId, 'dashboard-test', 'test', 1);",
			CancellationToken.None,
			new DatabaseParameter("$SessionId", sessionId.ToString("D")),
			new DatabaseParameter("$UserId", userId),
			new DatabaseParameter("$StartedUtc", startedUtc.ToUniversalTime().ToString("O")),
			new DatabaseParameter("$LastSeenUtc", lastSeenUtc.ToUniversalTime().ToString("O")),
			new DatabaseParameter("$EndedUtc", endedUtc?.ToUniversalTime().ToString("O")),
			new DatabaseParameter("$EndReason", endReason is null ? null : (int)endReason.Value),
			new DatabaseParameter("$ClientInstanceId", clientId.ToString("D")));
}
