// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

internal sealed class PurchasingMyWorkProvider : IMyWorkProvider
{
	private readonly PurchaseOrderService _orders;
	private readonly PurchaseOrderApprovalService _approvals;
	private readonly MyWorkReadRepository _read;
	private readonly IAuthorizationService _authorization;
	private readonly ApprovalPolicyService? _approvalPolicies;

	public PurchasingMyWorkProvider(PurchaseOrderService orders, PurchaseOrderApprovalService approvals, MyWorkReadRepository read, IAuthorizationService authorization, ApprovalPolicyService? approvalPolicies = null)
	{
		_orders = orders;
		_approvals = approvals;
		_read = read;
		_authorization = authorization;
		_approvalPolicies = approvalPolicies;
	}

	public string Name => "Purchasing";
	public bool CanQuery(IAuthorizationService authorization) =>
		authorization.HasAnyPermission(ApplicationPermission.PurchaseOrdersView, ApplicationPermission.PurchaseOrdersApprove, ApplicationPermission.GoodsReceiptsView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var items = new List<MyWorkItem>();
		var canViewOrders = _authorization.HasPermission(ApplicationPermission.PurchaseOrdersView);
		var canApprove = _authorization.HasPermission(ApplicationPermission.PurchaseOrdersApprove);
		var canReceive = _authorization.HasAnyPermission(ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost);
		var isAdministrator = _authorization.CurrentUser?.IsAdministrator == true;

		var ordersTask = canViewOrders ? _read.GetPurchaseOrdersAsync(query.ProviderLimit, cancellationToken) : Task.FromResult<IReadOnlyList<PurchaseOrderMyWorkRow>>([]);
		var approvalsTask = canApprove ? _read.GetPurchaseApprovalsAsync(query.ProviderLimit, cancellationToken) : Task.FromResult<IReadOnlyList<PurchaseOrderApprovalWorkItem>>([]);
		await Task.WhenAll(ordersTask, approvalsTask);
		var orders = await ordersTask;

		foreach (var order in orders.Where(order => order.Status == PurchaseOrderStatus.Draft && order.CreatedByUserId == query.UserId))
			items.Add(Item(MyWorkSectionKind.MyDrafts, order, "Purchase order draft", order.TotalAmount, order.ExpectedDeliveryDate, MyWorkPriority.Normal, "purchasing.purchase-orders", _orders.CanCurrentUserEdit ? "Edit" : "Open"));

		foreach (var approval in await approvalsTask)
		{
			var age = Math.Max(0, (int)(query.NowUtc - approval.SubmittedAtUtc).TotalDays);
			if (approval.CreatedByUserId == query.UserId && !isAdministrator)
				items.Add(new(MyWorkSectionKind.Waiting, MyWorkItemKind.PurchaseOrderApproval, approval.Id, approval.OrderNumber, "Purchase approval", approval.SupplierName, "Pending Approval", approval.TotalAmount, null, age, MyWorkPriority.Normal, "approvals.purchase", "Open", approval.CreatedByUserId));
			else if (_approvals.CanDecide(approval.CreatedByUserId) && (_approvalPolicies is null || await _approvalPolicies.CanCurrentUserDecideAsync(ApprovalSubjectKind.PurchaseOrder, approval.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)))
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.PurchaseOrderApproval, approval.Id, approval.OrderNumber, "Purchase approval", approval.SupplierName, "Pending Approval", approval.TotalAmount, null, age, age >= 3 ? MyWorkPriority.High : MyWorkPriority.Normal, "approvals.purchase", "Review", approval.CreatedByUserId));
		}

		foreach (var order in orders.Where(order => order.Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
		{
			if (canReceive)
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, order, "Supplier delivery", order.OpenQuantity, order.ExpectedDeliveryDate, MyWorkPriority.Normal, "purchasing.goods-receipts", "Receive"));
			if (order.ExpectedDeliveryDate is { } due && due.Date < query.NowUtc.Date)
				items.Add(Item(MyWorkSectionKind.Exceptions, order, "Overdue purchase delivery", order.OpenQuantity, due, MyWorkPriority.High, "purchasing.purchase-orders", "Open"));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var order in orders.Where(order => order.Status == PurchaseOrderStatus.Closed && order.CreatedByUserId == query.UserId && order.ClosedAtUtc >= recentCutoff))
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.PurchaseOrder, order.Id, order.OrderNumber, "Purchase order completed", order.SupplierName, order.StatusDisplayName, null, null, null, MyWorkPriority.Low, "purchasing.purchase-orders", "Open", order.CreatedByUserId, order.ClosedAtUtc));

		return items;
	}

	private static MyWorkItem Item(MyWorkSectionKind section, PurchaseOrderMyWorkRow order, string title, decimal? amount, DateTime? dueAt, MyWorkPriority priority, string routeId, string action) =>
		new(section, MyWorkItemKind.PurchaseOrder, order.Id, order.OrderNumber, title, order.SupplierName, order.StatusDisplayName, amount, dueAt, null, priority, routeId, action, order.CreatedByUserId);
}

