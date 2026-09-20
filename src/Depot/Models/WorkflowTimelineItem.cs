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
	SupplierPaymentReversal,
	PaymentProposal,
	PaymentExecution
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
	Rejected,
	Cancelled,
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
	public bool IsRejected { get; init; }
	public bool IsCancelled { get; init; }
	public bool IsCorrection { get; init; }
	public bool IsReversal { get; init; }
	public string? Actor { get; init; }
	public string? Detail { get; init; }
	public string? RequiredPermission { get; init; }
	public WorkflowTimelineSeverity Severity { get; init; }

	public DateTime OccurredAtLocal => OccurredAt.Kind == DateTimeKind.Utc ? OccurredAt.ToLocalTime() : OccurredAt;
	public bool HasOccurredAt => OccurredAt != default;
	public bool CanNavigate => !IsPending && EntityId > 0 && !string.IsNullOrWhiteSpace(RouteId);
	public WorkflowTimelineVisualState VisualState =>
		IsReversal ? WorkflowTimelineVisualState.Reversal :
		IsCorrection ? WorkflowTimelineVisualState.Correction :
		IsRejected ? WorkflowTimelineVisualState.Rejected :
		IsCancelled ? WorkflowTimelineVisualState.Cancelled :
		Severity == WorkflowTimelineSeverity.Error ? WorkflowTimelineVisualState.Error :
		IsPending ? WorkflowTimelineVisualState.Pending :
		IsCurrent ? WorkflowTimelineVisualState.Current :
		WorkflowTimelineVisualState.Completed;

	public string VisualStateLabel => VisualState switch
	{
		WorkflowTimelineVisualState.Completed => "Completed",
		WorkflowTimelineVisualState.Current => "Current",
		WorkflowTimelineVisualState.Pending => "Pending",
		WorkflowTimelineVisualState.Rejected => "Rejected",
		WorkflowTimelineVisualState.Cancelled => "Cancelled",
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
		WorkflowTimelineVisualState.Rejected => "×",
		WorkflowTimelineVisualState.Cancelled => "—",
		WorkflowTimelineVisualState.Correction => "↺",
		WorkflowTimelineVisualState.Reversal => "↶",
		WorkflowTimelineVisualState.Error => "!",
		_ => "•"
	};

	public string OccurredAtText => HasOccurredAt
		? OccurredAtLocal.ToString("g", CultureInfo.CurrentCulture)
		: "Not yet occurred";
	public bool HasMetadata => !string.IsNullOrWhiteSpace(Actor) || !string.IsNullOrWhiteSpace(RequiredPermission) || !string.IsNullOrWhiteSpace(Detail);
	public string MetadataText => string.Join(" · ", new[]
	{
		string.IsNullOrWhiteSpace(Actor) ? null : "Actor: " + Actor,
		string.IsNullOrWhiteSpace(RequiredPermission) ? null : "Permission: " + RequiredPermission,
		string.IsNullOrWhiteSpace(Detail) ? null : Detail
	}.Where(value => !string.IsNullOrWhiteSpace(value)));

	public string AccessibleName
	{
		get
		{
			var values = new List<string> { Title, "Document " + DisplayNumber, "Status " + Status, VisualStateLabel };
			if (HasOccurredAt) values.Add("Date " + OccurredAtText);
			if (!string.IsNullOrWhiteSpace(Actor)) values.Add("Actor " + Actor);
			if (!string.IsNullOrWhiteSpace(RequiredPermission)) values.Add("Required permission " + RequiredPermission);
			if (!string.IsNullOrWhiteSpace(Detail)) values.Add(Detail);
			return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
		}
	}
}
