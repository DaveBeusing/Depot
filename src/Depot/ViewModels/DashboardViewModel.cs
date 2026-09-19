// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

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
			OnPropertyChanged(nameof(MyWorkFilterSummaryText));
			RebuildMyWorkSections();
		}
	}
	public string MyWorkFilterSummaryText => MyWorkFilter switch
	{
		MyWorkQuickFilter.Overdue => "Showing overdue work",
		MyWorkQuickFilter.Today => "Showing work due today",
		MyWorkQuickFilter.HighPriority => "Showing high-priority work",
		_ => "Showing all work"
	};

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		BeginOperation("Loading home");
		if (request.IsCurrent)
		{
			IsMyWorkLoading = true;
			MyWorkStatusText = "Loading work queue...";
		}
		try
		{
			var dashboardTask = _dashboardService.GetAsync(request.Token);
			var myWorkTask = _myWorkService.GetAsync(request.Token);
			await Task.WhenAll(dashboardTask, myWorkTask);
			if (!request.IsCurrent) return;
			var result = await dashboardTask;
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
			ApplyMyWork(await myWorkTask);
			BuildAdaptiveKpis();
			BuildHomeQuickActions();
			OnPropertyChanged(nameof(HasRecentMovements));
			OnPropertyChanged(nameof(HasNoRecentMovements));
			CompleteOperation(RecentMovements.Count == 0, "Home loaded");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { if (request.IsCurrent) CompleteOperation(RecentMovements.Count == 0); }
		catch (Exception) when (!request.IsCurrent) { }
		catch (Exception exception) { FailOperation(exception, "Home could not be loaded"); }
		finally { if (request.IsCurrent) IsMyWorkLoading = false; }
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
		RebuildMyWorkSections();
		MyWorkStatusText = snapshot.Failures.Count == 0
			? "Work list is current."
			: $"Some work sources are unavailable: {string.Join(", ", snapshot.Failures.Select(failure => failure.Provider))}.";
	}

	private void RebuildMyWorkSections()
	{
		if (_myWorkSnapshot is null) return;
		var today = DateTime.Today;
		IEnumerable<MyWorkItem> Filter(IEnumerable<MyWorkItem> items) => MyWorkFilter switch
		{
			MyWorkQuickFilter.Overdue => items.Where(item => item.DueAt is { } due && due.Date < today && item.Section != MyWorkSectionKind.RecentlyCompleted),
			MyWorkQuickFilter.Today => items.Where(item => item.DueAt is { } due && due.Date == today),
			MyWorkQuickFilter.HighPriority => items.Where(item => item.Priority >= MyWorkPriority.High),
			_ => items
		};

		CollectionSynchronizer.Replace(
			MyWorkSections,
			_myWorkSnapshot.Sections
				.Select(section => new MyWorkSection(section.Kind, section.Title, Filter(section.Items).ToArray()))
				.ToArray());
	}

	private void BuildAdaptiveKpis()
	{
		var cards = new List<DashboardKpiCard>();
		var items = _myWorkSnapshot?.Sections.SelectMany(section => section.Items).ToArray() ?? [];
		var today = DateTime.Today;

		void Add(string title, string value, string detail, string routeId) => cards.Add(new DashboardKpiCard(title, value, detail, routeId));

		if (_authorization.HasPermission(ApplicationPermission.FinanceReceivablesView))
		{
			var overdue = items.Count(item => item.Kind == MyWorkItemKind.ReceivableOpenItem && item.DueAt is { } due && due.Date < today);
			Add("Overdue receivables", overdue.ToString("N0"), "Customer open items past due", "finance.receivables");
		}
		if (_authorization.HasPermission(ApplicationPermission.FinancePayablesView))
		{
			var supplierWork = items.Count(item => item.Kind == MyWorkItemKind.SupplierDocument && item.Section == MyWorkSectionKind.NeedsMyAction);
			Add("Payables requiring action", supplierWork.ToString("N0"), "Supplier documents in your action queue", "finance.payables");
		}
		if (_authorization.HasPermission(ApplicationPermission.FinanceBankingView))
		{
			var unreconciled = items.Count(item => item.Kind == MyWorkItemKind.BankStatementLine);
			var proposals = items.Count(item => item.Kind == MyWorkItemKind.PaymentRun && item.Section == MyWorkSectionKind.NeedsMyAction);
			Add("Unreconciled bank items", unreconciled.ToString("N0"), "Statement lines still requiring reconciliation", "finance.banking");
			Add("Payment proposals", proposals.ToString("N0"), "Payment runs requiring review or execution", "finance.banking");
		}
		if (SalesMetrics is { } sales)
		{
			Add("Net sales", $"{sales.NetSalesThisMonth:N2} EUR", "Current month", "role-centers.sales-workspace");
			Add("Backorders", sales.BackorderedOrders.ToString("N0"), "Sales orders with backordered quantity", "role-centers.sales-control");
			Add("Ready to ship", sales.ReadyToShipOrders.ToString("N0"), "Released orders ready for fulfillment", "role-centers.fulfillment-workspace");
		}
		if (PurchasingMetrics is { } purchasing)
		{
			Add("Overdue deliveries", purchasing.OverdueDeliveries.ToString("N0"), "Supplier deliveries past expected date", "role-centers.buyer-workbench");
			Add("Open purchasing work", (purchasing.PendingOrApprovedOrders + purchasing.PartiallyReceivedOrders).ToString("N0"), "Orders pending or partially received", "role-centers.buyer-workbench");
		}
		if (WarehouseMetrics is { } warehouse)
		{
			Add("Counts ready for review", warehouse.InventoryCountsAwaitingReviewOrPosting.ToString("N0"), "Inventory counts awaiting review or posting", "role-centers.inventory-control-workspace");
			Add("Open transfers", warehouse.OpenTransfers.ToString("N0"), "Stock transfers still in progress", "role-centers.inventory-control-workspace");
		}
		if (ApprovalSummary is { } approvals)
			Add("Pending approvals", approvals.OpenCount.ToString("N0"), "Purchase and sales decisions awaiting review", "role-centers.approval-inbox");
		if (HasCoreInventoryMetrics)
		{
			Add("Inventory value", $"{TotalInventoryValue:N2} EUR", $"{TotalStockQuantity:N0} stock units", "inventory.overview");
			Add("Inventory items", TotalItems.ToString("N0"), $"{TotalMovements:N0} recorded movements", "inventory.overview");
		}
		if (AdministrationMetrics is { } administration)
			Add("Online users", administration.OnlineUsers.ToString("N0"), $"{administration.ActiveSessions:N0} active sessions", "role-centers.application-administration");

		CollectionSynchronizer.Replace(AdaptiveKpis, cards.Take(5).ToArray());
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
