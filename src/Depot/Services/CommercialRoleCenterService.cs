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
		GoodsReceiptService goodsReceipts,
		ShipmentService shipments,
		InventoryCountService inventoryCounts,
		StockTransferService stockTransfers,
		MaterialIssueService materialIssues,
		MaterialReturnService materialReturns,
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
		_goodsReceipts = goodsReceipts;
		_shipments = shipments;
		_inventoryCounts = inventoryCounts;
		_stockTransfers = stockTransfers;
		_materialIssues = materialIssues;
		_materialReturns = materialReturns;
		_payables = payables;
		_banking = banking;
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

	private static int DaysSince(DateTime nowUtc, DateTime timestampUtc) => Math.Max(0, (int)(nowUtc - timestampUtc).TotalDays);
	private static string UserLabel(long? userId) => userId is null ? "Unknown" : $"User #{userId.Value:N0}";
}
