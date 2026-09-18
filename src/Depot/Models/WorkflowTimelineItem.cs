// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum WorkflowTimelineKind
{
	SalesQuote,
	SalesOrder,
	SalesApproval,
	ReservationRelease,
	Shipment,
	ShipmentReversal,
	SalesInvoice,
	SalesCreditNote,
	CustomerReturn,
	Receivable,
	ReceivablePayment,
	ReceivablePaymentReversal,
	PurchaseOrder,
	PurchaseApproval,
	PurchaseOrdered,
	GoodsReceipt,
	GoodsReceiptReversal,
	SupplierInvoice,
	SupplierMatch,
	SupplierApproval,
	SupplierDocumentReversal,
	Payable,
	SupplierPayment,
	SupplierPaymentReversal
}

public enum WorkflowTimelineSeverity
{
	Normal,
	Information,
	Success,
	Warning,
	Error
}

public sealed record WorkflowTimelineItem
{
	public WorkflowTimelineKind Kind { get; init; }
	public long EntityId { get; init; }
	public string DisplayNumber { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string Status { get; init; } = string.Empty;
	public DateTime OccurredAt { get; init; }
	public string? RouteId { get; init; }
	public bool IsCurrent { get; init; }
	public bool IsCorrection { get; init; }
	public bool IsReversal { get; init; }
	public WorkflowTimelineSeverity Severity { get; init; }

	public DateTime OccurredAtLocal => OccurredAt.Kind == DateTimeKind.Utc ? OccurredAt.ToLocalTime() : OccurredAt;
	public bool CanNavigate => EntityId > 0 && !string.IsNullOrWhiteSpace(RouteId);
}
