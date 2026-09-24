// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum PurchaseRequisitionStatus
{
	Draft = 1,
	Submitted = 2,
	Approved = 3,
	Returned = 4,
	Rejected = 5,
	Converted = 6,
	Cancelled = 7
}

public enum RequestForQuotationStatus
{
	Draft = 1,
	Open = 2,
	Awarded = 3,
	Converted = 4,
	Cancelled = 5
}

public enum SupplierQuoteResponseStatus
{
	Active = 1,
	Withdrawn = 2,
	Expired = 3
}

public sealed class PurchaseRequisition
{
	public long Id { get; set; }
	public string RequisitionNumber { get; set; } = string.Empty;
	public PurchaseRequisitionStatus Status { get; set; } = PurchaseRequisitionStatus.Draft;
	public long RequestedByUserId { get; set; }
	public string? RequestedByUserDisplay { get; set; }
	public long CreatedByUserId { get; set; }
	public string? CreatedByUserDisplay { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? SubmittedAtUtc { get; set; }
	public DateTime? ApprovalDecisionAtUtc { get; set; }
	public long? ApprovalDecisionByUserId { get; set; }
	public string? ApprovalDecisionByUserDisplay { get; set; }
	public string? ApprovalComment { get; set; }
	public DateTime? RequiredByDate { get; set; }
	public long? PreferredSupplierId { get; set; }
	public string? PreferredSupplierName { get; set; }
	public string? BusinessJustification { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<PurchaseRequisitionLine> Lines { get; set; } = [];
}

public sealed class PurchaseRequisitionLine
{
	public long Id { get; set; }
	public long PurchaseRequisitionId { get; set; }
	public int LineNumber { get; set; }
	public long ItemId { get; set; }
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public string? Notes { get; set; }
	public long Version { get; set; } = 1;
}

public sealed class RequestForQuotation
{
	public long Id { get; set; }
	public string RfqNumber { get; set; } = string.Empty;
	public long PurchaseRequisitionId { get; set; }
	public string RequisitionNumber { get; set; } = string.Empty;
	public RequestForQuotationStatus Status { get; set; } = RequestForQuotationStatus.Draft;
	public DateTime CreatedAtUtc { get; set; }
	public long CreatedByUserId { get; set; }
	public DateTime? ResponseDueDate { get; set; }
	public long? SelectedQuoteResponseId { get; set; }
	public long? SelectedByUserId { get; set; }
	public DateTime? SelectedAtUtc { get; set; }
	public long? ConvertedPurchaseOrderId { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<RequestForQuotationSupplier> Suppliers { get; set; } = [];
	public IReadOnlyList<RequestForQuotationLine> Lines { get; set; } = [];
}

public sealed record RequestForQuotationSupplier(long RequestForQuotationId, long SupplierId, string SupplierName);

public sealed class RequestForQuotationLine
{
	public long Id { get; set; }
	public long RequestForQuotationId { get; set; }
	public long PurchaseRequisitionLineId { get; set; }
	public long ItemId { get; set; }
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public DateTime? RequiredByDate { get; set; }
	public long Version { get; set; } = 1;
}

public sealed class SupplierQuoteResponse
{
	public long Id { get; set; }
	public long RequestForQuotationId { get; set; }
	public long SupplierId { get; set; }
	public string SupplierName { get; set; } = string.Empty;
	public string? SupplierReference { get; set; }
	public string Currency { get; set; } = string.Empty;
	public DateTime? ValidUntil { get; set; }
	public DateTime ReceivedAtUtc { get; set; }
	public long CapturedByUserId { get; set; }
	public SupplierQuoteResponseStatus Status { get; set; } = SupplierQuoteResponseStatus.Active;
	public long Version { get; set; } = 1;
	public IReadOnlyList<SupplierQuoteResponseLine> Lines { get; set; } = [];
	public decimal TotalAmount => Lines.Sum(line => line.UnitPrice * line.Quantity);
}

public sealed class SupplierQuoteResponseLine
{
	public long Id { get; set; }
	public long SupplierQuoteResponseId { get; set; }
	public long RequestForQuotationLineId { get; set; }
	public long ItemId { get; set; }
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public int? MinimumOrderQuantity { get; set; }
	public int? LeadTimeDays { get; set; }
	public long Version { get; set; } = 1;
}

public sealed record SupplierQuoteComparisonRow(
	long RequestForQuotationLineId,
	long ItemId,
	string PartNumber,
	string Description,
	int RequestedQuantity,
	long SupplierQuoteResponseId,
	long SupplierId,
	string SupplierName,
	string Currency,
	decimal UnitPrice,
	decimal LineTotal,
	int? MinimumOrderQuantity,
	int? LeadTimeDays,
	DateTime? ValidUntil,
	bool IsExpired,
	bool IsSelected);

public sealed record ProcurementSourcingEvidence(
	long PurchaseOrderId,
	long PurchaseRequisitionId,
	long RequestForQuotationId,
	long SupplierQuoteResponseId,
	long SelectedByUserId,
	DateTime SelectedAtUtc);