internal sealed class SalesMyWorkProvider : IMyWorkProvider
{
	private readonly SalesOrderService _orders;
	private readonly ShipmentService _shipments;
	private readonly MyWorkReadRepository _read;
	private readonly IAuthorizationService _authorization;
	private readonly ApprovalPolicyService? _approvalPolicies;

	public SalesMyWorkProvider(SalesOrderService orders, ShipmentService shipments, MyWorkReadRepository read, IAuthorizationService authorization, ApprovalPolicyService? approvalPolicies = null)
	{
		_orders = orders;
		_shipments = shipments;
		_read = read;
		_authorization = authorization;
		_approvalPolicies = approvalPolicies;
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

		var ordersTask = canViewOrders ? _read.GetSalesOrdersAsync(query.ProviderLimit, cancellationToken) : Task.FromResult<IReadOnlyList<SalesOrderMyWorkRow>>([]);
		var shipmentsTask = canViewShipments ? _read.GetShipmentsAsync(query.ProviderLimit, cancellationToken) : Task.FromResult<IReadOnlyList<ShipmentMyWorkRow>>([]);
		await Task.WhenAll(ordersTask, shipmentsTask);
		var orders = await ordersTask;
		var shipments = await shipmentsTask;

		foreach (var order in orders.Where(order => order.Status == SalesOrderStatus.Draft && order.CreatedByUserId == query.UserId))
			items.Add(OrderItem(MyWorkSectionKind.MyDrafts, order, "Sales order draft", MyWorkPriority.Normal, "sales.orders", _orders.CanEdit ? "Edit" : "Open"));

		foreach (var order in orders.Where(order => order.Status == SalesOrderStatus.PendingApproval))
		{
			var age = order.SubmittedAtUtc is null ? (int?)null : Math.Max(0, (int)(query.NowUtc - order.SubmittedAtUtc.Value).TotalDays);
			if (order.CreatedByUserId == query.UserId && !isAdministrator)
				items.Add(new(MyWorkSectionKind.Waiting, MyWorkItemKind.SalesOrderApproval, order.Id, order.OrderNumber, "Sales approval", order.CustomerName, "Pending Approval", order.GrossAmount, order.RequestedDeliveryDate, age, MyWorkPriority.Normal, "approvals.sales", "Open", order.CreatedByUserId));
			else if (canApprove && (_approvalPolicies is null || await _approvalPolicies.CanCurrentUserDecideAsync(ApprovalSubjectKind.SalesOrder, order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)))
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrderApproval, order.Id, order.OrderNumber, "Sales approval", order.CustomerName, "Pending Approval", order.GrossAmount, order.RequestedDeliveryDate, age, age >= 3 ? MyWorkPriority.High : MyWorkPriority.Normal, "approvals.sales", "Review", order.CreatedByUserId));
		}

		if (canRelease)
			foreach (var order in orders.Where(order => order.Status == SalesOrderStatus.Approved))
				items.Add(OrderItem(MyWorkSectionKind.NeedsMyAction, order, "Approved sales order", MyWorkPriority.Normal, "sales.orders", "Release"));

		foreach (var order in orders.Where(order => order.Status is SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped))
		{
			if (canShip)
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrder, order.Id, order.OrderNumber, "Fulfill sales order", order.CustomerName, order.Status.ToString(), order.OpenQuantity, order.RequestedDeliveryDate, null, MyWorkPriority.Normal, "sales.shipping", "Ship", order.CreatedByUserId));
			if (order.BackorderedQuantity > 0)
				items.Add(new(MyWorkSectionKind.Exceptions, MyWorkItemKind.SalesOrder, order.Id, order.OrderNumber, "Backordered sales order", order.CustomerName, order.Status.ToString(), order.BackorderedQuantity, order.RequestedDeliveryDate, null, MyWorkPriority.High, "sales.orders", "Open", order.CreatedByUserId));
		}

		foreach (var shipment in shipments.Where(shipment => shipment.Status == ShipmentStatus.Draft && shipment.CreatedByUserId == query.UserId))
		{
			items.Add(new(MyWorkSectionKind.MyDrafts, MyWorkItemKind.Shipment, shipment.Id, shipment.ShipmentNumber, "Shipment draft", shipment.CustomerName, shipment.PackingStatus.ToString(), shipment.TotalQuantity, shipment.ShipmentDate, null, MyWorkPriority.Normal, "sales.shipping", _shipments.CanEdit ? "Edit" : "Open", shipment.CreatedByUserId));
			if (_shipments.CanPost && shipment.PackingStatus == ShipmentPackingStatus.Packed)
				items.Add(new(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.Shipment, shipment.Id, shipment.ShipmentNumber, "Packed shipment", shipment.CustomerName, "Ready to post", shipment.TotalQuantity, shipment.ShipmentDate, null, MyWorkPriority.High, "sales.shipping", "Post", shipment.CreatedByUserId));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var shipment in shipments.Where(shipment => shipment.Status == ShipmentStatus.Posted && shipment.CreatedByUserId == query.UserId && shipment.PostedAtUtc >= recentCutoff))
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.Shipment, shipment.Id, shipment.ShipmentNumber, "Shipment posted", shipment.CustomerName, "Posted", shipment.TotalQuantity, null, null, MyWorkPriority.Low, "sales.shipping", "Open", shipment.CreatedByUserId, shipment.PostedAtUtc));

		return items;
	}

	private static MyWorkItem OrderItem(MyWorkSectionKind section, SalesOrderMyWorkRow order, string title, MyWorkPriority priority, string routeId, string action) =>
		new(section, MyWorkItemKind.SalesOrder, order.Id, order.OrderNumber, title, order.CustomerName, order.Status.ToString(), order.GrossAmount, order.RequestedDeliveryDate, null, priority, routeId, action, order.CreatedByUserId);
}

