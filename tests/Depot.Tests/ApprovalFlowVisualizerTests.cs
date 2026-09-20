// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ApprovalFlowVisualizerTests
{
	[Fact]
	public void PurchaseProjectionShowsPendingDecisionAndServiceCapability()
	{
		var order = new PurchaseOrder
		{
			Id = 17,
			OrderNumber = "PO-17",
			OrderDate = new DateTime(2026, 9, 20),
			Status = PurchaseOrderStatus.PendingApproval,
			CreatedByUserId = 7,
			CreatedByUserDisplay = "Creator",
			SubmittedByUserId = 7,
			SubmittedByUserDisplay = "Creator",
			SubmittedAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc)
		};

		var flow = ApprovalFlowProjectionService.ProjectPurchaseOrder(order, canSubmit: false, canDecide: false, canOrder: false);
		var decision = Assert.Single(flow.Steps, step => step.Title == "Approval decision");

		Assert.True(decision.IsCurrent);
		Assert.Equal(WorkflowTimelineVisualState.Current, decision.VisualState);
		Assert.Equal(PermissionCatalog.Code(ApplicationPermission.PurchaseOrdersApprove), flow.RequiredPermission);
		Assert.Contains("No approval decision", flow.NextActions, StringComparison.Ordinal);
		Assert.Contains("creators cannot approve", flow.SeparationOfDuties, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RejectedPurchaseProjectionCarriesActorCommentAndRejectedState()
	{
		var order = new PurchaseOrder
		{
			Id = 21,
			OrderNumber = "PO-21",
			OrderDate = DateTime.Today,
			Status = PurchaseOrderStatus.Rejected,
			ApprovalDecisionByUserId = 9,
			ApprovalDecisionByUserDisplay = "Approver",
			ApprovalDecisionAtUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
			ApprovalComment = "Budget limit exceeded."
		};

		var flow = ApprovalFlowProjectionService.ProjectPurchaseOrder(order, false, false, false);
		var decision = Assert.Single(flow.Steps, step => step.Title == "Approval decision");

		Assert.Equal(WorkflowTimelineVisualState.Rejected, decision.VisualState);
		Assert.Equal("Approver", decision.Actor);
		Assert.Equal("Budget limit exceeded.", decision.Detail);
		Assert.Contains("Actor Approver", decision.AccessibleName, StringComparison.Ordinal);
	}

	[Fact]
	public void SalesProjectionDistinguishesCancelledAndCompletedStates()
	{
		var cancelled = new SalesOrder
		{
			Id = 31,
			OrderNumber = "SO-31",
			OrderDate = DateTime.Today,
			Status = SalesOrderStatus.Cancelled,
			CancelledByUserId = 5,
			CancelledAtUtc = new DateTime(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc),
			CancelReason = "Customer request"
		};
		var completed = new SalesOrder { Id = 32, OrderNumber = "SO-32", OrderDate = DateTime.Today, Status = SalesOrderStatus.Completed };

		var cancelledFlow = ApprovalFlowProjectionService.ProjectSalesOrder(cancelled, false, false, false);
		var completedFlow = ApprovalFlowProjectionService.ProjectSalesOrder(completed, false, false, false);

		Assert.Equal(WorkflowTimelineVisualState.Cancelled, Assert.Single(cancelledFlow.Steps, step => step.Status == "Cancelled").VisualState);
		Assert.Contains(completedFlow.Steps, step => step.Title == "Approval decision" && step.VisualState == WorkflowTimelineVisualState.Completed);
		Assert.Contains(completedFlow.Steps, step => step.Title == "Release" && step.VisualState == WorkflowTimelineVisualState.Completed);
	}

	[Fact]
	public void SupplierDocumentProjectionKeepsApprovalAndPostingSeparate()
	{
		var document = new FinanceSupplierDocument
		{
			Id = 41,
			SupplierId = 3,
			SupplierDocumentNumber = "INV-41",
			DocumentDate = new DateOnly(2026, 9, 20),
			DueDate = new DateOnly(2026, 10, 20),
			Currency = new CurrencyCode("EUR"),
			Status = FinancePayableDocumentStatus.Approved,
			CreatedByUserId = 4,
			CreatedAtUtc = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc),
			ApprovalDecisionByUserId = 6,
			ApprovalDecisionAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc)
		};

		var flow = ApprovalFlowProjectionService.ProjectSupplierDocument(document, false, false, canPost: true);

		Assert.Equal(PermissionCatalog.Code(ApplicationPermission.FinanceSupplierInvoicesPost), flow.RequiredPermission);
		Assert.Equal("Post supplier document", flow.NextActions);
		Assert.Contains(flow.Steps, step => step.Title == "Approval decision" && step.VisualState == WorkflowTimelineVisualState.Completed);
		Assert.Contains(flow.Steps, step => step.Title == "Posting" && step.VisualState == WorkflowTimelineVisualState.Current);
	}

	[Fact]
	public void PaymentProposalProjectionMatchesExistingApprovalAndExecutionRules()
	{
		var draft = new FinancePaymentRun
		{
			Id = 51,
			OperationId = Guid.NewGuid(),
			PaymentDate = new DateOnly(2026, 9, 21),
			Currency = new CurrencyCode("EUR"),
			Description = "Supplier run",
			Status = FinancePaymentRunStatus.Draft,
			CreatedAtUtc = DateTime.UtcNow,
			CreatedByUserId = 12
		};
		var approved = draft with { Status = FinancePaymentRunStatus.Approved, ApprovedAtUtc = DateTime.UtcNow, ApprovedByUserId = 14 };

		var blocked = ApprovalFlowProjectionService.ProjectPaymentRun(draft, canApprove: false, canExecute: false);
		var executable = ApprovalFlowProjectionService.ProjectPaymentRun(approved, canApprove: false, canExecute: true);

		Assert.Equal(PermissionCatalog.Code(ApplicationPermission.FinancePaymentProposalsApprove), blocked.RequiredPermission);
		Assert.Contains("not available", blocked.NextActions, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("does not expose a payment-proposal rejection operation", blocked.SeparationOfDuties, StringComparison.Ordinal);
		Assert.Equal(PermissionCatalog.Code(ApplicationPermission.FinancePaymentRunsPost), executable.RequiredPermission);
		Assert.Equal("Execute remaining payment lines", executable.NextActions);
	}

	[Fact]
	public void TimelineMetadataIsAccessibleWithoutRelyingOnColor()
	{
		var item = new WorkflowTimelineItem
		{
			Title = "Approval decision",
			DisplayNumber = "DOC-7",
			Status = "Rejected",
			IsRejected = true,
			Actor = "Approver",
			Detail = "Reason",
			RequiredPermission = "Orders.Approve"
		};

		Assert.Equal(WorkflowTimelineVisualState.Rejected, item.VisualState);
		Assert.Equal("×", item.VisualGlyph);
		Assert.Contains("Actor: Approver", item.MetadataText, StringComparison.Ordinal);
		Assert.Contains("Required permission Orders.Approve", item.AccessibleName, StringComparison.Ordinal);
	}

	[Fact]
	public void ViewModelsConsumeServiceDecisionFactsInsteadOfImplementingAuthorizationInXaml()
	{
		var root = FindRepositoryRoot();
		var purchase = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "PurchaseOrderApprovalsViewModel.cs"));
		var sales = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "SalesViewModel.cs"));
		var payables = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinancePayablesViewModel.cs"));
		var banking = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinanceBankingViewModel.cs"));

		Assert.Contains("_approvals.CanDecide(value.Order.CreatedByUserId)", purchase, StringComparison.Ordinal);
		Assert.Contains("_orders.CanDecide(order.CreatedByUserId)", sales, StringComparison.Ordinal);
		Assert.Contains("_payables.CanDecide(value.CreatedByUserId)", payables, StringComparison.Ordinal);
		Assert.Contains("_banking.CanApprovePaymentRun(value.CreatedByUserId)", banking, StringComparison.Ordinal);
	}

	[Fact]
	public void ApprovalUxReusesWorkflowTimelineAndRemainsReadOnlyAndSchemaFree()
	{
		var root = FindRepositoryRoot();
		var controls = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));
		var projection = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "ApprovalFlowProjectionService.cs"));
		var views = new[]
		{
			Path.Combine(root, "src", "Depot", "Views", "PurchaseOrderApprovalsView.xaml"),
			Path.Combine(root, "src", "Depot", "Views", "SalesApprovalsView.xaml"),
			Path.Combine(root, "src", "Depot", "Views", "FinancePayablesView.xaml"),
			Path.Combine(root, "src", "Depot", "Views", "FinanceBankingView.xaml")
		};

		Assert.Contains("<controls:WorkflowTimeline ItemsSource=\"{Binding Projection.Steps", controls, StringComparison.Ordinal);
		Assert.Contains("Required permission", controls, StringComparison.Ordinal);
		Assert.Contains("Separation of duties", controls, StringComparison.Ordinal);
		Assert.Contains("Allowed next action", controls, StringComparison.Ordinal);
		foreach (var path in views) Assert.Contains("ApprovalFlowVisualizer", File.ReadAllText(path), StringComparison.Ordinal);
		Assert.DoesNotContain("CREATE TABLE", projection, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("ALTER TABLE", projection, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("SaveApproval", projection, StringComparison.Ordinal);
		Assert.DoesNotContain("ConditionEditor", controls, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
		}

		throw new DirectoryNotFoundException("Could not locate the Depot repository root from the test output directory.");
	}
}
