// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ElectronicInvoiceTypeCode
{
	Invoice = 380,
	CreditNote = 381
}

public sealed class ElectronicInvoice
{
	public string InvoiceNumber { get; init; } = string.Empty;
	public ElectronicInvoiceTypeCode TypeCode { get; init; } = ElectronicInvoiceTypeCode.Invoice;
	public DateOnly IssueDate { get; init; }
	public DateOnly? DueDate { get; init; }
	public DateOnly? ActualDeliveryDate { get; init; }
	public string Currency { get; init; } = "EUR";
	public string BuyerReference { get; init; } = string.Empty;
	public string BusinessProcessId { get; init; } = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";
	public string? PurchaseOrderReference { get; init; }
	public ElectronicInvoiceParty Seller { get; init; } = new();
	public ElectronicInvoiceParty Buyer { get; init; } = new();
	public ElectronicInvoicePayment Payment { get; init; } = new();
	public IReadOnlyList<ElectronicInvoiceLine> Lines { get; init; } = [];
	public string? Note { get; init; }
}

public sealed class ElectronicInvoiceParty
{
	public string Name { get; init; } = string.Empty;
	public string? TradingName { get; init; }
	public string? ElectronicAddress { get; init; }
	public string? ElectronicAddressScheme { get; init; }
	public string? TaxIdentifier { get; init; }
	public string? VatIdentifier { get; init; }
	public string? RegistrationIdentifier { get; init; }
	public string? RegistrationIdentifierScheme { get; init; }
	public string AddressLine1 { get; init; } = string.Empty;
	public string? AddressLine2 { get; init; }
	public string City { get; init; } = string.Empty;
	public string PostalCode { get; init; } = string.Empty;
	public string CountryCode { get; init; } = "DE";
	public string? ContactName { get; init; }
	public string? ContactEmail { get; init; }
	public string? ContactPhone { get; init; }
}

public sealed class ElectronicInvoicePayment
{
	public string MeansCode { get; init; } = "58";
	public string? AccountIdentifier { get; init; }
	public string? AccountName { get; init; }
	public string? FinancialInstitutionIdentifier { get; init; }
	public string? PaymentReference { get; init; }
	public string? Terms { get; init; }
}

public sealed class ElectronicInvoiceLine
{
	public string Id { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string? Description { get; init; }
	public decimal Quantity { get; init; }
	public string UnitCode { get; init; } = "C62";
	public decimal UnitPrice { get; init; }
	public decimal DiscountPercent { get; init; }
	public decimal TaxRate { get; init; } = 19m;
	public string TaxCategoryCode { get; init; } = "S";
	public string? SellerItemIdentifier { get; init; }
	public string? BuyerItemIdentifier { get; init; }
}