internal sealed class InventoryCountMyWorkProvider : IMyWorkProvider
{
	private readonly MyWorkReadRepository _read;
	private readonly IAuthorizationService _authorization;

	public InventoryCountMyWorkProvider(MyWorkReadRepository read, IAuthorizationService authorization)
	{
		_read = read;
		_authorization = authorization;
	}

	public string Name => "Inventory Counts";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.InventoryCountsView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		if (!_authorization.HasPermission(ApplicationPermission.InventoryCountsView)) return [];
		var rows = await _read.GetInventoryCountsAsync(query.ProviderLimit, cancellationToken);
		var items = new List<MyWorkItem>();

		foreach (var row in rows.Where(row => row.Status == InventoryCountStatus.Draft && row.CreatedByUserId == query.UserId))
			items.Add(Item(MyWorkSectionKind.MyDrafts, row, "Inventory count draft", MyWorkPriority.Normal, _authorization.HasPermission(ApplicationPermission.InventoryCountsEdit) ? "Edit" : "Open"));

		if (_authorization.HasPermission(ApplicationPermission.InventoryCountsEdit))
			foreach (var row in rows.Where(row => row.Status == InventoryCountStatus.Counting))
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, row, "Count inventory", MyWorkPriority.Normal, "Count"));

		foreach (var row in rows.Where(row => row.Status == InventoryCountStatus.Review))
		{
			if (_authorization.HasPermission(ApplicationPermission.InventoryCountsPost))
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, row, "Review inventory count", row.DifferenceLineCount > 0 ? MyWorkPriority.High : MyWorkPriority.Normal, "Post"));
			if (row.DifferenceLineCount > 0)
				items.Add(new(MyWorkSectionKind.Exceptions, MyWorkItemKind.InventoryCount, row.Id, row.CountNumber, "Inventory count differences", row.WarehouseName, row.StatusDisplayName, row.DifferenceLineCount, null, null, MyWorkPriority.High, "warehouse.inventory-counts", "Open", null));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var row in rows.Where(row => row.Status == InventoryCountStatus.Posted && row.CreatedByUserId == query.UserId && row.CompletedAtUtc >= recentCutoff))
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.InventoryCount, row.Id, row.CountNumber, "Inventory count posted", row.WarehouseName, row.StatusDisplayName, row.TotalLineCount, null, null, MyWorkPriority.Low, "warehouse.inventory-counts", "Open", row.CreatedByUserId, row.CompletedAtUtc));

		return items;
	}

	private static MyWorkItem Item(MyWorkSectionKind section, InventoryCountMyWorkRow row, string title, MyWorkPriority priority, string action) =>
		new(section, MyWorkItemKind.InventoryCount, row.Id, row.CountNumber, title, row.WarehouseName, row.StatusDisplayName, row.TotalLineCount, null, null, priority, "warehouse.inventory-counts", action, row.CreatedByUserId);
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
	private readonly MyWorkReadRepository _read;
	private readonly IAuthorizationService _authorization;
	private readonly ApprovalPolicyService? _approvalPolicies;

	public PayablesMyWorkProvider(FinanceAccountsPayableService payables, MyWorkReadRepository read, IAuthorizationService authorization, ApprovalPolicyService? approvalPolicies = null)
	{
		_payables = payables;
		_read = read;
		_authorization = authorization;
		_approvalPolicies = approvalPolicies;
	}

	public string Name => "Accounts Payable";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.FinancePayablesView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		if (!_authorization.HasPermission(ApplicationPermission.FinancePayablesView)) return [];
		var documents = await _read.GetPayablesDocumentsAsync(query.ProviderLimit, cancellationToken);
		var items = new List<MyWorkItem>();

		foreach (var document in documents.Where(document => document.Status == FinancePayableDocumentStatus.Draft && document.CreatedByUserId == query.UserId))
			items.Add(Item(MyWorkSectionKind.MyDrafts, document, "Supplier document draft", MyWorkPriority.Normal, _payables.CanCreateDocuments ? "Edit" : "Open"));

		foreach (var document in documents.Where(document => document.Status == FinancePayableDocumentStatus.PendingApproval))
		{
			if (document.CreatedByUserId == query.UserId && _authorization.CurrentUser?.IsAdministrator != true)
				items.Add(Item(MyWorkSectionKind.Waiting, document, "Supplier document approval", MyWorkPriority.Normal, "Open"));
			else if (_payables.CanApproveDocuments && (!document.HasMatchExceptions || _approvalPolicies is null || await _approvalPolicies.CanCurrentUserDecideAsync(ApprovalSubjectKind.AccountsPayableException, document.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)))
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, document, "Supplier document approval", MyWorkPriority.Normal, "Review"));
			if (document.HasMatchExceptions && !document.MatchExceptionApproved)
			{
				var canResolveException = _payables.CanApproveMatchExceptions && (_approvalPolicies is null || await _approvalPolicies.CanCurrentUserDecideAsync(ApprovalSubjectKind.AccountsPayableException, document.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken));
				items.Add(Item(MyWorkSectionKind.Exceptions, document, "Supplier match exception", MyWorkPriority.High, canResolveException ? "Resolve exception" : "Open"));
			}
		}

		foreach (var document in documents.Where(document => document.Status == FinancePayableDocumentStatus.Approved))
		{
			if (_payables.CanPostDocuments)
				items.Add(Item(MyWorkSectionKind.NeedsMyAction, document, "Approved supplier document", MyWorkPriority.Normal, "Post"));
			if (document.CreatedByUserId == query.UserId && !_payables.CanPostDocuments)
				items.Add(Item(MyWorkSectionKind.Waiting, document, "Awaiting supplier posting", MyWorkPriority.Normal, "Open"));
			if (document.HasMatchExceptions && !document.MatchExceptionApproved)
				items.Add(Item(MyWorkSectionKind.Exceptions, document, "Supplier match exception", MyWorkPriority.High, _payables.CanApproveMatchExceptions ? "Resolve exception" : "Open"));
		}

		var recentCutoff = query.NowUtc.AddDays(-14);
		foreach (var document in documents.Where(document => document.Status == FinancePayableDocumentStatus.Posted && document.CreatedByUserId == query.UserId && document.PostedAtUtc >= recentCutoff))
			items.Add(new(MyWorkSectionKind.RecentlyCompleted, MyWorkItemKind.SupplierDocument, document.Id, document.SupplierDocumentNumber, "Supplier document posted", document.SupplierName, document.Status.ToString(), document.GrossAmount, document.DueDate.ToDateTime(TimeOnly.MinValue), null, MyWorkPriority.Low, "finance.payables", "Open", document.CreatedByUserId, document.PostedAtUtc));

		return items;
	}

	private static MyWorkItem Item(MyWorkSectionKind section, FinancePayablesMyWorkDocument document, string title, MyWorkPriority priority, string action) =>
		new(section, MyWorkItemKind.SupplierDocument, document.Id, document.SupplierDocumentNumber, title, document.SupplierName, document.Status.ToString(), document.GrossAmount, document.DueDate.ToDateTime(TimeOnly.MinValue), null, priority, "finance.payables", action, document.CreatedByUserId);
}

