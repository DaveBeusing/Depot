// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

internal sealed class PurchasingMyWorkProvider : IMyWorkProvider
{
	private readonly PurchaseOrderService _orders;
	private readonly PurchaseOrderApprovalService _approvals;
	private readonly IAuthorizationService _authorization;

	public PurchasingMyWorkProvider(PurchaseOrderService orders, PurchaseOrderApprovalService approvals, IAuthorizationService authorization)
	{
		_orders = orders;
		_approvals = approvals;
		_authorization = authorization;
	}

	public string Name => "Purchasing";

	public bool CanQuery(IAuthorizationService authorization) =>
		authorization.HasAnyPermission(
			ApplicationPermission.PurchaseOrdersView,
			ApplicationPermission.PurchaseOrdersApprove,
			ApplicationPermission.GoodsReceiptsView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var items = new List<MyWorkItem>();
		var canViewOrders = _authorization.HasPermission(ApplicationPermission.PurchaseOrdersView);
		var canApprove = _authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove);
		var canReceive = _authorization.HasAnyPermission(ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost);
		var isAdministrator = _authorization.CurrentUser?.IsAdministrator == true;

		var draftsTask = SearchAsync(PurchaseOrderStatus.Draft, canViewOrders, query.ProviderLimit, cancellationToken);
		var orderedTask = SearchAsync(PurchaseOrderStatus.Ordered, canViewOrders, query.ProviderLimit, cancellationToken);
		var partialTask = SearchAsync(PurchaseOrderStatus.PartiallyReceived, canViewOrders, query.ProviderLimit, cancellationToken);
		var closedTask = SearchAsync(PurchaseOrderStatus.Closed, canViewOrders, query.ProviderLimit, cancellationToken);
		var approvalsTask = canApprove
			? _approvals.SearchAsync(new PurchaseOrderApprovalFilter(null, null, null, null, null), 1, query.ProviderLimit, cancellationToken)
			: Task.FromResult(new PurchaseOrderApprovalPage(new PageResult<PurchaseOrderApprovalWorkItem>([], 1, query.ProviderLimit, 0), new PurchaseOrderApprovalSummary(0, null, 0)));

		await Task.WhenAll(draftsTask, orderedTask, partialTask, closedTask, approvalsTask);

		foreach (var order in (await draftsTask).Items.Where(order => order.CreatedByUserId == query.UserId))
			items.Add(Item(MyWorkSectionKind.MyDrafts, MyWorkItemKind.PurchaseOrder, order, "Purchase order draft", order.SupplierName, order.StatusDisplayName, order.Lines.Sum(line => line.Quantity * line.UnitPrice), order.ExpectedDeliveryDate, MyWorkPriority.Normal, "purchasing.purchase-orders", _authorization.HasPermission(ApplicationPermission.PurchaseOrdersEdit) ? "Edit" : "Open"));

