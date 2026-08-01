// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record WorkflowOperation
{
	public WorkflowOperation(Guid operationId, string workflow, long entityId)
	{
		if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
		if (string.IsNullOrWhiteSpace(workflow)) throw new ArgumentException("A workflow name is required.", nameof(workflow));
		if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
		OperationId = operationId;
		Workflow = workflow;
		EntityId = entityId;
	}

	public Guid OperationId { get; }
	public string Workflow { get; }
	public long EntityId { get; }
}

public static class WorkflowOperationNames
{
	public const string ApprovePurchaseOrder = "PurchaseOrder.Approve";
	public const string RejectPurchaseOrder = "PurchaseOrder.Reject";
	public const string PlacePurchaseOrder = "PurchaseOrder.Place";
	public const string ClosePurchaseOrder = "PurchaseOrder.Close";
	public const string PostMaterialIssue = "MaterialIssue.Post";
	public const string PostMaterialReturn = "MaterialReturn.Post";
	public const string PostSupplierReturn = "SupplierReturn.Post";
}
