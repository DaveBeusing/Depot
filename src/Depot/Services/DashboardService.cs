// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class DashboardService
{
	private readonly StockService _stock;
	private readonly DashboardRepository _dashboard;
	private readonly IAuthorizationService _authorization;

	public DashboardService(StockService stock, DashboardRepository dashboard, IAuthorizationService authorization)
	{
		_stock = stock;
		_dashboard = dashboard;
		_authorization = authorization;
	}

	public async Task<(DashboardData? Inventory, DashboardRoleMetrics Roles)> GetAsync(CancellationToken cancellationToken)
	{
		var isAdministrator = _authorization.CurrentUser?.IsAdministrator == true;
		var includeAdministration = isAdministrator || _authorization.HasPermission(ApplicationPermission.UsersView);
		Task<DashboardData?> inventoryTask = isAdministrator || HasCoreInventoryPermission() ? GetInventoryAsync(cancellationToken) : Task.FromResult<DashboardData?>(null);
		var presenceCutoffUtc = DateTime.UtcNow - UserSessionPresenceOptions.Default.PresenceTimeout;
		var rolesTask = _dashboard.GetRoleMetricsAsync(
			isAdministrator || _authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove),
			isAdministrator || _authorization.HasAnyPermission(ApplicationPermission.PurchaseOrdersView, ApplicationPermission.SupplierReturnsView),
			isAdministrator || _authorization.HasAnyPermission(ApplicationPermission.InventoryCountsView, ApplicationPermission.StockTransfersView),
			isAdministrator || _authorization.HasAnyPermission(ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersApprove, ApplicationPermission.ShipmentsView, ApplicationPermission.SalesInvoicesView),
			includeAdministration,
			presenceCutoffUtc,
			cancellationToken);
		await Task.WhenAll(inventoryTask, rolesTask);
		return (await inventoryTask, await rolesTask ?? new DashboardRoleMetrics(null, null, null, null, null));
	}

	private bool HasCoreInventoryPermission() => _authorization.HasAnyPermission(ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView);
	private async Task<DashboardData?> GetInventoryAsync(CancellationToken cancellationToken) => await _stock.GetDashboardDataAsync(cancellationToken);
}