		foreach (var approval in (await approvalsTask).Page.Items)
		{
			var age = DaysSince(query.NowUtc, approval.SubmittedAtUtc);
			if (approval.CreatedByUserId == query.UserId && !isAdministrator)
				items.Add(new(MyWorkSectionKind.Waiting, MyWorkItemKind.PurchaseOrderApproval, approval.Id, approval.OrderNumber, "Purchase approval", approval.SupplierName, "Pending Approval", approval.TotalAmount, null, age, MyWorkPriority.Normal, "approvals.purchase", "Open", approval.CreatedByUserId));
			else if (_approvals.CanDecide(approval.CreatedByUserId))
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.PurchaseOrderApproval, approval.Id, approval.OrderNumber, "Purchase approval", approval.SupplierName, "Pending Approval", approval.TotalAmount, null, age, age >= 3 ? MyWorkPriority.High : MyWorkPriority.Normal, "approvals.purchase", "Review", approval.CreatedByUserId));
		}

		foreach (var order in (await orderedTask).Items.Concat((await partialTask).Items))
		{
			var due = order.ExpectedDeliveryDate;
			if (canReceive)
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.PurchaseOrder, order, "Supplier delivery", order.SupplierName, order.StatusDisplayName, order.Lines.Sum(line => Math.Max(0, line.Quantity - line.ReceivedQuantity)), due, MyWorkPriority.Normal, "purchasing.goods-receipts", "Receive"));
			if (due is not null && due.Value.Date < query.NowUtc.Date)
				items.Add(Item(MyWorkSectionKind.Exceptions, MyWorkItemKind.PurchaseOrder, order, "Overdue purchase delivery", order.SupplierName, order.StatusDisplayName, order.Lines.Sum(line => Math.Max(0, line.Quantity - line.ReceivedQuantity)), due, MyWorkPriority.High, "purchasing.purchase-orders", "Open"));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var order in (await closedTask).Items.Where(order => order.CreatedByUserId == query.UserId && order.ClosedAtUtc >= recentCutoff))
			items.Add(Item(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.PurchaseOrder, order, "Purchase order completed", order.SupplierName, order.StatusDisplayName, null, null, MyWorkPriority.Low, "purchasing.purchase-orders", "Open", order.ClosedAtUtc));

		return items;
	}

	private Task<PageResult<PurchaseOrder>> SearchAsync(PurchaseOrderStatus status, bool enabled, int limit, CancellationToken cancellationToken) =>
		enabled ? _orders.SearchAsync(null, status, 1, limit, cancellationToken) : Task.FromResult(new PageResult<PurchaseOrder>([], 1, limit, 0));

	private static MyWorkItem Item(MyWorkSectionKind section, MyWorkItemKind kind, PurchaseOrder order, string title, string? context, string status, decimal? amount, DateTime? dueAt, MyWorkPriority priority, string routeId, string action, DateTime? completedAtUtc = null) =>
		new(section, kind, order.Id, order.OrderNumber, title, context, status, amount, dueAt, null, priority, routeId, action, order.CreatedByUserId, completedAtUtc);

	private static int DaysSince(DateTime nowUtc, DateTime timestampUtc) => Math.Max(0, (int)(nowUtc - timestampUtc).TotalDays);
}

internal sealed class SalesMyWorkProvider : IMyWorkProvider
{
	private readonly SalesOrderService _orders;
	private readonly ShipmentService _shipments;
	private readonly IAuthorizationService _authorization;

	public SalesMyWorkProvider(SalesOrderService orders, ShipmentService shipments, IAuthorizationService authorization)
	{
		_orders = orders;
		_shipments = shipments;
		_authorization = authorization;
	}

	public string Name => "Sales and Shipping";

