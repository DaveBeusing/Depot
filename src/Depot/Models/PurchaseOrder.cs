// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class PurchaseOrder
{
	public long Id { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public long SupplierId { get; set; }
	public string SupplierName { get; set; } = string.Empty;
	public DateTime OrderDate { get; set; } = DateTime.Today;
	public DateTime? ExpectedDeliveryDate { get; set; }
	public string? Notes { get; set; }
	public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
	public long? CreatedByUserId { get; set; }
	public long? SubmittedByUserId { get; set; }
	public DateTime? SubmittedAtUtc { get; set; }
	public long? ApprovalDecisionByUserId { get; set; }
	public DateTime? ApprovalDecisionAtUtc { get; set; }
	public string? ApprovalComment { get; set; }
	public long? ClosedByUserId { get; set; }
	public DateTime? ClosedAtUtc { get; set; }
	public DateTime? SubmittedAtLocal => SubmittedAtUtc?.ToLocalTime();
	public DateTime? ApprovalDecisionAtLocal => ApprovalDecisionAtUtc?.ToLocalTime();
	public DateTime? ClosedAtLocal => ClosedAtUtc?.ToLocalTime();
	public string? CloseReason { get; set; }
	public string? CreatedByUserDisplay { get; set; }
	public string? SubmittedByUserDisplay { get; set; }
	public string? ApprovalDecisionByUserDisplay { get; set; }
	public string? ClosedByUserDisplay { get; set; }
	public string StatusDisplayName => Status switch
	{
		PurchaseOrderStatus.PartiallyReceived => "Partially Received",
		PurchaseOrderStatus.PendingApproval => "Pending Approval",
		_ => Status.ToString()
	};
	public long Version { get; set; } = 1;
	public IReadOnlyList<PurchaseOrderLine> Lines { get; set; } = [];
}
