// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public static class ApprovalFlowProjectionService
{
	public static ApprovalFlowProjection ProjectPurchaseOrder(PurchaseOrder order, bool canSubmit, bool canDecide, bool canOrder)
	{
		ArgumentNullException.ThrowIfNull(order);
		var status = order.Status;
		var progressedPastApproval = status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed;
		var progressedPastOrder = status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed;
		var steps = new List<WorkflowTimelineItem>
		{
			Step(WorkflowTimelineKind.PurchaseOrder, order.Id, order.OrderNumber, "Draft", "Draft", order.OrderDate, status == PurchaseOrderStatus.Draft, false, actor: order.CreatedByUserDisplay ?? User(order.CreatedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersSubmit)),
			Step(WorkflowTimelineKind.PurchaseApproval, order.Id, order.OrderNumber, "Submitted for approval", order.SubmittedAtUtc is null ? "Not submitted" : "Submitted", order.SubmittedAtUtc, false, status == PurchaseOrderStatus.Draft, actor: order.SubmittedByUserDisplay ?? User(order.SubmittedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersSubmit)),
			Step(WorkflowTimelineKind.PurchaseApproval, order.Id, order.OrderNumber, "Approval decision", status switch
			{
				PurchaseOrderStatus.Rejected => "Rejected",
				PurchaseOrderStatus.PendingApproval => "Pending Approval",
				_ when progressedPastApproval => "Approved",
				_ => "Not reached"
			}, order.ApprovalDecisionAtUtc, status is PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Rejected, status == PurchaseOrderStatus.Draft, status == PurchaseOrderStatus.Rejected ? WorkflowTimelineSeverity.Error : WorkflowTimelineSeverity.Normal, actor: order.ApprovalDecisionByUserDisplay ?? User(order.ApprovalDecisionByUserId), detail: order.ApprovalComment, requiredPermission: PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersApprove)),
			Step(WorkflowTimelineKind.PurchaseOrdered, order.Id, order.OrderNumber, "Order placement", progressedPastOrder ? status.ToString() : "Not reached", null, status == PurchaseOrderStatus.Approved, status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval, requiredPermission: PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersOrder))
		};
		if (status == PurchaseOrderStatus.Cancelled)
			steps.Add(Step(WorkflowTimelineKind.PurchaseOrder, order.Id, order.OrderNumber, "Order cancelled", "Cancelled", order.ClosedAtUtc, true, false, WorkflowTimelineSeverity.Error));

		return new ApprovalFlowProjection
		{
			Steps = steps,
			RequiredPermission = status switch
			{
				PurchaseOrderStatus.Draft => PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersSubmit),
				PurchaseOrderStatus.PendingApproval => PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersApprove),
				PurchaseOrderStatus.Approved => PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersOrder),
				_ => "None"
			},
			SeparationOfDuties = "Purchase-order creators cannot approve or reject their own order unless the existing administrator override applies.",
			NextActions = status switch
			{
				PurchaseOrderStatus.Draft => canSubmit ? "Submit for approval" : "Submission is not available to the current user.",
				PurchaseOrderStatus.PendingApproval => canDecide ? "Approve or reject" : "No approval decision is available to the current user.",
				PurchaseOrderStatus.Approved => canOrder ? "Place order" : "Order placement is not available to the current user.",
				_ => "No approval action is currently available."
			},
			RuleSummary = "Read-only projection of the existing purchase-order lifecycle. PurchaseOrderService and PurchaseOrderApprovalService remain the authorization and transition boundary."
		};
	}

	public static ApprovalFlowProjection ProjectSalesOrder(SalesOrder order, bool canSubmit, bool canDecide, bool canRelease)
	{
		ArgumentNullException.ThrowIfNull(order);
		var status = order.Status;
		var approved = status is SalesOrderStatus.Approved or SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped or SalesOrderStatus.Completed;
		var released = status is SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped or SalesOrderStatus.Completed;
		var steps = new List<WorkflowTimelineItem>
		{
			Step(WorkflowTimelineKind.SalesOrder, order.Id, order.OrderNumber, "Draft", "Draft", order.OrderDate, status == SalesOrderStatus.Draft, false, actor: User(order.CreatedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.SalesOrdersSubmit)),
			Step(WorkflowTimelineKind.SalesApproval, order.Id, order.OrderNumber, "Submitted for approval", order.SubmittedAtUtc is null ? "Not submitted" : "Submitted", order.SubmittedAtUtc, false, status == SalesOrderStatus.Draft, actor: User(order.SubmittedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.SalesOrdersSubmit)),
			Step(WorkflowTimelineKind.SalesApproval, order.Id, order.OrderNumber, "Approval decision", status switch
			{
				SalesOrderStatus.Rejected => "Rejected",
				SalesOrderStatus.PendingApproval => "Pending Approval",
				_ when approved => "Approved",
				_ => "Not reached"
			}, order.ApprovalDecisionAtUtc, status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Rejected, status == SalesOrderStatus.Draft, status == SalesOrderStatus.Rejected ? WorkflowTimelineSeverity.Error : WorkflowTimelineSeverity.Normal, actor: User(order.ApprovalDecisionByUserId), detail: order.ApprovalComment, requiredPermission: PermissionCatalog.Code(ApplicationPermission.SalesOrdersApprove)),
			Step(WorkflowTimelineKind.ReservationRelease, order.Id, order.OrderNumber, "Release", released ? status.ToString() : "Not reached", order.ReleasedAtUtc, status == SalesOrderStatus.Approved, status is SalesOrderStatus.Draft or SalesOrderStatus.PendingApproval, actor: User(order.ReleasedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.SalesOrdersRelease))
		};
		if (status == SalesOrderStatus.Cancelled)
			steps.Add(Step(WorkflowTimelineKind.SalesOrder, order.Id, order.OrderNumber, "Order cancelled", "Cancelled", order.CancelledAtUtc, true, false, WorkflowTimelineSeverity.Error, actor: User(order.CancelledByUserId), detail: order.CancelReason));

		return new ApprovalFlowProjection
		{
			Steps = steps,
			RequiredPermission = status switch
			{
				SalesOrderStatus.Draft => PermissionCatalog.Code(ApplicationPermission.SalesOrdersSubmit),
				SalesOrderStatus.PendingApproval => PermissionCatalog.Code(ApplicationPermission.SalesOrdersApprove),
				SalesOrderStatus.Approved => PermissionCatalog.Code(ApplicationPermission.SalesOrdersRelease),
				_ => "None"
			},
			SeparationOfDuties = "Sales-order creators cannot approve or reject their own order unless the existing administrator exception applies.",
			NextActions = status switch
			{
				SalesOrderStatus.Draft => canSubmit ? "Submit for approval" : "Submission is not available to the current user.",
				SalesOrderStatus.PendingApproval => canDecide ? "Approve or reject" : "No approval decision is available to the current user.",
				SalesOrderStatus.Approved => canRelease ? "Release order" : "Release is not available to the current user.",
				_ => "No approval action is currently available."
			},
			RuleSummary = "Read-only projection of the existing sales-order lifecycle. SalesOrderService remains the authorization and transition boundary."
		};
	}

	public static ApprovalFlowProjection ProjectSupplierDocument(FinanceSupplierDocument document, bool canSubmit, bool canDecide, bool canPost)
	{
		ArgumentNullException.ThrowIfNull(document);
		var status = document.Status;
		var approved = status is FinancePayableDocumentStatus.Approved or FinancePayableDocumentStatus.Posted or FinancePayableDocumentStatus.Reversed;
		var steps = new List<WorkflowTimelineItem>
		{
			Step(WorkflowTimelineKind.SupplierInvoice, document.Id, document.SupplierDocumentNumber, "Draft", "Draft", document.CreatedAtUtc, status == FinancePayableDocumentStatus.Draft, false, actor: User(document.CreatedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesSubmit)),
			Step(WorkflowTimelineKind.SupplierApproval, document.Id, document.SupplierDocumentNumber, "Submitted for approval", document.SubmittedAtUtc is null ? "Not submitted" : "Submitted", document.SubmittedAtUtc, false, status == FinancePayableDocumentStatus.Draft, actor: User(document.SubmittedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesSubmit)),
			Step(WorkflowTimelineKind.SupplierApproval, document.Id, document.SupplierDocumentNumber, "Approval decision", status switch
			{
				FinancePayableDocumentStatus.Rejected => "Rejected",
				FinancePayableDocumentStatus.PendingApproval => "Pending Approval",
				_ when approved => "Approved",
				_ => "Not reached"
			}, document.ApprovalDecisionAtUtc, status is FinancePayableDocumentStatus.PendingApproval or FinancePayableDocumentStatus.Rejected, status == FinancePayableDocumentStatus.Draft, status == FinancePayableDocumentStatus.Rejected ? WorkflowTimelineSeverity.Error : WorkflowTimelineSeverity.Normal, actor: User(document.ApprovalDecisionByUserId), detail: document.ApprovalComment, requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesApprove)),
			Step(WorkflowTimelineKind.Payable, document.Id, document.SupplierDocumentNumber, "Posting", status is FinancePayableDocumentStatus.Posted or FinancePayableDocumentStatus.Reversed ? status.ToString() : "Not reached", document.PostedAtUtc, status == FinancePayableDocumentStatus.Approved, status is FinancePayableDocumentStatus.Draft or FinancePayableDocumentStatus.PendingApproval, actor: User(document.PostedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesPost))
		};
		if (status == FinancePayableDocumentStatus.Reversed)
			steps.Add(Step(WorkflowTimelineKind.SupplierDocumentReversal, document.Id, document.SupplierDocumentNumber, "Reversal", "Reversed", document.ReversedAtUtc, true, false, WorkflowTimelineSeverity.Warning, isReversal: true, actor: User(document.ReversedByUserId)));

		return new ApprovalFlowProjection
		{
			Steps = steps,
			RequiredPermission = status switch
			{
				FinancePayableDocumentStatus.Draft => PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesSubmit),
				FinancePayableDocumentStatus.PendingApproval => PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesApprove),
				FinancePayableDocumentStatus.Approved => PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesPost),
				_ => "None"
			},
			SeparationOfDuties = "Supplier-document creators cannot decide their own document unless the existing administrator exception applies. Match-exception approval remains a separate permission.",
			NextActions = status switch
			{
				FinancePayableDocumentStatus.Draft => canSubmit ? "Submit for approval" : "Submission is not available to the current user.",
				FinancePayableDocumentStatus.PendingApproval => canDecide ? "Approve or reject" : "No approval decision is available to the current user.",
				FinancePayableDocumentStatus.Approved => canPost ? "Post supplier document" : "Posting is not available to the current user.",
				_ => "No approval action is currently available."
			},
			RuleSummary = "Read-only projection of the existing supplier-document lifecycle. FinanceAccountsPayableService remains the authorization and transition boundary."
		};
	}

	public static ApprovalFlowProjection ProjectPaymentRun(FinancePaymentRun run, bool canApprove, bool canExecute)
	{
		ArgumentNullException.ThrowIfNull(run);
		var status = run.Status;
		var approved = status is FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted or FinancePaymentRunStatus.Executed;
		var steps = new List<WorkflowTimelineItem>
		{
			Step(WorkflowTimelineKind.PaymentProposal, run.Id, $"Payment run {run.Id}", "Proposal created", "Draft", run.CreatedAtUtc, status == FinancePaymentRunStatus.Draft, false, actor: User(run.CreatedByUserId), requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinancePaymentProposalsCreate)),
			Step(WorkflowTimelineKind.PaymentProposal, run.Id, $"Payment run {run.Id}", "Approval decision", approved ? "Approved" : status == FinancePaymentRunStatus.Cancelled ? "Cancelled" : "Pending Approval", run.ApprovedAtUtc, status == FinancePaymentRunStatus.Draft, false, status == FinancePaymentRunStatus.Cancelled ? WorkflowTimelineSeverity.Error : WorkflowTimelineSeverity.Normal, actor: User(run.ApprovedByUserId), detail: run.ApprovalComment, requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinancePaymentProposalsApprove)),
			Step(WorkflowTimelineKind.PaymentExecution, run.Id, $"Payment run {run.Id}", "Execution", status is FinancePaymentRunStatus.PartiallyExecuted or FinancePaymentRunStatus.Executed ? status.ToString() : "Not reached", run.CompletedAtUtc, status == FinancePaymentRunStatus.Approved, status == FinancePaymentRunStatus.Draft, requiredPermission: PermissionCatalog.Code(ApplicationPermission.FinancePaymentRunsPost))
		};

		return new ApprovalFlowProjection
		{
			Steps = steps,
			RequiredPermission = status switch
			{
				FinancePaymentRunStatus.Draft => PermissionCatalog.Code(ApplicationPermission.FinancePaymentProposalsApprove),
				FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted => PermissionCatalog.Code(ApplicationPermission.FinancePaymentRunsPost),
				_ => "None"
			},
			SeparationOfDuties = "Payment-proposal creators cannot approve their own proposal. The existing banking service does not expose a payment-proposal rejection operation.",
			NextActions = status switch
			{
				FinancePaymentRunStatus.Draft => canApprove ? "Approve payment proposal" : "Approval is not available to the current user.",
				FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted => canExecute ? "Execute remaining payment lines" : "Execution is not available to the current user.",
				_ => "No approval action is currently available."
			},
			RuleSummary = "Read-only projection of the existing payment-run lifecycle. FinanceBankingService remains the authorization and transition boundary."
		};
	}

	private static WorkflowTimelineItem Step(
		WorkflowTimelineKind kind,
		long entityId,
		string displayNumber,
		string title,
		string status,
		DateTime? occurredAt,
		bool isCurrent,
		bool isPending,
		WorkflowTimelineSeverity severity = WorkflowTimelineSeverity.Normal,
		bool isReversal = false,
		string? actor = null,
		string? detail = null,
		string? requiredPermission = null) =>
		new()
		{
			Kind = kind,
			EntityId = entityId,
			DisplayNumber = displayNumber,
			Title = title,
			Status = status,
			OccurredAt = occurredAt ?? default,
			IsCurrent = isCurrent,
			IsPending = isPending,
			IsRejected = string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase),
			IsCancelled = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase),
			IsReversal = isReversal,
			Actor = actor,
			Detail = detail,
			RequiredPermission = requiredPermission,
			Severity = severity
		};

	private static string? User(long? userId) => userId is > 0 ? $"User #{userId.Value}" : null;
}