	public bool CanQuery(IAuthorizationService authorization) =>
		authorization.HasAnyPermission(ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersApprove, ApplicationPermission.ShipmentsView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var items = new List<MyWorkItem>();
		var canViewOrders = _authorization.HasPermission(ApplicationPermission.SalesOrdersView);
		var canViewShipments = _authorization.HasPermission(ApplicationPermission.ShipmentsView);
		var canApprove = _authorization.HasPermission(ApplicationPermission.SalesOrdersApprove);
		var canRelease = _authorization.HasPermission(ApplicationPermission.SalesOrdersRelease);
		var canShip = _authorization.HasPermission(ApplicationPermission.ShipmentsCreate);
		var isAdministrator = _authorization.CurrentUser?.IsAdministrator == true;

		var draftTask = SearchOrdersAsync(SalesOrderStatus.Draft, canViewOrders, query.ProviderLimit, cancellationToken);
		var pendingTask = SearchOrdersAsync(SalesOrderStatus.PendingApproval, canViewOrders, query.ProviderLimit, cancellationToken);
		var approvedTask = SearchOrdersAsync(SalesOrderStatus.Approved, canViewOrders, query.ProviderLimit, cancellationToken);
		var releasedTask = SearchOrdersAsync(SalesOrderStatus.Released, canViewOrders, query.ProviderLimit, cancellationToken);
		var partialTask = SearchOrdersAsync(SalesOrderStatus.PartiallyShipped, canViewOrders, query.ProviderLimit, cancellationToken);
		var shipmentDraftTask = SearchShipmentsAsync(ShipmentStatus.Draft, canViewShipments, query.ProviderLimit, cancellationToken);
		var shipmentPostedTask = SearchShipmentsAsync(ShipmentStatus.Posted, canViewShipments, query.ProviderLimit, cancellationToken);
		await Task.WhenAll(draftTask, pendingTask, approvedTask, releasedTask, partialTask, shipmentDraftTask, shipmentPostedTask);

		foreach (var order in (await draftTask).Items.Where(order => order.CreatedByUserId == query.UserId))
			items.Add(OrderItem(MyWorkSectionKind.MyDrafts, order, "Sales order draft", MyWorkPriority.Normal, "sales.orders", _authorization.HasPermission(ApplicationPermission.SalesOrdersEdit) ? "Edit" : "Open"));

		foreach (var order in (await pendingTask).Items)
		{
			var age = order.SubmittedAtUtc is null ? (int?)null : Math.Max(0, (int)(query.NowUtc - order.SubmittedAtUtc.Value).TotalDays);
			if (order.CreatedByUserId == query.UserId && !isAdministrator)
				items.Add(new(MyWorkSectionKind.Waiting, MyWorkItemKind.SalesOrderApproval, order.Id, order.OrderNumber, "Sales approval", order.CustomerName, "Pending Approval", order.GrossAmount, order.RequestedDeliveryDate, age, MyWorkPriority.Normal, "approvals.sales", "Open", order.CreatedByUserId));
			else if (canApprove)
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrderApproval, order.Id, order.OrderNumber, "Sales approval", order.CustomerName, "Pending Approval", order.GrossAmount, order.RequestedDeliveryDate, age, age >= 3 ? MyWorkPriority.High : MyWorkPriority.Normal, "approvals.sales", "Review", order.CreatedByUserId));
		}

		if (canRelease)
			foreach (var order in (await approvedTask).Items)
				items.Add(OrderItem(MyWorkSectionKind.NeedsMyAction, order, "Approved sales order", MyWorkPriority.Normal, "sales.orders", "Release"));

		foreach (var order in (await releasedTask).Items.Concat((await partialTask).Items))
		{
			var backordered = order.Lines.Sum(line => line.BackorderedQuantity);
			if (canShip)
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrder, order.Id, order.OrderNumber, "Fulfill sales order", order.CustomerName, order.Status.ToString(), order.Lines.Sum(line => line.OpenQuantity), order.RequestedDeliveryDate, null, MyWorkPriority.Normal, "sales.shipping", "Ship", order.CreatedByUserId));
			if (backordered > 0)
				items.Add(new(MyWorkSectionKind.Exceptions, MyWorkItemKind.SalesOrder, order.Id, order.OrderNumber, "Backordered sales order", order.CustomerName, order.Status.ToString(), backordered, order.RequestedDeliveryDate, null, MyWorkPriority.High, "sales.orders", "Open", order.CreatedByUserId));
		}

		foreach (var shipment in (await shipmentDraftTask).Items.Where(shipment => shipment.CreatedByUserId == query.UserId))
		{
			items.Add(new(MyWorkSectionKind.MyDrafts, MyWorkItemKind.Shipment, shipment.Id, shipment.ShipmentNumber, "Shipment draft", shipment.CustomerName, shipment.PackingStatus.ToString(), shipment.Lines.Sum(line => line.Quantity), shipment.ShipmentDate, null, MyWorkPriority.Normal, "sales.shipping", _shipments.CanEdit ? "Edit" : "Open", shipment.CreatedByUserId));
			if (_shipments.CanPost && shipment.PackingStatus == ShipmentPackingStatus.Packed)
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.Shipment, shipment.Id, shipment.ShipmentNumber, "Packed shipment", shipment.CustomerName, "Ready to post", shipment.Lines.Sum(line => line.Quantity), shipment.ShipmentDate, null, MyWorkPriority.High, "sales.shipping", "Post", shipment.CreatedByUserId));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var shipment in (await shipmentPostedTask).Items.Where(shipment => shipment.CreatedByUserId == query.UserId && shipment.PostedAtUtc >= recentCutoff))
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.Shipment, shipment.Id, shipment.ShipmentNumber, "Shipment posted", shipment.CustomerName, "Posted", shipment.Lines.Sum(line => line.Quantity), null, null, MyWorkPriority.Low, "sales.shipping", "Open", shipment.CreatedByUserId, shipment.PostedAtUtc));

		return items;
	}

	private Task<PageResult<SalesOrder>> SearchOrdersAsync(SalesOrderStatus status, bool enabled, int limit, CancellationToken cancellationToken) =>
		enabled ? _orders.SearchAsync(null, status, 1, limit, cancellationToken) : Task.FromResult(new PageResult<SalesOrder>([], 1, limit, 0));

	private Task<PageResult<Shipment>> SearchShipmentsAsync(ShipmentStatus status, bool enabled, int limit, CancellationToken cancellationToken) =>
		enabled ? _shipments.SearchAsync(null, status, 1, limit, cancellationToken) : Task.FromResult(new PageResult<Shipment>([], 1, limit, 0));

	private static MyWorkItem OrderItem(MyWorkSectionKind section, SalesOrder order, string title, MyWorkPriority priority, string routeId, string action) =>
		new(section, MyWorkItemKind.SalesOrder, order.Id, order.OrderNumber, title, order.CustomerName, order.Status.ToString(), order.GrossAmount, order.RequestedDeliveryDate, null, priority, routeId, action, order.CreatedByUserId);
}