internal sealed class BankingMyWorkProvider : IMyWorkProvider
{
	private readonly FinanceBankingService _banking;
	private readonly IAuthorizationService _authorization;
	private readonly ApprovalPolicyService? _approvalPolicies;

	public BankingMyWorkProvider(FinanceBankingService banking, IAuthorizationService authorization, ApprovalPolicyService? approvalPolicies = null)
	{
		_banking = banking;
		_authorization = authorization;
		_approvalPolicies = approvalPolicies;
	}

	public string Name => "Banking";
	public bool CanQuery(IAuthorizationService authorization) => authorization.HasPermission(ApplicationPermission.FinanceBankingView);

	public async Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken)
	{
		var runsTask = _banking.SearchPaymentRunsAsync(1, query.ProviderLimit, cancellationToken);
		var unreconciledTask = _banking.SearchUnreconciledLinesAsync(null, 1, query.ProviderLimit, cancellationToken);
		await Task.WhenAll(runsTask, unreconciledTask);
		var items = new List<MyWorkItem>();

		foreach (var run in (await runsTask).Items)
		{
			if (run.Status == FinancePaymentRunStatus.Draft)
			{
				if (_banking.CanApprovePaymentRuns && run.CreatedByUserId != query.UserId && (_approvalPolicies is null || await _approvalPolicies.CanCurrentUserDecideAsync(ApprovalSubjectKind.PaymentProposal, run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)))
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
