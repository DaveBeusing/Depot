// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Models;

namespace Depot.DocumentRendering;

public static class ElectronicInvoiceRenderModelFactory
{
	public static DocumentRenderModel Create(ElectronicInvoice invoice)
	{
		ArgumentNullException.ThrowIfNull(invoice);

		var rows = new List<DocumentRenderRow>(invoice.Lines.Count);
		var netTotal = 0m;
		var taxTotal = 0m;
		foreach (var line in invoice.Lines)
		{
			var net = Round(line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m));
			var tax = Round(net * line.TaxRate / 100m);
			netTotal += net;
			taxTotal += tax;
			rows.Add(new DocumentRenderRow(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["Line.Item"] = line.SellerItemIdentifier ?? line.Id,
				["Line.Description"] = line.Name,
				["Line.Quantity"] = line.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
				["Line.UnitPrice"] = line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
				["Line.TaxRate"] = $"{line.TaxRate.ToString("0.##", CultureInfo.InvariantCulture)}%",
				["Line.Total"] = $"{net.ToString("0.00", CultureInfo.InvariantCulture)} {invoice.Currency}"
			}));
		}

		var grossTotal = netTotal + taxTotal;
		var isCredit = invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote;
		var values = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Company.Name"] = invoice.Seller.Name,
			["Company.Address"] = JoinAddress(invoice.Seller),
			["Company.LegalLine"] = JoinNonEmpty(invoice.Seller.RegistrationIdentifier, invoice.Seller.VatIdentifier, invoice.Seller.TaxIdentifier),
			["Company.ContactLine"] = JoinNonEmpty(invoice.Seller.ContactEmail, invoice.Seller.ContactPhone),
			["Document.Title"] = isCredit ? "CREDIT NOTE" : "INVOICE",
			["Document.Number"] = invoice.InvoiceNumber,
			["Document.Date"] = invoice.IssueDate,
			["Document.DueDate"] = invoice.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—",
			["Document.CustomerReference"] = invoice.BuyerReference,
			["Document.SalesOrderNumber"] = invoice.PurchaseOrderReference ?? "—",
			["Document.ShipmentNumber"] = "—",
			["Document.InvoiceNumber"] = invoice.PurchaseOrderReference ?? "—",
			["Document.Status"] = "Finalized",
			["Document.Reason"] = invoice.Note ?? "—",
			["Document.Currency"] = invoice.Currency,
			["Document.Net"] = Money(netTotal, invoice.Currency),
			["Document.Tax"] = Money(taxTotal, invoice.Currency),
			["Document.Total"] = Money(grossTotal, invoice.Currency),
			["Customer.Name"] = invoice.Buyer.Name,
			["Customer.Address"] = JoinAddress(invoice.Buyer),
			["Customer.BillingAddress"] = JoinAddress(invoice.Buyer),
			["Customer.ShippingAddress"] = JoinAddress(invoice.Buyer)
		};

		var title = isCredit ? "Credit Note" : "Invoice";
		return new DocumentRenderModel(
			$"{invoice.Seller.Name} - {title} {invoice.InvoiceNumber}",
			invoice.InvoiceNumber,
			invoice.Seller.Name,
			values,
			rows);
	}

	private static string JoinAddress(ElectronicInvoiceParty party) =>
		string.Join(
			Environment.NewLine,
			new[]
			{
				party.AddressLine1,
				party.AddressLine2,
				string.Join(" ", new[] { party.PostalCode, party.City }.Where(value => !string.IsNullOrWhiteSpace(value))),
				party.CountryCode
			}.Where(value => !string.IsNullOrWhiteSpace(value)));

	private static string JoinNonEmpty(params string?[] values) =>
		string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

	private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
	private static string Money(decimal value, string currency) => $"{value.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";
}
