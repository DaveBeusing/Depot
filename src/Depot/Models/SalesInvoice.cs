// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SalesInvoiceStatus
{
	Draft = 1,
	Posted = 2,
	Cancelled = 3
}

public sealed class SalesInvoice
{
	public long Id { get; set; }
	public string InvoiceNumber { get; set; } = string.Empty;
	public long CustomerId { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	public long SalesOrderId { get; set; }
	public string SalesOrderNumber { get; set; } = string.Empty;
	public long ShipmentId { get; set; }
	public string ShipmentNumber { get; set; } = string.Empty;
	public DateTime InvoiceDate { get; set; } = DateTime.Today;
	public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
	public string Currency { get; set; } = "EUR";
	public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Draft;
	public string? CustomerReference { get; set; }
	public string? BillingAddress { get; set; }
	public string? Notes { get; set; }
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<SalesInvoiceLine> Lines { get; set; } = [];
	public decimal NetAmount => Lines.Sum(line => line.NetAmount);
	public decimal TaxAmount => Lines.Sum(line => line.TaxAmount);
	public decimal GrossAmount => Lines.Sum(line => line.GrossAmount);
}

public sealed class SalesInvoiceLine
{
	public long Id { get; set; }
	public long SalesInvoiceId { get; set; }
	public int LineNumber { get; set; }
	public long SalesOrderLineId { get; set; }
	public long ShipmentLineId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal DiscountPercent { get; set; }
	public decimal TaxRate { get; set; }
	public long Version { get; set; } = 1;
	public decimal NetAmount => Math.Round(Quantity * UnitPrice * (1m - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
	public decimal TaxAmount => Math.Round(NetAmount * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
	public decimal GrossAmount => NetAmount + TaxAmount;
}
