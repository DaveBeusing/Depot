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
	private readonly FinanceAccountsPayableService _payables;
	private readonly FinanceBankingService _banking;

	public CommercialRoleCenterService(
		IAuthorizationService authorization,
		MyWorkService myWork,
		DashboardService dashboard,
		SalesQuoteService salesQuotes,
		SalesOrderService salesOrders,
		PurchaseOrderService purchaseOrders,
		PurchaseOrderApprovalService purchaseApprovals,
		SupplierReturnService supplierReturns,
		FinanceAccountsPayableService payables,
		FinanceBankingService banking)
	{
		_authorization = authorization;
		_myWork = myWork;
		_dashboard = dashboard;
		_salesQuotes = salesQuotes;
		_salesOrders = salesOrders;
		_purchaseOrders = purchaseOrders;
		_purchaseApprovals = purchaseApprovals;
		_supplierReturns = supplierReturns;
		_payables = payables;
		_banking = banking;
	}

	public bool CanAccess(CommercialRoleCenterKind kind) => kind switch
	{
		CommercialRoleCenterKind.SalesWorkspace => _authorization.HasAnyPermission(ApplicationPermission.CustomersView, ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesOrdersView),
		CommercialRoleCenterKind.SalesControlCenter => _authorization.HasAnyPermission(ApplicationPermission.SalesOrdersApprove, ApplicationPermission.SalesPricingManage, ApplicationPermission.ShipmentsView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView),
		CommercialRoleCenterKind.BuyerWorkbench => _authorization.HasAnyPermission(ApplicationPermission.PurchaseOrdersView, ApplicationPermission.SuppliersView, ApplicationPermission.SupplierReturnsView),
		CommercialRoleCenterKind.ApprovalInbox => HasApprovalPermission(),
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
		await Task.WhenAll(workTask, metricsTask);
		var work = await workTask;
		var metrics = (await metricsTask).Roles.Sales;
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
				return new CommercialRoleItem(CommercialRoleItemKind.PurchaseOrderApproval, value.Id, value.Version, value.OrderNumber, "Purchase Order", value.SupplierName, "Pending Approval", value.TotalAmount, value.SubmittedAtUtc, value.ExpectedDeliveryDate, DaysSince(now, value.SubmittedAtUtc), "approvals.purchase", value.CreatedByUserId, canDecide, canDecide);
			}));
		}

		if (_authorization.HasPermission(ApplicationPermission.SalesOrdersApprove))
		{
			var page = await _salesOrders.SearchPendingApprovalsAsync(1, SourceItemLimit, cancellationToken);
			items.AddRange(page.Items.Select(value =>
			{
				var submitted = value.SubmittedAtUtc ?? now;
				var canDecide = _salesOrders.CanDecide(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.SalesOrderApproval, value.Id, value.Version, value.OrderNumber, "Sales Order", value.CustomerName, "Pending Approval", value.GrossAmount, value.SubmittedAtUtc, value.RequestedDeliveryDate, DaysSince(now, submitted), "approvals.sales", value.CreatedByUserId, canDecide, canDecide);
			}));
		}

		if (_authorization.HasPermission(ApplicationPermission.FinanceSupplierInvoicesApprove))
		{
			var page = await _payables.SearchPendingApprovalDocumentsAsync(1, SourceItemLimit, cancellationToken);
			items.AddRange(page.Items.Select(value =>
			{
				var submitted = value.SubmittedAtUtc ?? value.CreatedAtUtc;
				var canDecide = _payables.CanDecide(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.SupplierInvoice, value.Id, value.Version, value.SupplierDocumentNumber, value.Kind == FinancePayableDocumentKind.Invoice ? "Supplier Invoice" : "Supplier Credit Note", value.SupplierName, "Pending Approval", value.GrossAmount, submitted, value.DueDate.ToDateTime(TimeOnly.MinValue), DaysSince(now, submitted), "finance.payables", value.CreatedByUserId, canDecide, canDecide);
			}));
		}

		if (_authorization.HasPermission(ApplicationPermission.FinancePaymentProposalsApprove))
		{
			var paymentRuns = await _banking.SearchPendingApprovalPaymentRunsAsync(SourceItemLimit, cancellationToken);
			items.AddRange(paymentRuns.Select(value =>
			{
				var canApprove = _banking.CanApprovePaymentRun(value.CreatedByUserId);
				return new CommercialRoleItem(CommercialRoleItemKind.PaymentProposal, value.Id, value.Version, value.Description, "Payment Proposal", value.Currency.ToString(), "Draft / Awaiting Approval", value.Lines.Sum(line => line.Amount), value.CreatedAtUtc, value.PaymentDate.ToDateTime(TimeOnly.MinValue), DaysSince(now, value.CreatedAtUtc), "finance.banking", value.CreatedByUserId, canApprove, false);
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
			_ => CommercialRoleItemKind.SalesOrder
		}, value.EntityId, 0, value.DisplayNumber, value.Title, value.Context, value.Status, value.AmountOrQuantity, null, value.DueAt, value.AgeDays, value.RouteId, value.SourceUserId);

	private static CommercialRoleItem QuoteItem(SalesQuote value) =>
		new(CommercialRoleItemKind.SalesQuote, value.Id, value.Version, value.QuoteNumber, "Sales Quote", value.CustomerName, value.Status.ToString(), value.GrossAmount, value.CreatedAtUtc, value.ValidUntil, null, "sales.quotes", value.CreatedByUserId);

	private static CommercialRoleItem OrderItem(SalesOrder value) =>
		new(CommercialRoleItemKind.SalesOrder, value.Id, value.Version, value.OrderNumber, "Sales Order", value.CustomerName, value.Status.ToString(), value.GrossAmount, value.SubmittedAtUtc, value.RequestedDeliveryDate, null, "sales.orders", value.CreatedByUserId);

	private static CommercialRoleItem OrderItem(PurchaseOrder value) =>
		new(CommercialRoleItemKind.PurchaseOrder, value.Id, value.Version, value.OrderNumber, "Purchase Order", value.SupplierName, value.StatusDisplayName, value.Lines.Sum(line => line.Quantity * line.UnitPrice), value.SubmittedAtUtc, value.ExpectedDeliveryDate, value.SubmittedAtUtc is null ? null : DaysSince(DateTime.UtcNow, value.SubmittedAtUtc.Value), "purchasing.purchase-orders", value.CreatedByUserId);

	private static int DaysSince(DateTime nowUtc, DateTime timestampUtc) => Math.Max(0, (int)(nowUtc - timestampUtc).TotalDays);
}