internal sealed class InventoryCountMyWorkProvider : IMyWorkProvider
{
	private readonly InventoryCountService _counts;
	private readonly IAuthorizationService _authorization;

	public InventoryCountMyWorkProvider(InventoryCountService counts, IAuthorizationService authorization)
	{
		_counts = counts;
		_authorization = authorization;
	}

	public string Name => "Inventory Counts";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.InventoryCountsView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var draftTask = SearchAsync(InventoryCountStatus.Draft, query.ProviderLimit, cancellationToken);
		var countingTask = SearchAsync(InventoryCountStatus.Counting, query.ProviderLimit, cancellationToken);
		var reviewTask = SearchAsync(InventoryCountStatus.Review, query.ProviderLimit, cancellationToken);
		var postedTask = SearchAsync(InventoryCountStatus.Posted, query.ProviderLimit, cancellationToken);
		await Task.WhenAll(draftTask, countingTask, reviewTask, postedTask);

		var items = new List<MyWorkItem>();
		var drafts = (await draftTask).Items;
		var posted = (await postedTask).Items;
		var draftHeaders = await LoadHeadersAsync(drafts, cancellationToken);
		var postedHeaders = await LoadHeadersAsync(posted, cancellationToken);

		foreach (var row in drafts)
		{
			if (!draftHeaders.TryGetValue(row.Id, out var header) || header.CreatedByUserId != query.UserId) continue;
			items.Add(Item(MyWorkSectionKind.MyDrafts, row, "Inventory count draft", MyWorkPriority.Normal, _authorization.HasPermission(ApplicationPermission.InventoryCountsEdit) ? "Edit" : "Open", header.CreatedByUserId));
		}

		if (_authorization.HasPermission(ApplicationPermission.InventoryCountsEdit))
			foreach (var row in (await countingTask).Items)
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, row, "Count inventory", MyWorkPriority.Normal, "Count", null));

		foreach (var row in (await reviewTask).Items)
		{
			if (_authorization.HasPermission(ApplicationPermission.InventoryCountsPost))
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, row, "Review inventory count", row.DifferenceLineCount > 0 ? MyWorkPriority.High : MyWorkPriority.Normal, "Post", null));
			if (row.DifferenceLineCount > 0)
				items.Add(new(MyWorkSectionKind.Exceptions, MyWorkItemKind.InventoryCount, row.Id, row.CountNumber, "Inventory count differences", row.WarehouseName, row.StatusDisplayName, row.DifferenceLineCount, null, null, MyWorkPriority.High, "warehouse.inventory-counts", "Open", null));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var row in posted)
		{
			if (!postedHeaders.TryGetValue(row.Id, out var header) || header.CreatedByUserId != query.UserId || header.CompletedAtUtc < recentCutoff) continue;
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.InventoryCount, row.Id, row.CountNumber, "Inventory count posted", row.WarehouseName, row.StatusDisplayName, row.TotalLineCount, null, null, MyWorkPriority.Low, "warehouse.inventory-counts", "Open", header.CreatedByUserId, header.CompletedAtUtc));
		}

		return items;
	}

	private Task<PageResult<InventoryCountOverviewItem>> SearchAsync(InventoryCountStatus status, int limit, CancellationToken cancellationToken) =>
		_counts.SearchAsync(null, status, null, 1, limit, cancellationToken);

	private async Task<Dictionary<long, InventoryCount>> LoadHeadersAsync(IEnumerable<InventoryCountOverviewItem> rows, CancellationToken cancellationToken)
	{
		var tasks = rows.Select(async row => (row.Id, Value: await _counts.GetHeaderByIdAsync(row.Id, cancellationToken))).ToArray();
		var results = await Task.WhenAll(tasks);
		return results.Where(result => result.Value is not null).ToDictionary(result => result.Id, result => result.Value!);
	}

	private static MyWorkItem Item(MyWorkSectionKind section, InventoryCountOverviewItem row, string title, MyWorkPriority priority, string action, long? sourceUserId) =>
		new(section, MyWorkItemKind.InventoryCount, row.Id, row.CountNumber, title, row.WarehouseName, row.StatusDisplayName, row.TotalLineCount, null, null, priority, "warehouse.inventory-counts", action, sourceUserId);
}

