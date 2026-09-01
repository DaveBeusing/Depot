// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class DashboardViewModel : BaseViewModel, IDisposable
{
	private readonly DashboardService _dashboardService;
	private readonly LatestRequest _loadRequest = new();
	private int _totalItems;
	private int _totalStockQuantity;
	private decimal _totalInventoryValue;
	private int _totalMovements;
	private PurchaseOrderApprovalSummary? _approvalSummary;
	private DashboardPurchasingMetrics? _purchasingMetrics;
	private DashboardWarehouseMetrics? _warehouseMetrics;
	private DashboardSalesMetrics? _salesMetrics;
	private DashboardAdministrationMetrics? _administrationMetrics;

	public DashboardViewModel(DashboardService dashboardService) => _dashboardService = dashboardService;

	public int TotalItems { get => _totalItems; private set { _totalItems = value; OnPropertyChanged(); } }
	public int TotalStockQuantity { get => _totalStockQuantity; private set { _totalStockQuantity = value; OnPropertyChanged(); } }
	public decimal TotalInventoryValue { get => _totalInventoryValue; private set { _totalInventoryValue = value; OnPropertyChanged(); } }
	public int TotalMovements { get => _totalMovements; private set { _totalMovements = value; OnPropertyChanged(); } }
	public PurchaseOrderApprovalSummary? ApprovalSummary { get => _approvalSummary; private set { _approvalSummary = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasApprovalMetrics)); OnPropertyChanged(nameof(ApprovalSupportingText)); } }
	public DashboardPurchasingMetrics? PurchasingMetrics { get => _purchasingMetrics; private set { _purchasingMetrics = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPurchasingMetrics)); OnPropertyChanged(nameof(PurchasingAttentionCount)); OnPropertyChanged(nameof(PurchasingSupportingText)); } }
	public DashboardWarehouseMetrics? WarehouseMetrics { get => _warehouseMetrics; private set { _warehouseMetrics = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWarehouseMetrics)); OnPropertyChanged(nameof(WarehouseWorkCount)); OnPropertyChanged(nameof(WarehouseSupportingText)); } }
	public DashboardSalesMetrics? SalesMetrics { get => _salesMetrics; private set { _salesMetrics = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSalesMetrics)); OnPropertyChanged(nameof(SalesAttentionCount)); OnPropertyChanged(nameof(SalesSupportingText)); OnPropertyChanged(nameof(SalesCommercialText)); } }
	public DashboardAdministrationMetrics? AdministrationMetrics { get => _administrationMetrics; private set { _administrationMetrics = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAdministrationMetrics)); } }
	public bool HasApprovalMetrics => ApprovalSummary is not null;
	public bool HasPurchasingMetrics => PurchasingMetrics is not null;
	public bool HasWarehouseMetrics => WarehouseMetrics is not null;
	public bool HasSalesMetrics => SalesMetrics is not null;
	public bool HasAdministrationMetrics => AdministrationMetrics is not null;
	public bool HasReportsAccess => _dashboardService.CanViewReports;
	public bool HasCoreInventoryMetrics { get; private set; }
	public long PurchasingAttentionCount => PurchasingMetrics is null ? 0 : PurchasingMetrics.PendingOrApprovedOrders + PurchasingMetrics.PartiallyReceivedOrders + PurchasingMetrics.OverdueDeliveries + PurchasingMetrics.SupplierReturnsRequiringAttention;
	public long WarehouseWorkCount => WarehouseMetrics is null ? 0 : WarehouseMetrics.InventoryCountsAwaitingReviewOrPosting + WarehouseMetrics.OpenTransfers;
	public long SalesAttentionCount => SalesMetrics is null ? 0 : SalesMetrics.PendingApprovals + SalesMetrics.AwaitingReservation + SalesMetrics.BackorderedOrders + SalesMetrics.ReadyToShipOrders + SalesMetrics.DraftShipments + SalesMetrics.DraftInvoices;
	public string ApprovalSupportingText => ApprovalSummary is null ? string.Empty : $"Oldest: {(ApprovalSummary.OldestSubmittedAtUtc?.ToLocalTime().ToString("g") ?? "None")} · {ApprovalSummary.TotalAmount:C2}";
	public string PurchasingSupportingText => PurchasingMetrics is null ? string.Empty : $"Orders: {PurchasingMetrics.PendingOrApprovedOrders:N0} · Partial: {PurchasingMetrics.PartiallyReceivedOrders:N0} · Overdue: {PurchasingMetrics.OverdueDeliveries:N0} · Returns: {PurchasingMetrics.SupplierReturnsRequiringAttention:N0}";
	public string WarehouseSupportingText => WarehouseMetrics is null ? string.Empty : $"Counts: {WarehouseMetrics.InventoryCountsAwaitingReviewOrPosting:N0} · Transfers: {WarehouseMetrics.OpenTransfers:N0}";
	public string SalesSupportingText => SalesMetrics is null ? string.Empty : $"Approvals: {SalesMetrics.PendingApprovals:N0} · Reserve: {SalesMetrics.AwaitingReservation:N0} · Backorders: {SalesMetrics.BackorderedOrders:N0} · Ready: {SalesMetrics.ReadyToShipOrders:N0}";
	public string SalesCommercialText => SalesMetrics is null ? string.Empty : $"Draft shipments: {SalesMetrics.DraftShipments:N0} · Draft invoices: {SalesMetrics.DraftInvoices:N0} · Returns: {SalesMetrics.ReturnsThisMonth:N0} · Credits: {SalesMetrics.CreditNotesThisMonth:N0} · Net sales: {SalesMetrics.NetSalesThisMonth:C2}";

	public ObservableCollection<DashboardRecentMovementViewModel> RecentMovements { get; } = new();
	public bool HasRecentMovements => RecentMovements.Count > 0;
	public bool HasNoRecentMovements => !HasRecentMovements;

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		BeginOperation("Loading dashboard");
		try
		{
			var result = await _dashboardService.GetAsync(request.Token);
			if (!request.IsCurrent) return;
			var data = result.Inventory;
			var summary = data?.Summary ?? new DashboardSummary();
			HasCoreInventoryMetrics = data is not null;
			OnPropertyChanged(nameof(HasCoreInventoryMetrics));
			OnPropertyChanged(nameof(HasReportsAccess));
			TotalItems = summary.TotalItems;
			TotalStockQuantity = summary.TotalStockQuantity;
			TotalInventoryValue = summary.TotalInventoryValue;
			TotalMovements = summary.TotalMovements;
			CollectionSynchronizer.Replace(RecentMovements, data?.RecentMovements.Select(movement => new DashboardRecentMovementViewModel(movement)).ToArray() ?? []);
			ApprovalSummary = result.Roles.Approvals;
			PurchasingMetrics = result.Roles.Purchasing;
			WarehouseMetrics = result.Roles.Warehouse;
			SalesMetrics = result.Roles.Sales;
			AdministrationMetrics = result.Roles.Administration;
			OnPropertyChanged(nameof(HasRecentMovements));
			OnPropertyChanged(nameof(HasNoRecentMovements));
			CompleteOperation(RecentMovements.Count == 0, "Dashboard loaded");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { if (request.IsCurrent) CompleteOperation(RecentMovements.Count == 0); }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Dashboard could not be loaded"); }
	}

	public void Dispose() => _loadRequest.Dispose();
}
