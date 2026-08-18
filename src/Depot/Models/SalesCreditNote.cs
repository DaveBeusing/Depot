// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SalesCreditNoteStatus
{
	Draft = 1,
	Posted = 2
}

public sealed class SalesCreditNote
{
	public long Id { get; set; }
	public string CreditNoteNumber { get; set; } = string.Empty;
	public long SalesInvoiceId { get; set; }
	public long CustomerId { get; set; }
	public DateTime CreditDate { get; set; } = DateTime.Today;
	public SalesCreditNoteStatus Status { get; set; } = SalesCreditNoteStatus.Draft;
	public string Reason { get; set; } = string.Empty;
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<SalesCreditNoteLine> Lines { get; set; } = [];
	public decimal NetAmount => Lines.Sum(line => line.NetAmount);
	public decimal TaxAmount => Lines.Sum(line => line.TaxAmount);
	public decimal GrossAmount => NetAmount + TaxAmount;
}

public sealed class SalesCreditNoteLine
{
	public long Id { get; set; }
	public long SalesCreditNoteId { get; set; }
	public long SalesInvoiceLineId { get; set; }
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal DiscountPercent { get; set; }
	public decimal TaxRate { get; set; }
	public decimal NetAmount => Math.Round(Quantity * UnitPrice * (1m - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
	public decimal TaxAmount => Math.Round(NetAmount * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
	public long Version { get; set; } = 1;
}