internal sealed class ReceivablesMyWorkProvider : IMyWorkProvider
{
	private readonly FinanceAccountsReceivableService _receivables;
	private readonly IAuthorizationService _authorization;

	public ReceivablesMyWorkProvider(FinanceAccountsReceivableService receivables, IAuthorizationService authorization)
	{
		_receivables = receivables;
		_authorization = authorization;
	}

	public string Name => "Accounts Receivable";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.FinanceReceivablesView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var page = await _receivables.SearchOpenItemsAsync(null, false, 1, query.ProviderLimit, cancellationToken);
		var today = DateOnly.FromDateTime(query.NowUtc);
		return page.Items
			.Where(item => item.Kind == FinanceReceivableOpenItemKind.Invoice && !item.IsVoided && item.RemainingAmount > 0m && item.DueDate < today)
			.Select(item => new MyWorkItem(
				MyWorkSectionKind.Exceptions,
				MyWorkItemKind.ReceivableOpenItem,
				item.Id,
				item.SourceReference ?? item.SourceId,
				"Overdue receivable",
				item.CustomerName,
				item.SettlementStatus.ToString(),
				item.RemainingAmount,
				item.DueDate.ToDateTime(TimeOnly.MinValue),
				today.DayNumber - item.DueDate.DayNumber,
				MyWorkPriority.High,
				"finance.receivables",
				_authorization.HasPermission(ApplicationPermission.FinanceDunningManage) ? "Review / dunning" : "Open",
				item.CreatedByUserId))
			.ToArray();
	}
}

internal sealed class PayablesMyWorkProvider : IMyWorkProvider
{
	private readonly FinanceAccountsPayableService _payables;
	private readonly IAuthorizationService _authorization;

	public PayablesMyWorkProvider(FinanceAccountsPayableService payables, IAuthorizationService authorization)
	{
		_payables = payables;
		_authorization = authorization;
	}

	public string Name => "Accounts Payable";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.FinancePayablesView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var draftTask = SearchAsync(FinancePayableDocumentStatus.Draft, query.ProviderLimit, cancellationToken);
		var pendingTask = SearchAsync(FinancePayableDocumentStatus.PendingApproval, query.ProviderLimit, cancellationToken);
		var approvedTask = SearchAsync(FinancePayableDocumentStatus.Approved, query.ProviderLimit, cancellationToken);
		var postedTask = SearchAsync(FinancePayableDocumentStatus.Posted, query.ProviderLimit, cancellationToken);
		await Task.WhenAll(draftTask, pendingTask, approvedTask, postedTask);

		var items = new List<MyWorkItem>();
		foreach (var document in (await draftTask).Items.Where(document => document.CreatedByUserId == query.UserId))
			items.Add(Item(MyWorkSectionKind.MyDrafts, document, "Supplier document draft", MyWorkPriority.Normal, _payables.CanCreateDocuments ? "Edit" : "Open"));

		foreach (var document in (await pendingTask).Items)
		{
			if (document.CreatedByUserId == query.UserId && _authorization.CurrentUser?.IsAdministrator != true)
				items.Add(Item(MyWorkSectionKind.Waiting, document, "Supplier document approval", MyWorkPriority.Normal, "Open"));
			else if (_payables.CanApproveDocuments)
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, document, "Supplier document approval", MyWorkPriority.Normal, "Review"));

			var details = await _payables.GetDocumentAsync(document.Id, cancellationToken);
			if (details is { HasMatchExceptions: true, MatchExceptionApproved: false })
				items.Add(Item(MyWorkSectionKind.Exceptions, details, "Supplier match exception", MyWorkPriority.High, _payables.CanApproveMatchExceptions ? "Resolve exception" : "Open"));
		}

		foreach (var document in (await approvedTask).Items)
		{
			if (_payables.CanPostDocuments)
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, document, "Approved supplier document", MyWorkPriority.Normal, "Post"));
			if (document.CreatedByUserId == query.UserId && !_payables.CanPostDocuments)
				items.Add(Item(MyWorkSectionKind.Waiting, document, "Awaiting supplier posting", MyWorkPriority.Normal, "Open"));

			var details = await _payables.GetDocumentAsync(document.Id, cancellationToken);
			if (details is { HasMatchExceptions: true, MatchExceptionApproved: false })
				items.Add(Item(MyWorkSectionKind.Exceptions, details, "Supplier match exception", MyWorkPriority.High, _payables.CanApproveMatchExceptions ? "Resolve exception" : "Open"));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var document in (await postedTask).Items.Where(document => document.CreatedByUserId == query.UserId && document.PostedAtUtc >= recentCutoff))
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.SupplierDocument, document.Id, document.SupplierDocumentNumber, "Supplier document posted", document.SupplierName, document.Status.ToString(), document.GrossAmount, document.DueDate.ToDateTime(TimeOnly.MinValue), null, MyWorkPriority.Low, "finance.payables", "Open", document.CreatedByUserId, document.PostedAtUtc));

		return items;
	}

	private Task<PageResult<FinanceSupplierDocument>> SearchAsync(FinancePayableDocumentStatus status, int limit, CancellationToken cancellationToken) =>
		_payables.SearchDocumentsAsync(null, status, 1, limit, cancellationToken);

	private static MyWorkItem Item(MyWorkSectionKind section, FinanceSupplierDocument document, string title, MyWorkPriority priority, string action) =>
		new(section, MyWorkItemKind.SupplierDocument, document.Id, document.SupplierDocumentNumber, title, document.SupplierName, document.Status.ToString(), document.GrossAmount, document.DueDate.ToDateTime(TimeOnly.MinValue), null, priority, "finance.payables", action, document.CreatedByUserId);
}

