// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

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

public enum WorkflowTimelineVisualState
{
	Completed,
	Current,
	Pending,
	Correction,
	Reversal,
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
	public bool IsPending { get; init; }
	public bool IsCorrection { get; init; }
	public bool IsReversal { get; init; }
	public WorkflowTimelineSeverity Severity { get; init; }

	public DateTime OccurredAtLocal => OccurredAt.Kind == DateTimeKind.Utc ? OccurredAt.ToLocalTime() : OccurredAt;
	public bool HasOccurredAt => OccurredAt != default;
	public bool CanNavigate => !IsPending && EntityId > 0 && !string.IsNullOrWhiteSpace(RouteId);
	public WorkflowTimelineVisualState VisualState =>
		IsReversal ? WorkflowTimelineVisualState.Reversal :
		IsCorrection ? WorkflowTimelineVisualState.Correction :
		Severity == WorkflowTimelineSeverity.Error ? WorkflowTimelineVisualState.Error :
		IsPending ? WorkflowTimelineVisualState.Pending :
		IsCurrent ? WorkflowTimelineVisualState.Current :
		WorkflowTimelineVisualState.Completed;

	public string VisualStateLabel => VisualState switch
	{
		WorkflowTimelineVisualState.Completed => "Completed",
		WorkflowTimelineVisualState.Current => "Current",
		WorkflowTimelineVisualState.Pending => "Pending",
		WorkflowTimelineVisualState.Correction => "Correction",
		WorkflowTimelineVisualState.Reversal => "Reversal",
		WorkflowTimelineVisualState.Error => "Exception",
		_ => "Status"
	};

	public string VisualGlyph => VisualState switch
	{
		WorkflowTimelineVisualState.Completed => "✓",
		WorkflowTimelineVisualState.Current => "●",
		WorkflowTimelineVisualState.Pending => "○",
		WorkflowTimelineVisualState.Correction => "↺",
		WorkflowTimelineVisualState.Reversal => "↶",
		WorkflowTimelineVisualState.Error => "!",
		_ => "•"
	};

	public string OccurredAtText => HasOccurredAt
		? OccurredAtLocal.ToString("g", CultureInfo.CurrentCulture)
		: "Not yet occurred";

	public string AccessibleName
	{
		get
		{
			var values = new List<string> { Title, "Document " + DisplayNumber, "Status " + Status, VisualStateLabel };
			if (HasOccurredAt) values.Add("Date " + OccurredAtText);
			return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
		}
	}
}
