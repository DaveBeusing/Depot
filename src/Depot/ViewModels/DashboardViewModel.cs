// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Diagnostics;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class DashboardViewModel : BaseViewModel, IDisposable
{
	private readonly DashboardService _dashboardService;
	private readonly MyWorkService _myWorkService;
	private readonly IAuthorizationService _authorization;
	private readonly LatestRequest _loadRequest = new();
	private readonly LatestRequest _myWorkRequest = new();
	private int _totalItems;
	private int _totalStockQuantity;
	private decimal _totalInventoryValue;
	private int _totalMovements;
	private PurchaseOrderApprovalSummary? _approvalSummary;
	private DashboardPurchasingMetrics? _purchasingMetrics;
	private DashboardWarehouseMetrics? _warehouseMetrics;
	private DashboardSalesMetrics? _salesMetrics;
	private DashboardAdministrationMetrics? _administrationMetrics;
	private MyWorkSnapshot? _myWorkSnapshot;
	private MyWorkQuickFilter _myWorkFilter;
	private string _myWorkStatusText = string.Empty;
	private bool _isMyWorkLoading;

	public DashboardViewModel(DashboardService dashboardService, MyWorkService myWorkService, IAuthorizationService authorization)
	{
		_dashboardService = dashboardService;
		_myWorkService = myWorkService;
		_authorization = authorization;
		BuildHomeQuickActions();
	}

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

	public string GreetingText
	{
		get
		{
			var displayName = _authorization.CurrentUser?.DisplayName?.Trim();
			if (string.IsNullOrWhiteSpace(displayName)) return "Welcome back";
			var firstName = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? displayName;
			return $"Welcome back, {firstName}";
		}
	}
	public string HomeContextText => "Start with what needs your action, then continue the work that matters today.";

	public ObservableCollection<DashboardRecentMovementViewModel> RecentMovements { get; } = new();
	public ObservableCollection<MyWorkSection> MyWorkSections { get; } = new();
	public ObservableCollection<DashboardKpiCard> AdaptiveKpis { get; } = new();
	public ObservableCollection<CommercialRoleQuickAction> HomeQuickActions { get; } = new();
	public bool HasRecentMovements => RecentMovements.Count > 0;
	public bool HasNoRecentMovements => !HasRecentMovements;
	public bool HasAdaptiveKpis => AdaptiveKpis.Count > 0;
	public bool HasHomeQuickActions => HomeQuickActions.Count > 0;
	public string MyWorkStatusText { get => _myWorkStatusText; private set { _myWorkStatusText = value; OnPropertyChanged(); } }
	public bool IsMyWorkLoading { get => _isMyWorkLoading; private set { if (_isMyWorkLoading == value) return; _isMyWorkLoading = value; OnPropertyChanged(); } }
	public MyWorkQuickFilter MyWorkFilter
	{
		get => _myWorkFilter;
		set
		{
			if (_myWorkFilter == value) return;
			_myWorkFilter = value;
			OnPropertyChanged();
			NotifyMyWorkFilterPresentation();
			RebuildMyWorkSections();
		}
	}
	public bool IsAllMyWorkFilter => MyWorkFilter == MyWorkQuickFilter.All;
	public bool IsOverdueMyWorkFilter => MyWorkFilter == MyWorkQuickFilter.Overdue;
	public bool IsTodayMyWorkFilter => MyWorkFilter == MyWorkQuickFilter.Today;
	public bool IsHighPriorityMyWorkFilter => MyWorkFilter == MyWorkQuickFilter.HighPriority;
	public int MyWorkAllCount => CountMyWorkItems(MyWorkQuickFilter.All);
	public int MyWorkOverdueCount => CountMyWorkItems(MyWorkQuickFilter.Overdue);
	public int MyWorkTodayCount => CountMyWorkItems(MyWorkQuickFilter.Today);
	public int MyWorkHighPriorityCount => CountMyWorkItems(MyWorkQuickFilter.HighPriority);
	public string MyWorkFilterSummaryText => MyWorkFilter switch
	{
		MyWorkQuickFilter.Overdue => $"Showing {MyWorkOverdueCount:N0} overdue work item(s)",
		MyWorkQuickFilter.Today => $"Showing {MyWorkTodayCount:N0} work item(s) due today",
		MyWorkQuickFilter.HighPriority => $"Showing {MyWorkHighPriorityCount:N0} high-priority work item(s)",
		_ => $"Showing all {MyWorkAllCount:N0} work item(s)"
	};

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		var progress = new HomeProgressiveLoadTrace();
		BeginOperation("Loading home");
		if (request.IsCurrent)
		{
			IsMyWorkLoading = true;
			MyWorkStatusText = "Loading work queue...";
		}
		try
		{
			var dashboardApplyTask = ApplyDashboardWhenReadyAsync(_dashboardService.GetAsync(request.Token), request, progress);
			var myWorkApplyTask = ApplyMyWorkWhenReadyAsync(_myWorkService.GetAsync(request.Token), request, progress);
			await Task.WhenAll(dashboardApplyTask, myWorkApplyTask);
			if (!request.IsCurrent) return;
			BuildAdaptiveKpis();
			BuildHomeQuickActions();
			CompleteOperation(RecentMovements.Count == 0, "Home loaded");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
		{
			if (request.IsCurrent) CompleteOperation(RecentMovements.Count == 0);
		}
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Home could not be loaded"); }
		finally { if (request.IsCurrent) IsMyWorkLoading = false; }
	}

	private async Task ApplyDashboardWhenReadyAsync(
		Task<(DashboardData? Inventory, DashboardRoleMetrics Roles)> dashboardTask,
		LatestRequestLease request,
		HomeProgressiveLoadTrace progress)
	{
		var result = await dashboardTask;
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
		BuildAdaptiveKpis();
		BuildHomeQuickActions();
		OnPropertyChanged(nameof(HasRecentMovements));
		OnPropertyChanged(nameof(HasNoRecentMovements));
		progress.RecordFirstContent("dashboard");
		UpdateOperationStatus("Home content is loading...");
	}

	private async Task ApplyMyWorkWhenReadyAsync(
		Task<MyWorkSnapshot> myWorkTask,
		LatestRequestLease request,
		HomeProgressiveLoadTrace progress)
	{
		var snapshot = await myWorkTask;
		if (!request.IsCurrent) return;
		ApplyMyWork(snapshot);
		BuildAdaptiveKpis();
		BuildHomeQuickActions();
		progress.RecordFirstContent("my-work");
		UpdateOperationStatus("Home content is loading...");
	}

	public async Task RefreshMyWorkAsync(CancellationToken cancellationToken = default)
	{
		var request = _myWorkRequest.Begin(cancellationToken);
		if (request.IsCurrent)
		{
			IsMyWorkLoading = true;
			MyWorkStatusText = "Refreshing work queue...";
		}
		try
		{
			var snapshot = await _myWorkService.GetAsync(request.Token);
			if (request.IsCurrent)
			{
				ApplyMyWork(snapshot);
				BuildAdaptiveKpis();
			}
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { MyWorkStatusText = $"My Work could not be refreshed: {exception.Message}"; }
		finally { if (request.IsCurrent) IsMyWorkLoading = false; }
	}

	private void ApplyMyWork(MyWorkSnapshot snapshot)
	{
		_myWorkSnapshot = snapshot;
		NotifyMyWorkFilterPresentation();
		RebuildMyWorkSections();
		MyWorkStatusText = snapshot.Failures.Count == 0
			? "Work list is current."
			: $"Some work sources are unavailable: {string.Join(", ", snapshot.Failures.Select(failure => failure.Provider))}.";
	}

	private void RebuildMyWorkSections()
	{
		if (_myWorkSnapshot is null) return;
		var today = DateTime.Today;

		CollectionSynchronizer.Replace(
			MyWorkSections,
			_myWorkSnapshot.Sections
				.Select(section => new MyWorkSection(
					section.Kind,
					section.Title,
					section.Items.Where(item => MatchesMyWorkFilter(item, MyWorkFilter, today)).ToArray()))
				.ToArray());
	}

	private int CountMyWorkItems(MyWorkQuickFilter filter)
	{
		if (_myWorkSnapshot is null) return 0;
		var today = DateTime.Today;
		return _myWorkSnapshot.Sections
			.SelectMany(section => section.Items)
			.Count(item => MatchesMyWorkFilter(item, filter, today));
	}

	private static bool MatchesMyWorkFilter(MyWorkItem item, MyWorkQuickFilter filter, DateTime today) => filter switch
	{
		MyWorkQuickFilter.Overdue => item.DueAt is { } overdueDue && overdueDue.Date < today && item.Section != MyWorkSectionKind.RecentlyCompleted,
		MyWorkQuickFilter.Today => item.DueAt is { } todayDue && todayDue.Date == today,
		MyWorkQuickFilter.HighPriority => item.Priority >= MyWorkPriority.High,
		_ => true
	};

	private void NotifyMyWorkFilterPresentation()
	{
		OnPropertyChanged(nameof(IsAllMyWorkFilter));
		OnPropertyChanged(nameof(IsOverdueMyWorkFilter));
		OnPropertyChanged(nameof(IsTodayMyWorkFilter));
		OnPropertyChanged(nameof(IsHighPriorityMyWorkFilter));
		OnPropertyChanged(nameof(MyWorkAllCount));
		OnPropertyChanged(nameof(MyWorkOverdueCount));
		OnPropertyChanged(nameof(MyWorkTodayCount));
		OnPropertyChanged(nameof(MyWorkHighPriorityCount));
		OnPropertyChanged(nameof(MyWorkFilterSummaryText));
	}

	private void BuildAdaptiveKpis()
	{
		var candidates = new List<(DashboardKpiCard Card, int Priority, int Sequence)>();
		var items = _myWorkSnapshot?.Sections.SelectMany(section => section.Items).ToArray() ?? [];
		var today = DateTime.Today;
		var sequence = 0;

		bool HasAny(params ApplicationPermission[] permissions) => _authorization.HasAnyPermission(permissions);
		void Add(string title, string value, string detail, string routeId, int priority) =>
			candidates.Add((new DashboardKpiCard(title, value, detail, routeId), priority, sequence++));

		var receivablesOperational = HasAny(ApplicationPermission.FinanceReceivablePaymentsPost, ApplicationPermission.FinanceDunningManage);
		var payablesOperational = HasAny(
			ApplicationPermission.FinanceSupplierInvoicesCreate,
			ApplicationPermission.FinanceSupplierInvoicesSubmit,
			ApplicationPermission.FinanceSupplierInvoicesApprove,
			ApplicationPermission.FinanceSupplierMatchExceptionsApprove,
			ApplicationPermission.FinanceSupplierInvoicesPost);
		var treasuryOperational = HasAny(
			ApplicationPermission.FinanceBankingManage,
			ApplicationPermission.FinanceBankStatementsCreate,
			ApplicationPermission.FinanceBankReconciliationManage,
			ApplicationPermission.FinancePaymentProposalsCreate,
			ApplicationPermission.FinancePaymentProposalsApprove,
			ApplicationPermission.FinancePaymentRunsPost);
		var salesOperational = HasAny(
			ApplicationPermission.SalesQuotesCreate,
			ApplicationPermission.SalesQuotesEdit,
			ApplicationPermission.SalesOrdersCreate,
			ApplicationPermission.SalesOrdersEdit,
			ApplicationPermission.SalesOrdersApprove,
			ApplicationPermission.SalesOrdersRelease);
		var fulfillmentOperational = HasAny(
			ApplicationPermission.ShipmentsCreate,
			ApplicationPermission.ShipmentsEdit,
			ApplicationPermission.ShipmentsPost);
		var purchasingOperational = HasAny(
			ApplicationPermission.PurchaseOrdersCreate,
			ApplicationPermission.PurchaseOrdersEdit,
			ApplicationPermission.PurchaseOrdersSubmit,
			ApplicationPermission.PurchaseOrdersApprove,
			ApplicationPermission.PurchaseOrdersOrder,
			ApplicationPermission.GoodsReceiptsCreate,
			ApplicationPermission.GoodsReceiptsPost);
		var warehouseOperational = HasAny(
			ApplicationPermission.InventoryCountsCreate,
			ApplicationPermission.InventoryCountsEdit,
			ApplicationPermission.InventoryCountsPost,
			ApplicationPermission.StockTransfersCreate,
			ApplicationPermission.StockTransfersEdit,
			ApplicationPermission.StockTransfersPost);
		var inventoryOperational = HasAny(
			ApplicationPermission.InventoryManage,
			ApplicationPermission.StockMovementsCreate,
			ApplicationPermission.StockMovementsPost,
			ApplicationPermission.InventoryCountsCreate,
			ApplicationPermission.StockTransfersCreate);
		var administrationOperational = HasAny(
			ApplicationPermission.UsersManage,
			ApplicationPermission.RolesManage,
			ApplicationPermission.UserSessionsTerminate,
			ApplicationPermission.SecurityEventsManage,
			ApplicationPermission.DatabaseManage);
		var broadReadOnlyProjection =
			_authorization.HasPermission(ApplicationPermission.ReportsView) &&
			_authorization.HasPermission(ApplicationPermission.PurchaseOrdersView) &&
			_authorization.HasPermission(ApplicationPermission.SalesOrdersView) &&
			_authorization.HasPermission(ApplicationPermission.FinanceReceivablesView) &&
			_authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView);

		if (_authorization.HasPermission(ApplicationPermission.FinanceReceivablesView))
		{
			var overdue = items.Count(item => item.Kind == MyWorkItemKind.ReceivableOpenItem && item.DueAt is { } due && due.Date < today);
			Add("Overdue receivables", overdue.ToString("N0"), "Customer open items past due", "finance.receivables", receivablesOperational ? 520 : 240);
		}
		if (_authorization.HasPermission(ApplicationPermission.FinancePayablesView))
		{
			var supplierWork = items.Count(item => item.Kind == MyWorkItemKind.SupplierDocument && item.Section == MyWorkSectionKind.NeedsMyAction);
			Add("Payables requiring action", supplierWork.ToString("N0"), "Supplier documents in your action queue", "finance.payables", payablesOperational ? 520 : 240);
		}
		if (_authorization.HasPermission(ApplicationPermission.FinanceBankingView))
		{
			var unreconciled = items.Count(item => item.Kind == MyWorkItemKind.BankStatementLine);
			var proposals = items.Count(item => item.Kind == MyWorkItemKind.PaymentRun && item.Section == MyWorkSectionKind.NeedsMyAction);
			Add("Unreconciled bank items", unreconciled.ToString("N0"), "Statement lines still requiring reconciliation", "finance.banking", treasuryOperational ? 540 : 240);
			Add("Payment proposals", proposals.ToString("N0"), "Payment runs requiring review or execution", "finance.banking", treasuryOperational ? 530 : 230);
		}
		if (SalesMetrics is { } sales)
		{
			Add("Net sales", $"{sales.NetSalesThisMonth:N2} EUR", "Current month", "role-centers.sales-workspace", salesOperational ? 510 : 220);
			Add("Backorders", sales.BackorderedOrders.ToString("N0"), "Sales orders with backordered quantity", "role-centers.sales-control", salesOperational ? 500 : 210);
			Add("Ready to ship", sales.ReadyToShipOrders.ToString("N0"), "Released orders ready for fulfillment", "role-centers.fulfillment-workspace", fulfillmentOperational ? 550 : salesOperational ? 450 : 200);
		}
		if (PurchasingMetrics is { } purchasing)
		{
			Add("Overdue deliveries", purchasing.OverdueDeliveries.ToString("N0"), "Supplier deliveries past expected date", "role-centers.buyer-workbench", purchasingOperational ? 510 : 220);
			Add("Open purchasing work", (purchasing.PendingOrApprovedOrders + purchasing.PartiallyReceivedOrders).ToString("N0"), "Orders pending or partially received", "role-centers.buyer-workbench", purchasingOperational ? 500 : 210);
		}
		if (WarehouseMetrics is { } warehouse && (warehouseOperational || broadReadOnlyProjection))
		{
			Add("Counts ready for review", warehouse.InventoryCountsAwaitingReviewOrPosting.ToString("N0"), "Inventory counts awaiting review or posting", "role-centers.inventory-control-workspace", warehouseOperational ? 540 : 200);
			Add("Open transfers", warehouse.OpenTransfers.ToString("N0"), "Stock transfers still in progress", "role-centers.inventory-control-workspace", warehouseOperational ? 530 : 190);
		}
		if (ApprovalSummary is { } approvals)
			Add("Pending approvals", approvals.OpenCount.ToString("N0"), "Purchase and sales decisions awaiting review", "role-centers.approval-inbox", 560);
		if (HasCoreInventoryMetrics)
		{
			Add("Inventory value", $"{TotalInventoryValue:N2} EUR", $"{TotalStockQuantity:N0} stock units", "inventory.overview", inventoryOperational ? 330 : 180);
			Add("Inventory items", TotalItems.ToString("N0"), $"{TotalMovements:N0} recorded movements", "inventory.overview", inventoryOperational ? 320 : 170);
		}
		if (AdministrationMetrics is { } administration)
			Add("Online users", administration.OnlineUsers.ToString("N0"), $"{administration.ActiveSessions:N0} active sessions", "role-centers.application-administration", administrationOperational ? 500 : 160);

		CollectionSynchronizer.Replace(
			AdaptiveKpis,
			candidates
				.OrderByDescending(candidate => candidate.Priority)
				.ThenBy(candidate => candidate.Sequence)
				.Take(5)
				.Select(candidate => candidate.Card)
				.ToArray());
		OnPropertyChanged(nameof(HasAdaptiveKpis));
	}

	private void BuildHomeQuickActions()
	{
		var actions = new List<CommercialRoleQuickAction>();
		void Add(ApplicationPermission permission, string label, string actionId, string routeId)
		{
			if (_authorization.HasPermission(permission)) actions.Add(new CommercialRoleQuickAction(label, actionId, routeId));
		}

		Add(ApplicationPermission.SalesQuotesCreate, "New Quote", "sales.new-quote", "sales.quotes");
		Add(ApplicationPermission.SalesOrdersCreate, "New Order", "sales.new-order", "sales.orders");
		Add(ApplicationPermission.PurchaseOrdersCreate, "New Purchase Order", "purchasing.new-order", "purchasing.purchase-orders");
		Add(ApplicationPermission.GoodsReceiptsCreate, "Receive", "receiving.receive", "purchasing.goods-receipts");
		Add(ApplicationPermission.ShipmentsCreate, "Ship", "home.open-shipping", "sales.shipping");
		Add(ApplicationPermission.InventoryCountsCreate, "Count", "inventory.new-count", "warehouse.inventory-counts");
		Add(ApplicationPermission.FinanceSupplierInvoicesCreate, "New Supplier Invoice", "ap.new-invoice", "finance.payables");

		CollectionSynchronizer.Replace(HomeQuickActions, actions.Take(5).ToArray());
		OnPropertyChanged(nameof(HasHomeQuickActions));
	}

	public void Dispose() { _loadRequest.Dispose(); _myWorkRequest.Dispose(); }
}

public sealed record DashboardKpiCard(string Title, string Value, string Detail, string RouteId);