internal sealed class BankingMyWorkProvider : IMyWorkProvider
{
	private readonly FinanceBankingService _banking;
	private readonly IAuthorizationService _authorization;

	public BankingMyWorkProvider(FinanceBankingService banking, IAuthorizationService authorization)
	{
		_banking = banking;
		_authorization = authorization;
	}

	public string Name => "Banking";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.FinanceBankingView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var runsTask = _banking.GetPaymentRunsAsync(cancellationToken);
		var unreconciledTask = _banking.SearchUnreconciledLinesAsync(null, 1, query.ProviderLimit, cancellationToken);
		await Task.WhenAll(runsTask, unreconciledTask);
		var items = new List<MyWorkItem>();

		foreach (var run in (await runsTask).Take(query.ProviderLimit))
		{
			if (run.Status == FinancePaymentRunStatus.Draft)
			{
				if (_banking.CanApprovePaymentRuns && run.CreatedByUserId != query.UserId)
					items.Add(RunItem(MyWorkSectionKind.NeedsMyAction, run, "Payment proposal", MyWorkPriority.Normal, "Approve"));
				else if (run.CreatedByUserId == query.UserId)
					items.Add(RunItem(MyWorkSectionKind.Waiting, run, "Payment proposal", MyWorkPriority.Normal, "Open"));
			}
			else if (run.Status is FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted)
			{
				if (_banking.CanExecutePaymentRuns)
					items.Add(RunItem(MyWorkSectionKind.NeedsMyAction, run, "Approved payment run", MyWorkPriority.High, "Execute"));
			}
			else if (run.Status == FinancePaymentRunStatus.Executed && run.CreatedByUserId == query.UserId && run.CompletedAtUtc >= query.NowUtc.AddDays(-14))
				items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.PaymentRun, run.Id, $"PAY-{run.Id:N0}", "Payment run executed", run.Description, run.Status.ToString(), run.Lines.Sum(line => line.Amount), run.PaymentDate.ToDateTime(TimeOnly.MinValue), null, MyWorkPriority.Low, "finance.banking", "Open", run.CreatedByUserId, run.CompletedAtUtc));
		}

		foreach (var line in (await unreconciledTask).Items)
			items.Add(new(MyWorkSectionKind.Exceptions, MyWorkItemKind.BankStatementLine, line.Id, line.Reference ?? $"BANK-{line.Id:N0}", "Unreconciled bank item", line.CounterpartyName, "Unreconciled", Math.Abs(line.Amount), line.BookingDate.ToDateTime(TimeOnly.MinValue), null, MyWorkPriority.High, "finance.banking", _authorization.HasPermission(ApplicationPermission.FinanceBankReconciliationManage) ? "Reconcile" : "Open", null));

		return items;
	}

	private static MyWorkItem RunItem(MyWorkSectionKind section, FinancePaymentRun run, string title, MyWorkPriority priority, string action) =>
		new(section, MyWorkItemKind.PaymentRun, run.Id, $"PAY-{run.Id:N0}", title, run.Description, run.Status.ToString(), run.Lines.Sum(line => line.Amount), run.PaymentDate.ToDateTime(TimeOnly.MinValue), null, priority, "finance.banking", action, run.CreatedByUserId);
}
