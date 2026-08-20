// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SalesQuoteStatus
{
	Draft = 1,
	Sent = 2,
	Accepted = 3,
	Rejected = 4,
	Expired = 5,
	Converted = 6,
	Cancelled = 7
}

public sealed class SalesQuote
{
	public long Id { get; set; }
	public string QuoteNumber { get; set; } = string.Empty;
	public long CustomerId { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	public string? BillingAddress { get; set; }
	public string? ShippingAddress { get; set; }
	public long? ContactId { get; set; }
	public string? ContactName { get; set; }
	public DateTime QuoteDate { get; set; } = DateTime.Today;
	public DateTime ValidUntil { get; set; } = DateTime.Today.AddDays(30);
	public string Currency { get; set; } = "EUR";
	public string? CustomerReference { get; set; }
	public string? Notes { get; set; }
	public SalesQuoteStatus Status { get; set; } = SalesQuoteStatus.Draft;
	public long CreatedByUserId { get; set; }
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
	public long? ConvertedSalesOrderId { get; set; }
	public DateTime? ConvertedAtUtc { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<SalesQuoteLine> Lines { get; set; } = [];
	public decimal NetAmount => Lines.Sum(line => line.NetAmount);
	public decimal TaxAmount => Lines.Sum(line => line.TaxAmount);
	public decimal GrossAmount => Lines.Sum(line => line.GrossAmount);
}

public sealed class SalesQuoteLine
{
	public long Id { get; set; }
	public long SalesQuoteId { get; set; }
	public int LineNumber { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal DiscountPercent { get; set; }
	public decimal TaxRate { get; set; } = 19m;
	public long Version { get; set; } = 1;
	public decimal NetAmount => Math.Round(Quantity * UnitPrice * (1m - DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
	public decimal TaxAmount => Math.Round(NetAmount * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
	public decimal GrossAmount => NetAmount + TaxAmount;
}
