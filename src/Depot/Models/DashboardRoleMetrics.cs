// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record DashboardRoleMetrics(
	PurchaseOrderApprovalSummary? Approvals,
	DashboardPurchasingMetrics? Purchasing,
	DashboardWarehouseMetrics? Warehouse,
	DashboardSalesMetrics? Sales,
	DashboardAdministrationMetrics? Administration);

public sealed record DashboardPurchasingMetrics(
	long PendingOrApprovedOrders,
	long PartiallyReceivedOrders,
	long OverdueDeliveries,
	long SupplierReturnsRequiringAttention);

public sealed record DashboardWarehouseMetrics(
	long InventoryCountsAwaitingReviewOrPosting,
	long OpenTransfers);

public sealed record DashboardSalesMetrics(
	long PendingApprovals,
	long ReadyToShipOrders,
	long DraftShipments,
	long DraftInvoices);

public sealed record DashboardAdministrationMetrics(long ActiveUsers);
