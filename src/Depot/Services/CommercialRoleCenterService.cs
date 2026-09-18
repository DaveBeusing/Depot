// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public sealed class CommercialRoleCenterService
{
	public const int SourceItemLimit = 12;
	public const int MaximumItemsPerSection = 50;

	private readonly IAuthorizationService _authorization;
	private readonly MyWorkService _myWork;
	private readonly DashboardService _dashboard;
	private readonly SalesQuoteService _salesQuotes;
	private readonly SalesOrderService _salesOrders;
	private readonly PurchaseOrderService _purchaseOrders;
	private readonly PurchaseOrderApprovalService _purchaseApprovals;
	private readonly SupplierReturnService _supplierReturns;
	private readonly GoodsReceiptService _goodsReceipts;
	private readonly ShipmentService _shipments;
	private readonly InventoryCountService _inventoryCounts;
	private readonly StockTransferService _stockTransfers;
	private readonly MaterialIssueService _materialIssues;
	private readonly MaterialReturnService _materialReturns;
	private readonly FinanceAccountsReceivableService _receivables;
	private readonly FinanceAccountsPayableService _payables;
	private readonly FinanceBankingService _banking;
	private readonly FinanceGeneralLedgerService _generalLedger;
	private readonly FinanceInventoryAccountingService _inventoryAccounting;
	private readonly FinanceInventoryCostingService _inventoryCosting;
	private readonly FinanceFinancialReportingService _financialReporting;

	public CommercialRoleCenterService(
		IAuthorizationService authorization,
		MyWorkService myWork,
		DashboardService dashboard,
		SalesQuoteService salesQuotes,
		SalesOrderService salesOrders,
		PurchaseOrderService purchaseOrders,
		PurchaseOrderApprovalService purchaseApprovals,
		SupplierReturnService supplierReturns,
		GoodsReceiptService goodsReceipts,
		ShipmentService shipments,
		InventoryCountService inventoryCounts,
		StockTransferService stockTransfers,
		MaterialIssueService materialIssues,
		MaterialReturnService materialReturns,
		FinanceAccountsReceivableService receivables,
		FinanceAccountsPayableService payables,
		FinanceBankingService banking,
		FinanceGeneralLedgerService generalLedger,
		FinanceInventoryAccountingService inventoryAccounting,
		FinanceInventoryCostingService inventoryCosting,
		FinanceFinancialReportingService financialReporting)
	{
		_authorization = authorization;
		_myWork = myWork;
		_dashboard = dashboard;
		_salesQuotes = salesQuotes;
		_salesOrders = salesOrders;
		_purchaseOrders = purchaseOrders;
		_purchaseApprovals = purchaseApprovals;
		_supplierReturns = supplierReturns;
		_goodsReceipts = goodsReceipts;
		_shipments = shipments;
		_inventoryCounts = inventoryCounts;
		_stockTransfers = stockTransfers;
		_materialIssues = materialIssues;
		_materialReturns = materialReturns;
		_receivables = receivables;
		_payables = payables;
		_banking = banking;
		_generalLedger = generalLedger;
		_inventoryAccounting = inventoryAccounting;
		_inventoryCosting = inventoryCosting;
		_financialReporting = financialReporting;
	}

	public bool CanAccess(CommercialRoleCenterKind kind) => kind switch
	{
		CommercialRoleCenterKind.SalesWorkspace => _authorization.HasAnyPermission(ApplicationPermission.CustomersView, ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesOrdersView),
		CommercialRoleCenterKind.SalesControlCenter => _authorization.HasAnyPermission(ApplicationPermission.SalesOrdersApprove, ApplicationPermission.SalesPricingManage, ApplicationPermission.ShipmentsView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView),
		CommercialRoleCenterKind.BuyerWorkbench => _authorization.HasAnyPermission(ApplicationPermission.PurchaseOrdersView, ApplicationPermission.SuppliersView, ApplicationPermission.SupplierReturnsView),
		CommercialRoleCenterKind.ApprovalInbox => HasApprovalPermission(),
		CommercialRoleCenterKind.ReceivingWorkspace => _authorization.HasAnyPermission(ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost, ApplicationPermission.SupplierReturnsCreate),
		CommercialRoleCenterKind.FulfillmentWorkspace => _authorization.HasAnyPermission(ApplicationPermission.ShipmentsCreate, ApplicationPermission.ShipmentsEdit, ApplicationPermission.ShipmentsPost, ApplicationPermission.CustomerReturnsCreate, ApplicationPermission.CustomerReturnsPost),
		CommercialRoleCenterKind.InventoryControlWorkspace => _authorization.HasAnyPermission(ApplicationPermission.InventoryCountsCreate, ApplicationPermission.InventoryCountsEdit, ApplicationPermission.InventoryCountsPost, ApplicationPermission.StockTransfersCreate, ApplicationPermission.StockTransfersEdit, ApplicationPermission.StockTransfersPost, ApplicationPermission.MaterialIssuesCreate, ApplicationPermission.MaterialIssuesPost, ApplicationPermission.MaterialReturnsCreate, ApplicationPermission.MaterialReturnsPost),
		CommercialRoleCenterKind.ReceivablesWorkspace => _authorization.HasPermission(ApplicationPermission.FinanceReceivablesView),
		CommercialRoleCenterKind.PayablesWorkspace => _authorization.HasPermission(ApplicationPermission.FinancePayablesView),
		CommercialRoleCenterKind.TreasuryWorkspace => _authorization.HasAnyPermission(
			ApplicationPermission.FinanceBankStatementsCreate,
			ApplicationPermission.FinancePaymentProposalsCreate,
			ApplicationPermission.FinancePaymentRunsPost,
			ApplicationPermission.FinanceCashPositionView),
		CommercialRoleCenterKind.AccountingControlWorkspace => _authorization.HasAnyPermission(
			ApplicationPermission.FinancePeriodsView,
			ApplicationPermission.FinanceGeneralLedgerView,
			ApplicationPermission.FinanceInventoryAccountingView,
			ApplicationPermission.FinanceFinancialReportingView),
		CommercialRoleCenterKind.ManagementCockpit =>
			_authorization.HasPermission(ApplicationPermission.DashboardView) &&
			_authorization.HasPermission(ApplicationPermission.InventoryView) &&
			_authorization.HasPermission(ApplicationPermission.PurchaseOrdersView) &&
			_authorization.HasPermission(ApplicationPermission.SalesOrdersView) &&
			_authorization.HasPermission(ApplicationPermission.FinanceReceivablesView) &&
			_authorization.HasPermission(ApplicationPermission.FinancePayablesView) &&
			_authorization.HasPermission(ApplicationPermission.FinanceBankingView) &&
			_authorization.HasPermission(ApplicationPermission.FinanceCashPositionView) &&
			_authorization.HasPermission(ApplicationPermission.ReportsView),
		CommercialRoleCenterKind.AuditComplianceCenter =>
			_authorization.HasPermission(ApplicationPermission.AuditLogView) &&
			_authorization.HasPermission(ApplicationPermission.AuditLogExport) &&
			_authorization.HasPermission(ApplicationPermission.SecurityEventsView) &&
			_authorization.HasPermission(ApplicationPermission.UsersView) &&
			_authorization.HasPermission(ApplicationPermission.RolesView) &&
			_authorization.HasPermission(ApplicationPermission.ReportsView) &&
			_authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView),
		CommercialRoleCenterKind.MasterDataWorkspace =>
			_authorization.HasPermission(ApplicationPermission.MasterDataView) &&
			_authorization.HasPermission(ApplicationPermission.ItemsView) &&
			_authorization.HasPermission(ApplicationPermission.CustomersView) &&
			_authorization.HasPermission(ApplicationPermission.SuppliersView),
		CommercialRoleCenterKind.ApplicationAdministrationCenter =>
			_authorization.HasPermission(ApplicationPermission.AdministrationView) &&
			_authorization.HasPermission(ApplicationPermission.UsersView) &&
			_authorization.HasPermission(ApplicationPermission.RolesView) &&
			_authorization.HasPermission(ApplicationPermission.SettingsView) &&
			_authorization.HasPermission(ApplicationPermission.DatabaseView) &&
			_authorization.HasPermission(ApplicationPermission.SecurityEventsView) &&
			_authorization.HasPermission(ApplicationPermission.AuditLogView),
		_ => false
	};

	public Task<CommercialRoleCenterSnapshot> GetAsync(CommercialRoleCenterKind kind, CancellationToken cancellationToken = default)
	{
		if (!CanAccess(kind)) throw new UnauthorizedAccessException("The current user cannot access this role center.");
		return kind switch
		{
			CommercialRoleCenterKind.SalesWorkspace => GetSalesWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.SalesControlCenter => GetSalesControlCenterAsync(cancellationToken),
			CommercialRoleCenterKind.BuyerWorkbench => GetBuyerWorkbenchAsync(cancellationToken),
			CommercialRoleCenterKind.ApprovalInbox => GetApprovalInboxAsync(cancellationToken),
			CommercialRoleCenterKind.ReceivingWorkspace => GetReceivingWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.FulfillmentWorkspace => GetFulfillmentWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.InventoryControlWorkspace => GetInventoryControlWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.ReceivablesWorkspace => GetReceivablesWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.PayablesWorkspace => GetPayablesWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.TreasuryWorkspace => GetTreasuryWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.AccountingControlWorkspace => GetAccountingControlWorkspaceAsync(cancellationToken),
			CommercialRoleCenterKind.ManagementCockpit => GetManagementCockpitAsync(cancellationToken),
			CommercialRoleCenterKind.AuditComplianceCenter => GetAuditComplianceCenterAsync(),
			CommercialRoleCenterKind.MasterDataWorkspace => GetMasterDataWorkspaceAsync(),
			CommercialRoleCenterKind.ApplicationAdministrationCenter => GetApplicationAdministrationCenterAsync(),
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};
	}

	public async Task DecideAsync(CommercialRoleItem item, bool approve, string? comment, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);
		switch (item.Kind)
		{
			case CommercialRoleItemKind.PurchaseOrderApproval:
				if (approve) await _purchaseApprovals.ApproveAsync(item.EntityId, item.Version, comment, cancellationToken);
				else await _purchaseApprovals.RejectAsync(item.EntityId, item.Version, comment, cancellationToken);
				break;
			case CommercialRoleItemKind.SalesOrderApproval:
				if (approve) await _salesOrders.ApproveAsync(item.EntityId, item.Version, comment, cancellationToken);
				else await _salesOrders.RejectAsync(item.EntityId, item.Version, comment, cancellationToken);
				break;
			case CommercialRoleItemKind.SupplierInvoice:
				await _payables.DecideAsync(item.EntityId, new FinanceSupplierApprovalRequest
				{
					ExpectedVersion = item.Version,
					Approve = approve,
					Comment = comment
				}, cancellationToken);
				break;
			case CommercialRoleItemKind.PaymentProposal when approve:
				await _banking.ApprovePaymentRunAsync(item.EntityId, item.Version, comment, cancellationToken);
				break;
			case CommercialRoleItemKind.PaymentProposal:
				throw new InvalidOperationException("Payment proposals do not expose a rejection operation in the existing banking service.");
			default:
				throw new InvalidOperationException("The selected work item is not an approval item.");
		}
	}

	private async Task<CommercialRoleCenterSnapshot> GetSalesWorkspaceAsync(CancellationToken cancellationToken)
	{
		var user = RequireUser();
		var workTask = _myWork.GetAsync(cancellationToken);
		var quotesTask = _authorization.HasPermission(ApplicationPermission.SalesQuotesView)
			? _salesQuotes.SearchAsync(null, null, 1, SourceItemLimit * 3, cancellationToken)
			: Task.FromResult(new PageResult<SalesQuote>([], 1, SourceItemLimit * 3, 0));
		var ordersTask = _authorization.HasPermission(ApplicationPermission.SalesOrdersView)
			? _salesOrders.SearchAsync(null, null, 1, SourceItemLimit * 3, cancellationToken)
			: Task.FromResult(new PageResult<SalesOrder>([], 1, SourceItemLimit * 3, 0));
		await Task.WhenAll(workTask, quotesTask, ordersTask);

		var work = await workTask;
		var quotes = (await quotesTask).Items.Where(value => value.CreatedByUserId == user.Id).OrderByDescending(value => value.CreatedAtUtc).Take(SourceItemLimit).ToArray();
		var orders = (await ordersTask).Items.Where(value => value.CreatedByUserId == user.Id).ToArray();
		var myDrafts = WorkItems(work, MyWorkSectionKind.MyDrafts, MyWorkItemKind.SalesOrder, user.Id);
		var waiting = WorkItems(work, MyWorkSectionKind.Waiting, MyWorkItemKind.SalesOrderApproval, user.Id);
		var exceptions = WorkItems(work, MyWorkSectionKind.Exceptions, MyWorkItemKind.SalesOrder, user.Id);
		var rejected = orders.Where(value => value.Status == SalesOrderStatus.Rejected).Take(SourceItemLimit).Select(OrderItem).ToArray();

		var recentCustomers = quotes.Select(value => new { value.CustomerId, value.CustomerName, At = value.CreatedAtUtc, Reference = value.QuoteNumber })
			.Concat(orders.Select(value => new { value.CustomerId, value.CustomerName, At = value.OrderDate.ToUniversalTime(), Reference = value.OrderNumber }))
			.GroupBy(value => value.CustomerId)
			.Select(group => group.OrderByDescending(value => value.At).First())
			.OrderByDescending(value => value.At)
			.Take(SourceItemLimit)
			.Select(value => new CommercialRoleItem(CommercialRoleItemKind.Customer, value.CustomerId, 0, value.CustomerName, "Recently used customer", value.Reference, "Recent", null, value.At, null, null, "sales.customers"))
			.ToArray();

		return Snapshot(
			CommercialRoleCenterKind.SalesWorkspace,
			"Sales Workspace",
			"Your quotes, sales orders, approval handoffs and recent customers in one commercial starting point.",
			[
				Section("My Quotes", "No quotes are currently associated with your user.", quotes.Select(QuoteItem)),
				Section("My Draft Orders", "You have no sales-order drafts.", myDrafts),
				Section("Waiting for Approval", "None of your sales orders are waiting for approval.", waiting),
				Section("Returned / Rejected for Correction", "No rejected sales orders require correction.", rejected),
				Section("Backordered / Blocked Own Orders", "No owned sales-order exceptions are currently modeled.", exceptions),
				Section("Recently Used Customers", "No recent customer context is available.", recentCustomers)
			],
			[],
			[
				new("New Customer", "sales.new-customer", "sales.customers"),
				new("New Quote", "sales.new-quote", "sales.quotes"),
				new("New Sales Order", "sales.new-order", "sales.orders")
			],
			work.Failures);
	}

	private async Task<CommercialRoleCenterSnapshot> GetSalesControlCenterAsync(CancellationToken cancellationToken)
	{
		var workTask = _myWork.GetAsync(cancellationToken);
		var metricsTask = _dashboard.GetAsync(cancellationToken);
		var releasedTask = _authorization.HasPermission(ApplicationPermission.SalesOrdersView)
			? _salesOrders.SearchAsync(null, SalesOrderStatus.Released, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<SalesOrder>([], 1, SourceItemLimit, 0));
		var partiallyShippedTask = _authorization.HasPermission(ApplicationPermission.SalesOrdersView)
			? _salesOrders.SearchAsync(null, SalesOrderStatus.PartiallyShipped, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<SalesOrder>([], 1, SourceItemLimit, 0));
		await Task.WhenAll(workTask, metricsTask, releasedTask, partiallyShippedTask);
		var work = await workTask;
		var metrics = (await metricsTask).Roles.Sales;
		var released = (await releasedTask).Items.Concat((await partiallyShippedTask).Items).Take(MaximumItemsPerSection).Select(OrderItem).ToArray();
		var approvals = WorkItems(work, MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrderApproval);
		var fulfillment = work.Sections.Single(section => section.Kind == MyWorkSectionKind.NeedsMyAction).Items
			.Where(item => item.Kind is MyWorkItemKind.SalesOrder or MyWorkItemKind.Shipment)
			.Take(MaximumItemsPerSection).Select(FromMyWork).ToArray();
		var blocked = WorkItems(work, MyWorkSectionKind.Exceptions, MyWorkItemKind.SalesOrder);

		var kpis = metrics is null ? [] :
			new CommercialRoleKpi[]
			{
				new("Approvals", metrics.PendingApprovals.ToString("N0"), "Sales orders awaiting decision", "approvals.sales"),
				new("Ready to ship", metrics.ReadyToShipOrders.ToString("N0"), $"Backordered: {metrics.BackorderedOrders:N0}", "sales.shipping"),
				new("Awaiting reservation", metrics.AwaitingReservation.ToString("N0"), "Approved orders awaiting inventory reservation", "sales.orders"),
				new("Draft invoices", metrics.DraftInvoices.ToString("N0"), $"Credits this month: {metrics.CreditNotesThisMonth:N0}", "sales.invoices"),
				new("Net sales this month", metrics.NetSalesThisMonth.ToString("C2"), $"Returns: {metrics.ReturnsThisMonth:N0}", "sales.invoices")
			};

		return Snapshot(
			CommercialRoleCenterKind.SalesControlCenter,
			"Sales Control Center",
			"Sales approvals, fulfillment handoff, blocked orders and read-only commercial KPIs without introducing CRM or pipeline state.",
			[
				Section("Sales Approvals Requiring Decision", "No sales approvals require your decision.", approvals),
				Section("Released / Partially Shipped Orders", "No released sales orders are currently in fulfillment.", released),
				Section("Fulfillment Handoff", "No released sales work requires handoff.", fulfillment),
				Section("Blocked / Backordered Orders", "No modeled sales exceptions require attention.", blocked)
			],
			kpis,
			[
				new("Open Sales Approvals", "navigate", "approvals.sales"),
				new("Open Pricing", "navigate", "sales.pricing"),
				new("Open Invoices", "navigate", "sales.invoices")
			],
			work.Failures);
	}

	private async Task<CommercialRoleCenterSnapshot> GetBuyerWorkbenchAsync(CancellationToken cancellationToken)
	{
		Task<PageResult<PurchaseOrder>> Query(PurchaseOrderStatus status) => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersView)
			? _purchaseOrders.SearchAsync(null, status, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<PurchaseOrder>([], 1, SourceItemLimit, 0));

		var draftsTask = Query(PurchaseOrderStatus.Draft);
		var submittedTask = Query(PurchaseOrderStatus.PendingApproval);
		var approvedTask = Query(PurchaseOrderStatus.Approved);
		var orderedTask = Query(PurchaseOrderStatus.Ordered);
		var partialTask = Query(PurchaseOrderStatus.PartiallyReceived);
		var returnsTask = _authorization.HasPermission(ApplicationPermission.SupplierReturnsView)
			? _supplierReturns.SearchAsync(null, null, SupplierReturnStatus.Draft, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<SupplierReturnOverviewItem>([], 1, SourceItemLimit, 0));
		await Task.WhenAll(draftsTask, submittedTask, approvedTask, orderedTask, partialTask, returnsTask);

		var now = DateTime.Today;
		var ordered = (await orderedTask).Items;
		var overdue = ordered.Where(value => value.ExpectedDeliveryDate is { } due && due.Date < now).Select(OrderItem).ToArray();
		var supplierReturns = (await returnsTask).Items.Select(value => new CommercialRoleItem(
			CommercialRoleItemKind.SupplierReturn, value.Id, value.Version, value.ReturnNumber, "Supplier return requiring attention",
			value.SupplierName, value.StatusDisplayName, null, null, null, null, "purchasing.supplier-returns")).ToArray();

		return Snapshot(
			CommercialRoleCenterKind.BuyerWorkbench,
			"Buyer Workbench",
			"Purchase-order progression, overdue deliveries and supplier-return attention in one buying workspace.",
			[
				Section("Draft Purchase Orders", "No draft purchase orders.", (await draftsTask).Items.Select(OrderItem)),
				Section("Submitted", "No purchase orders are awaiting approval.", (await submittedTask).Items.Select(OrderItem)),
				Section("Approved but not Ordered", "No approved purchase orders are waiting to be placed.", (await approvedTask).Items.Select(OrderItem)),
				Section("Ordered with Overdue Delivery", "No overdue supplier deliveries.", overdue),
				Section("Partially Received", "No purchase orders are partially received.", (await partialTask).Items.Select(OrderItem)),
				Section("Supplier Returns Requiring Attention", "No supplier-return drafts require attention.", supplierReturns)
			],
			[],
			[
				new("New Purchase Order", "purchasing.new-order", "purchasing.purchase-orders"),
				new("Open Supplier", "purchasing.open-supplier", "administration.suppliers"),
				new("Receive Goods", "purchasing.receive", "purchasing.goods-receipts")
			],
			[]);
	}




	private async Task<CommercialRoleCenterSnapshot> GetTreasuryWorkspaceAsync(CancellationToken cancellationToken)
	{
		var statementsTask = _banking.GetStatementsAsync(cancellationToken: cancellationToken);
		var unreconciledTask = _banking.SearchUnreconciledLinesAsync(null, 1, MaximumItemsPerSection, cancellationToken);
		var paymentRunsTask = _banking.SearchPaymentRunsAsync(1, MaximumItemsPerSection, cancellationToken);
		var cashTask = _authorization.HasPermission(ApplicationPermission.FinanceCashPositionView)
			? _banking.GetCashPositionAsync(cancellationToken)
			: Task.FromResult<IReadOnlyList<FinanceCashPosition>>([]);

		await Task.WhenAll(statementsTask, unreconciledTask, paymentRunsTask, cashTask);

		var today = DateOnly.FromDateTime(DateTime.Today);
		var unreconciledPage = await unreconciledTask;
		var unreconciled = unreconciledPage.Items;
		var unreconciledByStatement = unreconciled
			.GroupBy(value => value.StatementId)
			.ToDictionary(group => group.Key, group => group.Count());
		var statements = (await statementsTask)
			.Where(value => unreconciledByStatement.ContainsKey(value.Id) || value.ImportedAtUtc >= DateTime.UtcNow.AddDays(-7))
			.OrderByDescending(value => value.ImportedAtUtc)
			.Take(MaximumItemsPerSection)
			.Select(value => BankStatementItem(value, unreconciledByStatement.GetValueOrDefault(value.Id)))
			.ToArray();
		var cash = await cashTask;
		var reconciliationExceptions = cash
			.Where(value => value.Difference != 0m)
			.OrderByDescending(value => Math.Abs(value.Difference))
			.Select(CashPositionItem)
			.ToArray();
		var openReconciliation = unreconciled
			.OrderBy(value => value.BookingDate)
			.ThenBy(value => value.Id)
			.Select(value => BankStatementLineItem(value, today))
			.ToArray();
		var paymentRuns = (await paymentRunsTask).Items;
		var awaitingDecision = paymentRuns
			.Where(value => value.Status == FinancePaymentRunStatus.Draft)
			.OrderBy(value => value.PaymentDate)
			.ThenBy(value => value.Id)
			.Select(TreasuryPaymentRunItem)
			.ToArray();
		var readyToExecute = paymentRuns
			.Where(value => value.Status is FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted)
			.OrderBy(value => value.PaymentDate)
			.ThenBy(value => value.Id)
			.Select(TreasuryPaymentRunItem)
			.ToArray();
		var recentRuns = paymentRuns
			.Where(value => value.Status is FinancePaymentRunStatus.Executed or FinancePaymentRunStatus.Cancelled)
			.OrderByDescending(value => value.CompletedAtUtc ?? value.CreatedAtUtc)
			.ThenByDescending(value => value.Id)
			.Select(TreasuryPaymentRunItem)
			.ToArray();

		var quickActions = new List<CommercialRoleQuickAction>();
		if (_banking.CanImportStatements) quickActions.Add(new("Import Bank Statement", "navigate", "finance.banking"));
		if (_banking.CanReconcile) quickActions.Add(new("Reconcile Statement Lines", "navigate", "finance.banking"));
		if (_banking.CanCreatePaymentRuns) quickActions.Add(new("Create Payment Proposal", "navigate", "finance.banking"));
		if (_banking.CanExecutePaymentRuns) quickActions.Add(new("Execute Approved Payment Run", "navigate", "finance.banking"));

		return Snapshot(
			CommercialRoleCenterKind.TreasuryWorkspace,
			"Treasury Workspace",
			"Bank statements, reconciliation, payment proposals, payment execution and cash position without collapsing preparation, approval and execution authority.",
			[
				Section("New / Unprocessed Bank Statements", "No new or unreconciled bank statements require attention.", statements),
				Section("Reconciliation Exceptions", "No cash-to-ledger reconciliation differences are currently visible.", reconciliationExceptions),
				Section("Open Reconciliation Items", "No unreconciled bank statement lines remain.", openReconciliation),
				Section("Payment Proposals Awaiting Preparation / Decision", "No draft payment proposals are waiting for preparation or independent decision.", awaitingDecision),
				Section("Approved Proposals Ready for Payment Run", "No approved payment proposals are ready for execution.", readyToExecute),
				Section("Recent Payment Runs", "No recently completed or cancelled payment runs are available.", recentRuns),
				Section("Cash Position", _authorization.HasPermission(ApplicationPermission.FinanceCashPositionView) ? "No active cash-position evidence is available." : "Cash position is not available to this account.", cash.Select(CashPositionItem))
			],
			[
				new("Unreconciled", unreconciledPage.TotalCount.ToString("N0"), "Bank statement lines requiring reconciliation", "finance.banking"),
				new("Draft proposals", awaitingDecision.Length.ToString("N0"), "Preparation and approval remain separate authorities", "finance.banking"),
				new("Ready to execute", readyToExecute.Length.ToString("N0"), "Approved payment runs", "finance.banking"),
				new("Cash differences", reconciliationExceptions.Length.ToString("N0"), "Bank accounts with statement-to-GL differences", "finance.banking")
			],
			quickActions,
			[]);
	}

	private async Task<CommercialRoleCenterSnapshot> GetAccountingControlWorkspaceAsync(CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var journalsTask = _authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerView)
			? _generalLedger.SearchAsync(null, null, null, null, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<FinanceJournalEntrySummary>([], 1, SourceItemLimit, 0));
		var periodsTask = _authorization.HasPermission(ApplicationPermission.FinancePeriodsView)
			? _generalLedger.GetPeriodsForDateAsync(today, cancellationToken)
			: Task.FromResult<IReadOnlyList<AccountingPeriod>>([]);
		var configurationTask = _authorization.HasPermission(ApplicationPermission.FinanceInventoryAccountingView)
			? _inventoryAccounting.GetConfigurationAsync(cancellationToken)
			: Task.FromResult<FinanceInventoryAccountingConfiguration?>(null);
		var policyTask = _authorization.HasPermission(ApplicationPermission.FinanceInventoryAccountingView)
			? _inventoryCosting.GetPolicyAsync(cancellationToken)
			: Task.FromResult<FinanceInventoryAccountingPolicy?>(null);
		var valuationTask = _authorization.HasPermission(ApplicationPermission.FinanceInventoryAccountingView)
			? _inventoryCosting.GetValuationSummaryAsync(cancellationToken)
			: Task.FromResult<IReadOnlyList<FinanceInventoryValuationSummary>>([]);
		var reconciliationTask = _authorization.HasPermission(ApplicationPermission.FinanceInventoryAccountingView)
			? _inventoryCosting.GetRecentReconciliationsAsync(cancellationToken)
			: Task.FromResult<IReadOnlyList<FinanceInventoryReconciliationRun>>([]);
		var snapshotsTask = _authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView)
			? _financialReporting.GetRecentSnapshotsAsync(cancellationToken: cancellationToken)
			: Task.FromResult<IReadOnlyList<FinanceReportSnapshot>>([]);
		var profilesTask = _authorization.HasPermission(ApplicationPermission.FinancePostingProfilesView)
			? _generalLedger.GetPostingProfilesAsync(cancellationToken)
			: Task.FromResult<IReadOnlyList<FinancePostingProfile>>([]);

		await Task.WhenAll(journalsTask, periodsTask, configurationTask, policyTask, valuationTask, reconciliationTask, snapshotsTask, profilesTask);

		var journals = (await journalsTask).Items
			.Where(value => value.EntryKind != FinanceJournalEntryKind.Manual || _generalLedger.CanPostManualJournal)
			.ToArray();
		var periods = await periodsTask;
		var configuration = await configurationTask;
		var policy = await policyTask;
		var valuation = await valuationTask;
		var reconciliations = await reconciliationTask;
		var snapshots = await snapshotsTask;
		var profiles = await profilesTask;

		var configurationWarnings = new List<CommercialRoleItem>();
		if (_authorization.HasPermission(ApplicationPermission.FinanceInventoryAccountingView))
		{
			if (configuration is null)
				configurationWarnings.Add(FinanceStatusItem("INV-CONFIG", "Inventory Accounting configuration", "No configuration is available.", "Warning", "finance.inventory-accounting", "Configure Inventory Accounting"));
			else if (!configuration.IsActive)
				configurationWarnings.Add(FinanceStatusItem("INV-CONFIG", "Inventory Accounting configuration", "The configured Inventory Accounting profile is inactive.", "Warning", "finance.inventory-accounting", "Review configuration"));
			if (policy is null)
				configurationWarnings.Add(FinanceStatusItem("INV-POLICY", "Inventory Accounting policy", "No valuation/reconciliation policy is available.", "Warning", "finance.inventory-accounting", "Configure policy"));
			else if (!policy.IsActive)
				configurationWarnings.Add(FinanceStatusItem("INV-POLICY", "Inventory Accounting policy", "The configured valuation/reconciliation policy is inactive.", "Warning", "finance.inventory-accounting", "Review policy"));
		}
		if (_authorization.HasPermission(ApplicationPermission.FinancePostingProfilesView) && profiles.Count == 0)
			configurationWarnings.Add(FinanceStatusItem("POSTING", "Posting profiles", "No Finance posting profiles are available.", "Warning", "finance.inventory-accounting", "Review posting configuration"));
		if (_authorization.HasPermission(ApplicationPermission.FinancePeriodsView) && periods.Count == 0)
			configurationWarnings.Add(FinanceStatusItem("PERIOD", "Fiscal period", $"No fiscal period covers {today:yyyy-MM-dd}.", "Warning", "finance.reporting", "Review fiscal period configuration"));

		var reconciliationDifferences = reconciliations
			.Where(value => value.Difference != 0m)
			.OrderByDescending(value => Math.Abs(value.Difference))
			.Select(InventoryReconciliationItem)
			.ToArray();
		var needsAttention = configurationWarnings
			.Concat(reconciliationDifferences)
			.Take(MaximumItemsPerSection)
			.ToArray();
		var periodItems = periods
			.OrderBy(value => value.StartDate)
			.ThenBy(value => value.Code)
			.Select(PeriodStatusItem)
			.ToArray();
		var accountingStatus = new List<CommercialRoleItem>();
		if (configuration is not null)
		{
			accountingStatus.Add(FinanceStatusItem(
				"GRNI / COGS",
				"Inventory posting profiles",
				$"GRNI profile #{configuration.GoodsReceiptPostingProfileId:N0} · COGS profile #{configuration.SalesIssuePostingProfileId:N0}",
				configuration.IsActive ? "Configured" : "Inactive",
				"finance.inventory-accounting",
				"Open Inventory Accounting"));
		}
		accountingStatus.AddRange(valuation
			.OrderByDescending(value => Math.Abs(value.TransactionValue))
			.Take(SourceItemLimit)
			.Select(InventoryValuationItem));

		var quickActions = new List<CommercialRoleQuickAction>();
		if (_authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerView)) quickActions.Add(new("General Ledger Reporting", "navigate", "finance.reporting"));
		if (_authorization.HasPermission(ApplicationPermission.FinanceInventoryAccountingView)) quickActions.Add(new("Inventory Accounting", "navigate", "finance.inventory-accounting"));
		if (_authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView)) quickActions.Add(new("Financial Reporting", "navigate", "finance.reporting"));

		return Snapshot(
			CommercialRoleCenterKind.AccountingControlWorkspace,
			"Accounting & Control Workspace",
			"Posting, reconciliation, fiscal-period and reporting attention for Accounting and Controlling, with sensitive manual journals kept behind their explicit permission.",
			[
				Section("Needs Attention", "No accounting, reconciliation or configuration issue currently requires attention.", needsAttention),
				Section("GL Posting / Reversal Attention", "No recent General Ledger postings are visible to this account.", journals.Select(GeneralLedgerItem)),
				Section("Reconciliation", "No recent Inventory-to-GL reconciliation evidence is available.", reconciliations.Select(InventoryReconciliationItem)),
				Section("Period / Posting Status", "No current fiscal-period status is available to this account.", periodItems),
				Section("GRNI / COGS / Valuation Status", "No Inventory Accounting status is available.", accountingStatus),
				Section("Report Snapshots", "No recent financial-report snapshots are available.", snapshots.Take(MaximumItemsPerSection).Select(ReportSnapshotItem)),
				Section("Configuration Warnings", "No existing Finance configuration warning is currently derivable.", configurationWarnings)
			],
			[
				new("Current periods", periods.Count.ToString("N0"), periods.Count(value => value.Status == AccountingPeriodStatus.Open) + " open", "finance.reporting"),
				new("Recent reversals", journals.Count(value => value.EntryKind == FinanceJournalEntryKind.Reversal).ToString("N0"), "Visible General Ledger reversals", "finance.reporting"),
				new("Reconciliation differences", reconciliationDifferences.Length.ToString("N0"), "Inventory-to-GL differences", "finance.inventory-accounting"),
				new("Report snapshots", snapshots.Count.ToString("N0"), "Recent immutable reporting evidence", "finance.reporting")
			],
			quickActions,
			[]);
	}

	private async Task<CommercialRoleCenterSnapshot> GetReceivablesWorkspaceAsync(CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var openItemsTask = _receivables.SearchOpenItemsAsync(null, false, 1, MaximumItemsPerSection, cancellationToken);
		var recentPaymentsTask = _receivables.ListRecentPaymentsAsync(SourceItemLimit, cancellationToken);
		await Task.WhenAll(openItemsTask, recentPaymentsTask);

		var page = await openItemsTask;
		var openItems = page.Items;
		var invoices = openItems.Where(value => value.Kind == FinanceReceivableOpenItemKind.Invoice).ToArray();
		var overdue = invoices
			.Where(value => value.DueDate < today)
			.OrderByDescending(value => today.DayNumber - value.DueDate.DayNumber)
			.ThenBy(value => value.DueDate)
			.Select(value => ReceivableItem(value, today))
			.ToArray();
		var dueToday = invoices
			.Where(value => value.DueDate == today)
			.OrderByDescending(value => value.RemainingAmount)
			.Select(value => ReceivableItem(value, today))
			.ToArray();
		var unapplied = openItems
			.Where(value => value.Kind is FinanceReceivableOpenItemKind.Payment or FinanceReceivableOpenItemKind.CreditNote)
			.OrderBy(value => value.DocumentDate)
			.ThenBy(value => value.Id)
			.Select(value => ReceivableItem(value, today))
			.ToArray();
		var dunning = _receivables.CanViewDunning ? overdue : Array.Empty<CommercialRoleItem>();
		var recentReceipts = (await recentPaymentsTask).Select(value => ReceivablePaymentItem(value, today)).ToArray();

		var quickActions = new List<CommercialRoleQuickAction>
		{
			new("Open Receivables", "navigate", "finance.receivables"),
			new("Customer Statements", "navigate", "finance.receivables")
		};
		if (_receivables.CanViewDunning) quickActions.Add(new("Dunning", "navigate", "finance.receivables"));

		return Snapshot(
			CommercialRoleCenterKind.ReceivablesWorkspace,
			"Receivables Workspace",
			"Prioritized customer open items, overdue balances, allocations, receipts and dunning handoffs using the existing Accounts Receivable subledger.",
			[
				Section("Invoices / Open Items", "No customer open items require attention.", openItems.OrderBy(value => value.DueDate).ThenBy(value => value.Id).Select(value => ReceivableItem(value, today))),
				Section("Overdue Receivables", "No customer invoices are overdue.", overdue),
				Section("Due Today", "No customer invoices are due today.", dueToday),
				Section("Unapplied / Partially Applied Customer Payments", "No unapplied customer payments or credits require allocation.", unapplied),
				Section("Dunning Candidates", _receivables.CanViewDunning ? "No overdue receivables currently require dunning review." : "Dunning information is not available to this account.", dunning),
				Section("Recently Posted / Reversed Receipts", "No recent customer receipts are available.", recentReceipts)
			],
			[new CommercialRoleKpi("Open items", page.TotalCount.ToString("N0"), "Current Accounts Receivable open-item population", "finance.receivables")],
			quickActions,
			[]);
	}

	private async Task<CommercialRoleCenterSnapshot> GetPayablesWorkspaceAsync(CancellationToken cancellationToken)
	{
		Task<PageResult<FinanceSupplierDocument>> Query(FinancePayableDocumentStatus status) =>
			_payables.SearchDocumentsAsync(null, status, 1, SourceItemLimit, cancellationToken);

		var today = DateOnly.FromDateTime(DateTime.Today);
		var draftsTask = Query(FinancePayableDocumentStatus.Draft);
		var submittedTask = Query(FinancePayableDocumentStatus.PendingApproval);
		var approvedTask = Query(FinancePayableDocumentStatus.Approved);
		var openItemsTask = _payables.SearchOpenItemsAsync(null, false, 1, MaximumItemsPerSection, cancellationToken);
		await Task.WhenAll(draftsTask, submittedTask, approvedTask, openItemsTask);

		var drafts = (await draftsTask).Items;
		var submitted = (await submittedTask).Items;
		var approved = (await approvedTask).Items;
		var detailIds = drafts.Concat(submitted).Concat(approved).Select(value => value.Id).Distinct().Take(MaximumItemsPerSection).ToArray();
		var detailTasks = detailIds.Select(id => _payables.GetDocumentAsync(id, cancellationToken)).ToArray();
		var details = (await Task.WhenAll(detailTasks)).Where(value => value is not null).Select(value => value!).ToArray();
		var matchResults = details
			.Where(value => value.Lines.Any(line => line.MatchStatus != FinancePayableMatchStatus.NotRequired))
			.OrderByDescending(value => value.HasMatchExceptions)
			.ThenBy(value => value.DueDate)
			.Select(value => SupplierDocumentItem(value, today))
			.ToArray();
		var matchExceptions = details
			.Where(value => value.HasMatchExceptions && !value.MatchExceptionApproved)
			.OrderBy(value => value.DueDate)
			.Select(value => SupplierDocumentItem(
				value,
				today,
				_payables.CanApproveDocuments && _payables.CanApproveMatchExceptions && _payables.CanDecide(value.CreatedByUserId)
					? "Review / resolve exception"
					: "Await authorized review"))
			.ToArray();
		var openItems = (await openItemsTask).Items;
		var paymentReady = openItems
			.Where(value => value.Kind == FinancePayableOpenItemKind.Invoice && !value.IsVoided && value.RemainingAmount > 0m)
			.OrderBy(value => value.DueDate)
			.ThenBy(value => value.Id)
			.Select(value => PayableOpenItemItem(value, today, "Treasury handoff"))
			.ToArray();

		var quickActions = new List<CommercialRoleQuickAction>();
		if (_payables.CanCreateDocuments) quickActions.Add(new("New Supplier Invoice", "ap.new-invoice", "finance.payables"));
		quickActions.Add(new("Open Payables", "navigate", "finance.payables"));

		return Snapshot(
			CommercialRoleCenterKind.PayablesWorkspace,
			"Payables Workspace",
			"Supplier invoice processing, three-way match visibility, open items and read-only Treasury handoff without granting approval or payment authority.",
			[
				Section("Supplier Invoices in Draft", "No supplier invoice drafts require processing.", drafts.Where(value => value.Kind == FinancePayableDocumentKind.Invoice).Select(value => SupplierDocumentItem(value, today))),
				Section("Submitted / Waiting for Approval", "No supplier documents are waiting for approval.", submitted.Select(value => SupplierDocumentItem(value, today))),
				Section("PO / GR / Invoice Match Results", "No evaluated three-way match results require attention.", matchResults),
				Section("Match Exceptions", "No unresolved match exceptions require attention.", matchExceptions),
				Section("Approved but Unposted", "No approved supplier documents are waiting to be posted.", approved.Select(value => SupplierDocumentItem(value, today))),
				Section("Open Supplier Items", "No supplier open items require attention.", openItems.OrderBy(value => value.DueDate).ThenBy(value => value.Id).Select(value => PayableOpenItemItem(value, today))),
				Section("Payment-ready Items", "No supplier invoice open items are ready for Treasury handoff.", paymentReady)
			],
			[],
			quickActions,
			[]);
	}


	private async Task<CommercialRoleCenterSnapshot> GetManagementCockpitAsync(CancellationToken cancellationToken)
	{
		Task<PageResult<SalesOrder>> Sales(SalesOrderStatus status, int pageSize = 1) =>
			_salesOrders.SearchAsync(null, status, 1, pageSize, cancellationToken);
		Task<PageResult<PurchaseOrder>> Purchasing(PurchaseOrderStatus status) =>
			_purchaseOrders.SearchAsync(null, status, 1, 1, cancellationToken);

		var today = DateOnly.FromDateTime(DateTime.Today);
		var dashboardTask = _dashboard.GetAsync(cancellationToken);
		var workTask = _myWork.GetAsync(cancellationToken);
		var draftSalesTask = Sales(SalesOrderStatus.Draft);
		var pendingSalesTask = Sales(SalesOrderStatus.PendingApproval);
		var approvedSalesTask = Sales(SalesOrderStatus.Approved);
		var releasedSalesTask = Sales(SalesOrderStatus.Released, SourceItemLimit);
		var partialSalesTask = Sales(SalesOrderStatus.PartiallyShipped, SourceItemLimit);
		var pendingPurchaseTask = Purchasing(PurchaseOrderStatus.PendingApproval);
		var receivableOpenTask = _receivables.SearchOpenItemsAsync(null, false, 1, MaximumItemsPerSection, cancellationToken);
		var receivableAgingTask = _receivables.GetAgingAsync(today, cancellationToken);
		var payableOpenTask = _payables.SearchOpenItemsAsync(null, false, 1, MaximumItemsPerSection, cancellationToken);
		var cashTask = _banking.GetCashPositionAsync(cancellationToken);

		await Task.WhenAll(
			dashboardTask, workTask,
			draftSalesTask, pendingSalesTask, approvedSalesTask, releasedSalesTask, partialSalesTask,
			pendingPurchaseTask, receivableOpenTask, receivableAgingTask, payableOpenTask, cashTask);

		var dashboard = await dashboardTask;
		var salesMetrics = dashboard.Roles.Sales;
		var purchasingMetrics = dashboard.Roles.Purchasing;
		var draftSales = await draftSalesTask;
		var pendingSales = await pendingSalesTask;
		var approvedSales = await approvedSalesTask;
		var releasedSales = await releasedSalesTask;
		var partialSales = await partialSalesTask;
		var openSalesCount = draftSales.TotalCount + pendingSales.TotalCount + approvedSales.TotalCount + releasedSales.TotalCount + partialSales.TotalCount;
		var releasedSalesCount = releasedSales.TotalCount + partialSales.TotalCount;
		var pendingPurchaseCount = (await pendingPurchaseTask).TotalCount;
		var receivableOpen = await receivableOpenTask;
		var receivableAging = await receivableAgingTask;
		var overdueAccounts = receivableAging.Count(value =>
			value.Days1To30 != 0m || value.Days31To60 != 0m || value.Days61To90 != 0m || value.DaysOver90 != 0m);
		var payableOpen = await payableOpenTask;
		var dueHorizon = payableOpen.Items
			.Where(value => value.Kind == FinancePayableOpenItemKind.Invoice && !value.IsVoided && value.RemainingAmount > 0m && value.DueDate <= today.AddDays(7))
			.OrderBy(value => value.DueDate)
			.ThenBy(value => value.Id)
			.Select(value => PayableOpenItemItem(value, today, "Treasury handoff"))
			.ToArray();
		var overdueReceivables = receivableOpen.Items
			.Where(value => value.Kind == FinanceReceivableOpenItemKind.Invoice && value.DueDate < today)
			.OrderBy(value => value.DueDate)
			.ThenBy(value => value.Id)
			.Select(value => ReceivableItem(value, today))
			.ToArray();
		var releasedAndBackordered = releasedSales.Items
			.Concat(partialSales.Items)
			.OrderByDescending(value => value.Lines.Sum(line => line.BackorderedQuantity))
			.ThenBy(value => value.RequestedDeliveryDate)
			.Select(OrderItem)
			.ToArray();
		var work = await workTask;
		var exceptions = work.Sections
			.Single(value => value.Kind == MyWorkSectionKind.Exceptions)
			.Items
			.Take(MaximumItemsPerSection)
			.Select(FromMyWork)
			.ToArray();
		var cash = await cashTask;
		var pendingApprovals = pendingPurchaseCount + (salesMetrics?.PendingApprovals ?? pendingSales.TotalCount);

		var kpis = new List<CommercialRoleKpi>
		{
			new("Open sales orders", openSalesCount.ToString("N0"), $"Released / partially shipped: {releasedSalesCount:N0}", "sales.orders"),
			new("Pending approvals", pendingApprovals.ToString("N0"), $"Sales: {salesMetrics?.PendingApprovals ?? pendingSales.TotalCount:N0} · Purchase: {pendingPurchaseCount:N0}", "approvals"),
			new("AR open items", receivableOpen.TotalCount.ToString("N0"), $"Overdue customer/currency positions: {overdueAccounts:N0}", "finance.receivables"),
			new("AP open items", payableOpen.TotalCount.ToString("N0"), $"Due within 7 days shown: {dueHorizon.Length:N0}", "finance.payables")
		};
		if (salesMetrics is not null)
			kpis.Insert(0, new("Net sales this month", salesMetrics.NetSalesThisMonth.ToString("C2"), $"Backordered orders: {salesMetrics.BackorderedOrders:N0}", "sales.invoices"));
		if (dashboard.Inventory is not null)
			kpis.Add(new("Inventory value", dashboard.Inventory.Summary.TotalInventoryValue.ToString("C2"), $"Stock quantity: {dashboard.Inventory.Summary.TotalStockQuantity:N0}", "inventory.overview"));
		if (purchasingMetrics is not null)
			kpis.Add(new("Late purchase orders", purchasingMetrics.OverdueDeliveries.ToString("N0"), "Existing purchasing overdue-delivery metric", "purchasing.purchase-orders"));

		return Snapshot(
			CommercialRoleCenterKind.ManagementCockpit,
			"Management Cockpit",
			"Read-only operational and financial visibility using existing permission-filtered KPIs, subledgers and exception projections.",
			[
				Section("Released / Backordered Sales Orders", "No released sales orders currently require management visibility.", releasedAndBackordered),
				Section("Overdue Receivables", "No overdue customer invoices are visible.", overdueReceivables),
				Section("AP Due Horizon — Next 7 Days", "No supplier invoices in the visible open-item horizon are due within seven days.", dueHorizon),
				Section("Cash Position", "No active cash-position evidence is available.", cash.Select(CashPositionItem)),
				Section("Selected Operational Exceptions", "No operational exceptions are visible for the current permissions.", exceptions)
			],
			kpis,
			[
				new("Sales Orders", "navigate", "sales.orders"),
				new("Inventory", "navigate", "inventory.overview"),
				new("Receivables", "navigate", "finance.receivables"),
				new("Payables", "navigate", "finance.payables"),
				new("Cash Position", "navigate", "finance.banking"),
				new("Reports", "navigate", "reports.overview")
			],
			work.Failures);
	}

	private Task<CommercialRoleCenterSnapshot> GetAuditComplianceCenterAsync() =>
		Task.FromResult(Snapshot(
			CommercialRoleCenterKind.AuditComplianceCenter,
			"Audit & Compliance Center",
			"Read-only evidence and export entry points for audit, security, identities and reporting without operational mutation authority.",
			[],
			[],
			[
				new("Audit Log", "admin.audit-log", "administration"),
				new("Document / Entity History", "admin.audit-log", "administration"),
				new("Security Events", "admin.security", "administration"),
				new("Users — Read Only", "admin.users", "administration"),
				new("Roles — Read Only", "admin.roles", "administration"),
				new("Operational Reports", "navigate", "reports.overview"),
				new("Financial Reporting", "navigate", "finance.reporting")
			],
			[]));

	private Task<CommercialRoleCenterSnapshot> GetMasterDataWorkspaceAsync() =>
		Task.FromResult(Snapshot(
			CommercialRoleCenterKind.MasterDataWorkspace,
			"Master Data Workspace",
			"Controlled maintenance entry points for item, customer, supplier, warehouse, location and reference master data without operational posting rights.",
			[],
			[],
			[
				new("Items", "navigate", "inventory.items"),
				new("Customers", "navigate", "sales.customers"),
				new("Suppliers", "admin.suppliers", "administration"),
				new("Reference Master Data", "admin.master-data", "administration"),
				new("Warehouses & Locations", "admin.warehouses", "administration")
			],
			[]));

	private Task<CommercialRoleCenterSnapshot> GetApplicationAdministrationCenterAsync()
	{
		var quickActions = new List<CommercialRoleQuickAction>
		{
			new("Users", "admin.users", "administration"),
			new("Roles", "admin.roles", "administration"),
			new("User Sessions", "admin.sessions", "administration"),
			new("Security Center", "admin.security", "administration"),
			new("Company Settings", "admin.company", "administration"),
			new("Database", "admin.database", "administration"),
			new("Audit Log", "admin.audit-log", "administration")
		};
		if (_authorization.HasPermission(ApplicationPermission.ImportManage))
			quickActions.Add(new("Import", "admin.import", "administration"));

		return Task.FromResult(Snapshot(
			CommercialRoleCenterKind.ApplicationAdministrationCenter,
			"Application Administration Center",
			"Application, identity, security, settings and database administration without implicit Sales, Purchasing, Warehouse or Finance posting authority.",
			[],
			[],
			quickActions,
			[]));
	}

	private async Task<CommercialRoleCenterSnapshot> GetReceivingWorkspaceAsync(CancellationToken cancellationToken)
	{
		Task<PageResult<PurchaseOrder>> QueryOrder(PurchaseOrderStatus status) => _authorization.HasPermission(ApplicationPermission.PurchaseOrdersView)
			? _purchaseOrders.SearchAsync(null, status, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<PurchaseOrder>([], 1, SourceItemLimit, 0));

		var orderedTask = QueryOrder(PurchaseOrderStatus.Ordered);
		var partialTask = QueryOrder(PurchaseOrderStatus.PartiallyReceived);
		var receiptsTask = _authorization.HasPermission(ApplicationPermission.GoodsReceiptsView)
			? _goodsReceipts.ListRecentAsync(SourceItemLimit, cancellationToken)
			: Task.FromResult<IReadOnlyList<GoodsReceipt>>([]);
		var returnsTask = _authorization.HasPermission(ApplicationPermission.SupplierReturnsView)
			? _supplierReturns.SearchAsync(null, null, SupplierReturnStatus.Draft, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<SupplierReturnOverviewItem>([], 1, SourceItemLimit, 0));
		await Task.WhenAll(orderedTask, partialTask, receiptsTask, returnsTask);

		var receipts = await receiptsTask;
		var orderLookup = new Dictionary<long, PurchaseOrder>();
		if (_authorization.HasPermission(ApplicationPermission.PurchaseOrdersView))
		{
			var orderTasks = receipts.Select(value => value.PurchaseOrderId).Distinct()
				.Select(async id => (Id: id, Order: await _purchaseOrders.GetByIdAsync(id, cancellationToken))).ToArray();
			var orders = await Task.WhenAll(orderTasks);
			orderLookup = orders.Where(value => value.Order is not null).ToDictionary(value => value.Id, value => value.Order!);
		}

		return Snapshot(
			CommercialRoleCenterKind.ReceivingWorkspace,
			"Receiving Workspace",
			"Expected deliveries, partial receipts and supplier-return execution using the existing purchasing and goods-receipt workflows.",
			[
				Section("Expected / Ordered Purchase Orders", "No ordered purchase orders are waiting for receipt.", (await orderedTask).Items.Select(OrderItem)),
				Section("Partial Receipts", "No purchase orders have remaining quantities after a partial receipt.", (await partialTask).Items.Select(OrderItem)),
				Section("Draft Goods Receipts", "Goods receipt drafts are session-only in the existing receipt workflow and are not persisted as separate work items.", []),
				Section("Recently Posted Receipts", "No posted goods receipts are available.", receipts.Select(value => ReceiptItem(value, orderLookup.GetValueOrDefault(value.PurchaseOrderId)))),
				Section("Supplier Returns Requiring Execution", "No supplier-return drafts require execution.", (await returnsTask).Items.Select(SupplierReturnItem))
			],
			[],
			[
				new("Receive Goods", "receiving.receive", "purchasing.goods-receipts"),
				new("Open Purchase Order", "receiving.open-order", "purchasing.purchase-orders"),
				new("Create Supplier Return", "receiving.new-supplier-return", "purchasing.supplier-returns")
			],
			[]);
	}

	private async Task<CommercialRoleCenterSnapshot> GetFulfillmentWorkspaceAsync(CancellationToken cancellationToken)
	{
		Task<PageResult<SalesOrder>> QueryOrder(SalesOrderStatus status) => _authorization.HasPermission(ApplicationPermission.SalesOrdersView)
			? _salesOrders.SearchAsync(null, status, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<SalesOrder>([], 1, SourceItemLimit, 0));
		Task<PageResult<Shipment>> QueryShipment(ShipmentStatus status) => _authorization.HasPermission(ApplicationPermission.ShipmentsView)
			? _shipments.SearchAsync(null, status, 1, SourceItemLimit * 3, cancellationToken)
			: Task.FromResult(new PageResult<Shipment>([], 1, SourceItemLimit * 3, 0));

		var workTask = _myWork.GetAsync(cancellationToken);
		var releasedTask = QueryOrder(SalesOrderStatus.Released);
		var partialTask = QueryOrder(SalesOrderStatus.PartiallyShipped);
		var draftsTask = QueryShipment(ShipmentStatus.Draft);
		var cancelledTask = QueryShipment(ShipmentStatus.Cancelled);
		var returnsTask = _authorization.HasPermission(ApplicationPermission.CustomerReturnsView)
			? _shipments.SearchCustomerReturnsAsync(null, CustomerReturnStatus.Draft, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<CustomerReturn>([], 1, SourceItemLimit, 0));
		await Task.WhenAll(workTask, releasedTask, partialTask, draftsTask, cancelledTask, returnsTask);

		var work = await workTask;
		var drafts = (await draftsTask).Items;
		var awaitingPicking = drafts.Where(value => value.PackingStatus == ShipmentPackingStatus.NotStarted).Take(SourceItemLimit).Select(ShipmentItem);
		var packing = drafts.Where(value => value.PackingStatus == ShipmentPackingStatus.Picking).Take(SourceItemLimit).Select(ShipmentItem);
		var readyToPost = drafts.Where(value => value.PackingStatus == ShipmentPackingStatus.Packed).Take(SourceItemLimit).Select(ShipmentItem);
		var exceptions = WorkItems(work, MyWorkSectionKind.Exceptions, MyWorkItemKind.SalesOrder)
			.Concat((await cancelledTask).Items.Select(ShipmentItem)).Take(MaximumItemsPerSection).ToArray();

		return Snapshot(
			CommercialRoleCenterKind.FulfillmentWorkspace,
			"Fulfillment Workspace",
			"Released orders, picking, packing, shipment posting and customer returns projected from existing Sales and Shipment states.",
			[
				Section("Released Orders Ready for Fulfillment", "No released sales orders are ready for fulfillment.", (await releasedTask).Items.Concat((await partialTask).Items).Select(OrderItem)),
				Section("Awaiting Picking", "No draft shipments are waiting to start picking.", awaitingPicking),
				Section("Packing in Progress", "No shipment is currently in picking/packing.", packing),
				Section("Draft Shipments Ready to Post", "No packed draft shipments are ready to post.", readyToPost),
				Section("Shipment / Fulfillment Exceptions", "No existing shipment or backorder state requires attention.", exceptions),
				Section("Customer Returns Requiring Processing", "No customer-return drafts require processing.", (await returnsTask).Items.Select(CustomerReturnItem))
			],
			[],
			[
				new("Open Released Orders", "navigate", "sales.orders"),
				new("Open Shipping", "navigate", "sales.shipping")
			],
			work.Failures);
	}

	private async Task<CommercialRoleCenterSnapshot> GetInventoryControlWorkspaceAsync(CancellationToken cancellationToken)
	{
		Task<PageResult<InventoryCountOverviewItem>> QueryCount(InventoryCountStatus status) => _authorization.HasPermission(ApplicationPermission.InventoryCountsView)
			? _inventoryCounts.SearchAsync(null, status, null, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<InventoryCountOverviewItem>([], 1, SourceItemLimit, 0));
		var countingTask = QueryCount(InventoryCountStatus.Counting);
		var reviewTask = QueryCount(InventoryCountStatus.Review);
		var transfersTask = _authorization.HasPermission(ApplicationPermission.StockTransfersView)
			? _stockTransfers.SearchAsync(null, StockTransferStatus.Draft, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<StockTransferOverviewItem>([], 1, SourceItemLimit, 0));
		var issuesTask = _authorization.HasPermission(ApplicationPermission.MaterialIssuesView)
			? _materialIssues.SearchAsync(null, MaterialIssueStatus.Draft, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<MaterialIssueOverviewItem>([], 1, SourceItemLimit, 0));
		var returnsTask = _authorization.HasPermission(ApplicationPermission.MaterialReturnsView)
			? _materialReturns.SearchAsync(null, MaterialReturnStatus.Draft, 1, SourceItemLimit, cancellationToken)
			: Task.FromResult(new PageResult<MaterialReturnOverviewItem>([], 1, SourceItemLimit, 0));
		await Task.WhenAll(countingTask, reviewTask, transfersTask, issuesTask, returnsTask);

		var review = (await reviewTask).Items;
		return Snapshot(
			CommercialRoleCenterKind.InventoryControlWorkspace,
			"Inventory Control Workspace",
			"Counts, variances, transfers and controlled material corrections without introducing a second inventory workflow.",
			[
				Section("Counts in Progress", "No inventory counts are currently in progress.", (await countingTask).Items.Select(CountItem)),
				Section("Counts Ready for Review / Posting", "No inventory counts are ready for review or posting.", review.Select(CountItem)),
				Section("Count Variances", "No reviewed inventory counts contain variances.", review.Where(value => value.DifferenceLineCount > 0).Select(CountItem)),
				Section("Open Stock Transfers", "No draft stock transfers require action.", (await transfersTask).Items.Select(TransferItem)),
				Section("Material Issues Requiring Action", "No material-issue drafts require action.", (await issuesTask).Items.Select(MaterialIssueItem)),
				Section("Material Returns Requiring Action", "No material-return drafts require action.", (await returnsTask).Items.Select(MaterialReturnItem))
			],
			[],
			[
				new("Start Inventory Count", "inventory.new-count", "warehouse.inventory-counts"),
				new("Transfer Stock", "inventory.new-transfer", "warehouse.transfers"),
				new("New Material Issue", "inventory.new-issue", "warehouse.material-issues"),
				new("New Material Return", "inventory.new-return", "warehouse.material-returns")
			],
			[]);
	}

	private async Task<CommercialRoleCenterSnapshot> GetApprovalInboxAsync(CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		var items = new List<CommercialRoleItem>();

		if (_authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove))
		{
			var page = await _purchaseApprovals.SearchAsync(new PurchaseOrderApprovalFilter(null, null, null, null, null), 1, SourceItemLimit, cancellationToken);
			items.AddRange(page.Page.Items.Select(value =>
			{
				var canDecide = _purchaseApprovals.CanDecide(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.PurchaseOrderApproval, value.Id, value.Version, value.OrderNumber, "Purchase Order", value.SupplierName, "Pending Approval", value.TotalAmount, value.SubmittedAtUtc, value.ExpectedDeliveryDate, DaysSince(now, value.SubmittedAtUtc), "approvals.purchase", value.CreatedByUserId, canDecide, canDecide, value.CreatorDisplayName);
			}));
		}

		if (_authorization.HasPermission(ApplicationPermission.SalesOrdersApprove))
		{
			var page = await _salesOrders.SearchPendingApprovalsAsync(1, SourceItemLimit, cancellationToken);
			items.AddRange(page.Items.Select(value =>
			{
				var submitted = value.SubmittedAtUtc ?? now;
				var canDecide = _salesOrders.CanDecide(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.SalesOrderApproval, value.Id, value.Version, value.OrderNumber, "Sales Order", value.CustomerName, "Pending Approval", value.GrossAmount, value.SubmittedAtUtc, value.RequestedDeliveryDate, DaysSince(now, submitted), "approvals.sales", value.CreatedByUserId, canDecide, canDecide, UserLabel(value.CreatedByUserId));
			}));
		}

		if (_authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesApprove))
		{
			var page = await _payables.SearchPendingApprovalDocumentsAsync(1, SourceItemLimit, cancellationToken);
			items.AddRange(page.Items.Select(value =>
			{
				var submitted = value.SubmittedAtUtc ?? value.CreatedAtUtc;
				var canDecide = _payables.CanDecide(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.SupplierInvoice, value.Id, value.Version, value.SupplierDocumentNumber, value.Kind == FinancePayableDocumentKind.Invoice ? "Supplier Invoice" : "Supplier Credit Note", value.SupplierName, "Pending Approval", value.GrossAmount, submitted, value.DueDate.ToDateTime(TimeOnly.MinValue), DaysSince(now, submitted), "finance.payables", value.CreatedByUserId, canDecide, canDecide, UserLabel(value.CreatedByUserId));
			}));
		}

		if (_authorization.HasPermission(ApplicationPermission.FinancePaymentProposalsApprove))
		{
			var paymentRuns = await _banking.SearchPendingApprovalPaymentRunsAsync(SourceItemLimit, cancellationToken);
			items.AddRange(paymentRuns.Select(value =>
			{
				var canApprove = _banking.CanApprovePaymentRun(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.PaymentProposal, value.Id, value.Version, value.Description, "Payment Proposal", value.Currency.ToString(), "Draft / Awaiting Approval", value.Lines.Sum(line => line.Amount), value.CreatedAtUtc, value.PaymentDate.ToDateTime(TimeOnly.MinValue), DaysSince(now, value.CreatedAtUtc), "finance.banking", value.CreatedByUserId, canApprove, false, UserLabel(value.CreatedByUserId));
			}));
		}

		var ordered = items.OrderByDescending(value => value.AgeDays ?? 0).ThenBy(value => value.SubmittedAtUtc).Take(MaximumItemsPerSection).ToArray();
		return Snapshot(
			CommercialRoleCenterKind.ApprovalInbox,
			"Approval Inbox",
			"One permission-aware queue for existing purchase, sales, supplier-invoice and payment-proposal approvals.",
			[Section("Pending Approvals", "No approvals currently require your authority.", ordered)],
			[new CommercialRoleKpi("Pending", ordered.Length.ToString("N0"), "Across the approval types permitted to your account")],
			[],
			[]);
	}

	private bool HasApprovalPermission() => _authorization.HasAnyPermission(
		ApplicationPermission.PurchaseOrdersApprove,
		ApplicationPermission.SalesOrdersApprove,
		ApplicationPermission.FinanceSupplierInvoicesApprove,
		ApplicationPermission.FinancePaymentProposalsApprove);

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user
		? user
		: throw new UnauthorizedAccessException("An active signed-in user is required.");

	private static CommercialRoleSection Section(string title, string emptyText, IEnumerable<CommercialRoleItem> items) =>
		new(title, emptyText, items.Take(MaximumItemsPerSection).ToArray());

	private static CommercialRoleCenterSnapshot Snapshot(
		CommercialRoleCenterKind kind,
		string title,
		string subtitle,
		IReadOnlyList<CommercialRoleSection> sections,
		IReadOnlyList<CommercialRoleKpi> kpis,
		IReadOnlyList<CommercialRoleQuickAction> quickActions,
		IReadOnlyList<MyWorkProviderFailure> failures) =>
		new(kind, title, subtitle, sections, kpis, quickActions, failures);

	private static IReadOnlyList<CommercialRoleItem> WorkItems(MyWorkSnapshot snapshot, MyWorkSectionKind section, MyWorkItemKind kind, long? owner = null) =>
		snapshot.Sections.Single(value => value.Kind == section).Items
			.Where(value => value.Kind == kind && (owner is null || value.SourceUserId == owner))
			.Take(MaximumItemsPerSection)
			.Select(FromMyWork)
			.ToArray();

	private static CommercialRoleItem FromMyWork(MyWorkItem value) =>
		new(value.Kind switch
		{
			MyWorkItemKind.SalesOrderApproval => CommercialRoleItemKind.SalesOrderApproval,
			MyWorkItemKind.SalesOrder => CommercialRoleItemKind.SalesOrder,
			MyWorkItemKind.PurchaseOrderApproval => CommercialRoleItemKind.PurchaseOrderApproval,
			MyWorkItemKind.PurchaseOrder => CommercialRoleItemKind.PurchaseOrder,
			MyWorkItemKind.Shipment => CommercialRoleItemKind.Shipment,
			MyWorkItemKind.InventoryCount => CommercialRoleItemKind.InventoryCount,
			_ => CommercialRoleItemKind.SalesOrder
		}, value.EntityId, 0, value.DisplayNumber, value.Title, value.Context, value.Status, value.AmountOrQuantity, null, value.DueAt, value.AgeDays, value.RouteId, value.SourceUserId);

	private static CommercialRoleItem QuoteItem(SalesQuote value) =>
		new(CommercialRoleItemKind.SalesQuote, value.Id, value.Version, value.QuoteNumber, "Sales Quote", value.CustomerName, value.Status.ToString(), value.GrossAmount, value.CreatedAtUtc, value.ValidUntil, null, "sales.quotes", value.CreatedByUserId);

	private static CommercialRoleItem OrderItem(SalesOrder value) =>
		new(CommercialRoleItemKind.SalesOrder, value.Id, value.Version, value.OrderNumber, "Sales Order", value.CustomerName, value.Status.ToString(), value.GrossAmount, value.SubmittedAtUtc, value.RequestedDeliveryDate, null, "sales.orders", value.CreatedByUserId);

	private static CommercialRoleItem OrderItem(PurchaseOrder value) =>
		new(CommercialRoleItemKind.PurchaseOrder, value.Id, value.Version, value.OrderNumber, "Purchase Order", value.SupplierName, value.StatusDisplayName, value.Lines.Sum(line => line.Quantity * line.UnitPrice), value.SubmittedAtUtc, value.ExpectedDeliveryDate, value.SubmittedAtUtc is null ? null : DaysSince(DateTime.UtcNow, value.SubmittedAtUtc.Value), "purchasing.purchase-orders", value.CreatedByUserId);


	private static CommercialRoleItem ReceiptItem(GoodsReceipt value, PurchaseOrder? order) =>
		new(CommercialRoleItemKind.GoodsReceipt, value.PurchaseOrderId, value.Version, value.ReceiptNumber, "Goods Receipt", order?.OrderNumber ?? $"Purchase order #{value.PurchaseOrderId:N0}", value.IsReversed ? "Reversed" : "Posted", null, value.ReceiptDate, null, null, "purchasing.goods-receipts", value.ReceivedByUserId);

	private static CommercialRoleItem ShipmentItem(Shipment value) =>
		new(CommercialRoleItemKind.Shipment, value.Id, value.Version, value.ShipmentNumber, "Shipment", value.SalesOrderNumber, value.Status == ShipmentStatus.Draft ? value.PackingStatus.ToString() : value.Status.ToString(), value.Lines.Sum(line => line.Quantity), value.PackedAtUtc, value.ShipmentDate, null, "sales.shipping", value.CreatedByUserId);

	private static CommercialRoleItem CustomerReturnItem(CustomerReturn value) =>
		new(CommercialRoleItemKind.CustomerReturn, value.Id, value.Version, value.ReturnNumber, "Customer Return", $"Shipment #{value.ShipmentId:N0}", value.Status.ToString(), value.Lines.Sum(line => line.Quantity), value.PostedAtUtc, value.ReturnDate, null, "sales.shipping", value.CreatedByUserId);

	private static CommercialRoleItem CountItem(InventoryCountOverviewItem value) =>
		new(CommercialRoleItemKind.InventoryCount, value.Id, value.Version, value.CountNumber, "Inventory Count", value.WarehouseName, value.StatusDisplayName, value.DifferenceLineCount, value.StartedAtUtc ?? value.CreatedAtUtc, null, null, "warehouse.inventory-counts");

	private static CommercialRoleItem TransferItem(StockTransferOverviewItem value) =>
		new(CommercialRoleItemKind.StockTransfer, value.Id, value.Version, value.TransferNumber, "Stock Transfer", $"{value.SourceWarehouseName} → {value.DestinationWarehouseName}", value.StatusDisplayName, value.LineCount, value.TransferDate, null, null, "warehouse.transfers");

	private static CommercialRoleItem MaterialIssueItem(MaterialIssueOverviewItem value) =>
		new(CommercialRoleItemKind.MaterialIssue, value.Id, value.Version, value.IssueNumber, "Material Issue", value.Recipient, value.StatusDisplayName, value.LineCount, value.IssueDate, null, null, "warehouse.material-issues");

	private static CommercialRoleItem MaterialReturnItem(MaterialReturnOverviewItem value) =>
		new(CommercialRoleItemKind.MaterialReturn, value.Id, value.Version, value.ReturnNumber, "Material Return", value.RecipientOrSource, value.StatusDisplayName, value.LineCount, value.ReturnDate, null, null, "warehouse.material-returns");

	private static CommercialRoleItem SupplierReturnItem(SupplierReturnOverviewItem value) =>
		new(CommercialRoleItemKind.SupplierReturn, value.Id, value.Version, value.ReturnNumber, "Supplier Return", value.SupplierName, value.StatusDisplayName, value.LineCount, value.ReturnDate, null, null, "purchasing.supplier-returns");


	private CommercialRoleItem ReceivableItem(FinanceReceivableOpenItem value, DateOnly today)
	{
		var allocationState = value.Kind switch
		{
			FinanceReceivableOpenItemKind.Payment or FinanceReceivableOpenItemKind.CreditNote when value.SettlementStatus == FinanceReceivableSettlementStatus.Open => "Unapplied",
			FinanceReceivableOpenItemKind.Payment or FinanceReceivableOpenItemKind.CreditNote when value.SettlementStatus == FinanceReceivableSettlementStatus.PartiallySettled => "Partially applied",
			_ => value.SettlementStatus.ToString()
		};
		var nextAction = value.Kind switch
		{
			FinanceReceivableOpenItemKind.Invoice when value.DueDate < today && _receivables.CanManageDunning => "Review / dunning",
			FinanceReceivableOpenItemKind.Payment or FinanceReceivableOpenItemKind.CreditNote when _receivables.CanPostPayments => "Allocate",
			_ => "Open item"
		};
		var title = value.Kind switch
		{
			FinanceReceivableOpenItemKind.Invoice => "Customer Invoice",
			FinanceReceivableOpenItemKind.CreditNote => "Customer Credit",
			_ => "Customer Payment"
		};
		return new CommercialRoleItem(
			CommercialRoleItemKind.ReceivableOpenItem,
			value.Id,
			value.Version,
			value.SourceReference ?? value.SourceId,
			title,
			value.CustomerName,
			value.SettlementStatus.ToString(),
			value.RemainingAmount,
			value.CreatedAtUtc,
			value.DueDate.ToDateTime(TimeOnly.MinValue),
			AgeFromDueDate(today, value.DueDate),
			"finance.receivables",
			value.CreatedByUserId,
			Currency: value.Currency.Value,
			StateDetail: allocationState,
			NextAction: nextAction);
	}

	private static CommercialRoleItem ReceivablePaymentItem(FinanceReceivablePayment value, DateOnly today) =>
		new(
			CommercialRoleItemKind.ReceivableOpenItem,
			value.OpenItemId,
			value.Version,
			value.Reference ?? $"RCPT-{value.Id:N0}",
			"Customer Receipt",
			$"Customer #{value.CustomerId:N0}",
			value.IsReversed ? "Reversed" : "Posted",
			value.Amount,
			value.ReversedAtUtc ?? value.CreatedAtUtc,
			value.PaymentDate.ToDateTime(TimeOnly.MinValue),
			AgeFromDueDate(today, value.PaymentDate),
			"finance.receivables",
			value.CreatedByUserId,
			Currency: value.Currency.Value,
			StateDetail: value.IsReversed ? "Receipt reversed" : "Receipt posted",
			NextAction: "Open item");

	private CommercialRoleItem SupplierDocumentItem(FinanceSupplierDocument value, DateOnly today, string? nextAction = null)
	{
		var action = nextAction ?? value.Status switch
		{
			FinancePayableDocumentStatus.Draft when _payables.CanSubmitDocuments => "Edit / submit",
			FinancePayableDocumentStatus.PendingApproval when _payables.CanApproveDocuments && _payables.CanDecide(value.CreatedByUserId) => "Review",
			FinancePayableDocumentStatus.PendingApproval => "Await approval",
			FinancePayableDocumentStatus.Approved when _payables.CanPostDocuments => "Post",
			FinancePayableDocumentStatus.Approved => "Await posting",
			_ => "Open"
		};
		return new CommercialRoleItem(
			CommercialRoleItemKind.SupplierInvoice,
			value.Id,
			value.Version,
			value.SupplierDocumentNumber,
			value.Kind == FinancePayableDocumentKind.Invoice ? "Supplier Invoice" : "Supplier Credit Note",
			value.SupplierName,
			value.Status.ToString(),
			value.GrossAmount,
			value.SubmittedAtUtc ?? value.CreatedAtUtc,
			value.DueDate.ToDateTime(TimeOnly.MinValue),
			AgeFromDueDate(today, value.DueDate),
			"finance.payables",
			value.CreatedByUserId,
			Requester: UserLabel(value.CreatedByUserId),
			Currency: value.Currency.Value,
			StateDetail: MatchState(value),
			NextAction: action);
	}

	private static CommercialRoleItem PayableOpenItemItem(FinancePayableOpenItem value, DateOnly today, string? nextAction = null)
	{
		var title = value.Kind switch
		{
			FinancePayableOpenItemKind.Invoice => "Supplier Invoice",
			FinancePayableOpenItemKind.CreditNote => "Supplier Credit",
			_ => "Supplier Payment"
		};
		return new CommercialRoleItem(
			CommercialRoleItemKind.PayableOpenItem,
			value.Id,
			value.Version,
			value.SourceReference ?? value.SourceId,
			title,
			value.SupplierName,
			value.SettlementStatus.ToString(),
			value.RemainingAmount,
			value.CreatedAtUtc,
			value.DueDate.ToDateTime(TimeOnly.MinValue),
			AgeFromDueDate(today, value.DueDate),
			"finance.payables",
			value.CreatedByUserId,
			Currency: value.Currency.Value,
			StateDetail: value.SettlementStatus.ToString(),
			NextAction: nextAction ?? "Open item");
	}

	private static string MatchState(FinanceSupplierDocument value)
	{
		if (value.Lines.Count == 0) return "Not evaluated";
		if (value.HasMatchExceptions) return value.MatchExceptionApproved ? "Exception approved" : "Exception";
		if (value.Lines.Any(line => line.MatchStatus == FinancePayableMatchStatus.Matched)) return "Matched";
		return "Not required";
	}


	private static CommercialRoleItem BankStatementItem(FinanceBankStatement value, int unreconciledCount) =>
		new(
			CommercialRoleItemKind.BankStatement,
			value.Id,
			0,
			value.StatementReference,
			"Bank Statement",
			$"{value.FromDate:d} – {value.ToDate:d}",
			unreconciledCount > 0 ? "Unprocessed" : "Processed",
			value.ClosingBalance,
			value.ImportedAtUtc,
			value.ToDate.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.banking",
			value.ImportedByUserId,
			Currency: value.Currency.Value,
			StateDetail: unreconciledCount > 0 ? $"{unreconciledCount:N0} unreconciled lines" : "Fully reconciled",
			NextAction: unreconciledCount > 0 ? "Reconcile" : "Review");

	private static CommercialRoleItem BankStatementLineItem(FinanceBankStatementLine value, DateOnly today) =>
		new(
			CommercialRoleItemKind.BankStatementLine,
			value.Id,
			0,
			value.ExternalId ?? value.Reference ?? $"Line {value.LineNumber:N0}",
			"Bank Statement Line",
			value.CounterpartyName,
			value.IsReconciled ? "Reconciled" : "Open",
			value.Amount,
			value.BookingDate.ToDateTime(TimeOnly.MinValue),
			value.ValueDate?.ToDateTime(TimeOnly.MinValue),
			AgeFromDueDate(today, value.BookingDate),
			"finance.banking",
			Currency: value.Currency.Value,
			StateDetail: value.Reference,
			NextAction: value.IsReconciled ? "Review" : "Reconcile");

	private static CommercialRoleItem CashPositionItem(FinanceCashPosition value) =>
		new(
			CommercialRoleItemKind.CashPosition,
			value.BankAccountId,
			0,
			value.BankAccountName,
			"Cash Position",
			value.StatementDate is null ? "No bank statement date" : $"Statement {value.StatementDate:yyyy-MM-dd}",
			value.Difference == 0m ? "Reconciled" : "Difference",
			value.StatementBalance,
			null,
			value.StatementDate?.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.banking",
			Currency: value.Currency.Value,
			StateDetail: $"GL {value.GeneralLedgerBalance:N2} · Difference {value.Difference:N2}",
			NextAction: value.Difference == 0m ? "Review" : "Reconcile");

	private CommercialRoleItem TreasuryPaymentRunItem(FinancePaymentRun value)
	{
		var executed = value.Lines.Count(line => line.Status == FinancePaymentRunLineStatus.Executed);
		var nextAction = value.Status switch
		{
			FinancePaymentRunStatus.Draft when _banking.CanApprovePaymentRun(value.CreatedByUserId) => "Independent approval available",
			FinancePaymentRunStatus.Draft when value.CreatedByUserId == _authorization.CurrentUser?.Id => "Await independent approval",
			FinancePaymentRunStatus.Draft => "Await approval",
			FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted when _banking.CanExecutePaymentRuns => "Execute payment",
			FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted => "Await execution authority",
			_ => "Review"
		};
		return new CommercialRoleItem(
			CommercialRoleItemKind.PaymentProposal,
			value.Id,
			value.Version,
			$"PAY-{value.Id:N0}",
			"Payment Proposal",
			value.Description,
			value.Status.ToString(),
			value.Lines.Sum(line => line.Amount),
			value.CreatedAtUtc,
			value.PaymentDate.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.banking",
			value.CreatedByUserId,
			Requester: UserLabel(value.CreatedByUserId),
			Currency: value.Currency.Value,
			StateDetail: $"{executed:N0} of {value.Lines.Count:N0} lines executed",
			NextAction: nextAction);
	}

	private CommercialRoleItem GeneralLedgerItem(FinanceJournalEntrySummary value) =>
		new(
			CommercialRoleItemKind.GeneralLedgerEntry,
			value.Id,
			0,
			value.EntryNumber,
			value.EntryKind == FinanceJournalEntryKind.Reversal ? "General Ledger Reversal" : "General Ledger Posting",
			$"{value.SourceType} / {value.SourceEvent}",
			value.EntryKind.ToString(),
			null,
			value.PostedAtUtc,
			value.PostingDate.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.reporting",
			Currency: value.ReportingCurrency.Value,
			StateDetail: value.SourceReference ?? value.SourceId,
			NextAction: value.EntryKind == FinanceJournalEntryKind.Reversal ? "Review reversal" : _generalLedger.CanReverse ? "Review / reverse if required" : "Review");

	private static CommercialRoleItem InventoryReconciliationItem(FinanceInventoryReconciliationRun value) =>
		new(
			CommercialRoleItemKind.InventoryReconciliation,
			value.Id,
			0,
			$"REC-{value.Id:N0}",
			"Inventory-to-GL Reconciliation",
			$"As of {value.AsOfDate:yyyy-MM-dd}",
			value.Difference == 0m ? "Balanced" : "Difference",
			value.Difference,
			value.CreatedAtUtc,
			value.AsOfDate.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.inventory-accounting",
			value.CreatedByUserId,
			Currency: value.ReportingCurrency.Value,
			StateDetail: $"Valuation {value.ValuationAmount:N2} · GL {value.GeneralLedgerAmount:N2}",
			NextAction: value.Difference == 0m ? "Review" : "Investigate");

	private static CommercialRoleItem InventoryValuationItem(FinanceInventoryValuationSummary value) =>
		new(
			CommercialRoleItemKind.InventoryValuation,
			value.ItemId,
			0,
			$"ITEM-{value.ItemId:N0}",
			"Inventory Valuation",
			$"{value.Quantity:N0} units",
			"Valued",
			value.TransactionValue,
			null,
			null,
			null,
			"finance.inventory-accounting",
			Currency: value.Currency.Value,
			StateDetail: "FIFO valuation / GRNI / COGS evidence",
			NextAction: "Open Inventory Accounting");

	private static CommercialRoleItem PeriodStatusItem(AccountingPeriod value) =>
		new(
			CommercialRoleItemKind.FinanceStatus,
			0,
			0,
			value.Code,
			"Fiscal Period",
			$"{value.StartDate:yyyy-MM-dd} – {value.EndDate:yyyy-MM-dd}",
			value.Status.ToString(),
			null,
			null,
			value.EndDate.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.reporting",
			StateDetail: $"Calendar {value.FiscalCalendarId:D}",
			NextAction: value.Status == AccountingPeriodStatus.Open ? "Posting period open" : "Period closed");

	private static CommercialRoleItem ReportSnapshotItem(FinanceReportSnapshot value) =>
		new(
			CommercialRoleItemKind.ReportSnapshot,
			value.Id,
			0,
			$"SNAP-{value.Id:N0}",
			value.Kind.ToString(),
			value.AccountingBookId.ToString("D"),
			"Snapshot",
			null,
			value.CreatedAtUtc,
			value.AsOfDate?.ToDateTime(TimeOnly.MinValue) ?? value.ToDate?.ToDateTime(TimeOnly.MinValue),
			null,
			"finance.reporting",
			value.CreatedByUserId,
			StateDetail: value.ContentHash,
			NextAction: "Open Financial Reporting");

	private static CommercialRoleItem FinanceStatusItem(string displayNumber, string title, string? context, string status, string route, string nextAction) =>
		new(
			CommercialRoleItemKind.FinanceStatus,
			0,
			0,
			displayNumber,
			title,
			context,
			status,
			null,
			null,
			null,
			null,
			route,
			NextAction: nextAction);

	private static int? AgeFromDueDate(DateOnly today, DateOnly dueDate) =>
		dueDate <= today ? today.DayNumber - dueDate.DayNumber : null;

	private static int DaysSince(DateTime nowUtc, DateTime timestampUtc) => Math.Max(0, (int)(nowUtc - timestampUtc).TotalDays);
	private static string UserLabel(long? userId) => userId is null ? "Unknown" : $"User #{userId.Value:N0}";
}
