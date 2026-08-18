// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SalesOrderStatus
{
	Draft = 1,
	PendingApproval = 2,
	Approved = 3,
	Released = 4,
	PartiallyShipped = 5,
	Shipped = 6,
	Completed = 7,
	Rejected = 8,
	Cancelled = 9
}

public sealed class SalesOrder
{
	public long Id { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public long CustomerId { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	public DateTime OrderDate { get; set; } = DateTime.Today;
	public DateTime? RequestedDeliveryDate { get; set; }
	public string Currency { get; set; } = "EUR";
	public string? CustomerReference { get; set; }
	public string? Notes { get; set; }
	public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
	public long? CreatedByUserId { get; set; }
	public long? SubmittedByUserId { get; set; }
	public DateTime? SubmittedAtUtc { get; set; }
	public long? ApprovalDecisionByUserId { get; set; }
	public DateTime? ApprovalDecisionAtUtc { get; set; }
	public string? ApprovalComment { get; set; }
	public long? ReleasedByUserId { get; set; }
	public DateTime? ReleasedAtUtc { get; set; }
	public long? CancelledByUserId { get; set; }
	public DateTime? CancelledAtUtc { get; set; }
	public string? CancelReason { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<SalesOrderLine> Lines { get; set; } = [];
	public decimal NetAmount => Lines.Sum(line => line.NetAmount);
	public decimal TaxAmount => Lines.Sum(line => line.TaxAmount);
	public decimal GrossAmount => Lines.Sum(line => line.GrossAmount);
}

public sealed class SalesOrderLine
{
	public long Id { get; set; }
	public long SalesOrderId { get; set; }
	public int LineNumber { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal DiscountPercent { get; set; }
	public decimal TaxRate { get; set; } = 19m;
	public int ReservedQuantity { get; set; }
	public int ShippedQuantity { get; set; }
	public int InvoicedQuantity { get; set; }
	public long Version { get; set; } = 1;
	public decimal NetAmount => Math.Round(Quantity * UnitPrice * (1m - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
	public decimal TaxAmount => Math.Round(NetAmount * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
	public decimal GrossAmount => NetAmount + TaxAmount;
	public int OpenQuantity => Math.Max(0, Quantity - ShippedQuantity);
	public int BackorderedQuantity => Math.Max(0, OpenQuantity - ReservedQuantity);
}
