// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Models;

namespace Depot.DocumentRendering;

public static class BusinessDocumentRenderModelFactory
{
	public static DocumentRenderModel Create(DocumentIssuerProfile issuer, SalesQuote quote)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(quote);
		var values = BaseValues(issuer, "QUOTE", quote.QuoteNumber, quote.QuoteDate, quote.CustomerName, quote.BillingAddress);
		values["Document.ValidUntil"] = quote.ValidUntil;
		values["Document.Contact"] = quote.ContactName ?? "—";
		values["Document.CustomerReference"] = quote.CustomerReference ?? "—";
		values["Document.Status"] = quote.Status.ToString();
		values["Document.Currency"] = quote.Currency;
		AddTotals(values, quote.NetAmount, quote.TaxAmount, quote.GrossAmount, quote.Currency);
		return CreateModel(issuer, "Sales Quote", quote.QuoteNumber, values, quote.Lines.Select(line => FinancialRow(line.PartNumber, line.Description, line.Quantity, line.UnitPrice, line.TaxRate, line.GrossAmount, quote.Currency)));
	}

	public static DocumentRenderModel Create(DocumentIssuerProfile issuer, SalesOrder order)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(order);
		var values = BaseValues(issuer, "ORDER CONFIRMATION", order.OrderNumber, order.OrderDate, order.CustomerName, order.BillingAddress);
		values["Document.CustomerReference"] = order.CustomerReference ?? "—";
		values["Document.RequestedDeliveryDate"] = order.RequestedDeliveryDate?.ToString("d", CultureInfo.CurrentCulture) ?? "—";
		values["Document.Status"] = order.Status.ToString();
		values["Document.Currency"] = order.Currency;
		AddTotals(values, order.NetAmount, order.TaxAmount, order.GrossAmount, order.Currency);
		return CreateModel(issuer, "Order Confirmation", order.OrderNumber, values, order.Lines.Select(line => FinancialRow(line.PartNumber, line.Description, line.Quantity, line.UnitPrice, line.TaxRate, line.GrossAmount, order.Currency)));
	}

	public static DocumentRenderModel CreatePickList(DocumentIssuerProfile issuer, Shipment shipment)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(shipment);
		var values = BaseValues(issuer, "PICK LIST", shipment.ShipmentNumber, shipment.ShipmentDate, shipment.CustomerName, shipment.ShippingAddress);
		values["Document.SalesOrderNumber"] = shipment.SalesOrderNumber;
		values["Document.PackingStatus"] = shipment.PackingStatus.ToString();
		values["Document.Carrier"] = shipment.Carrier ?? "—";
		values["Document.TrackingNumber"] = shipment.TrackingNumber ?? "—";
		return CreateModel(issuer, "Pick List", shipment.ShipmentNumber, values, shipment.Lines.Select(ShipmentRow));
	}

	public static DocumentRenderModel CreatePackingSlip(DocumentIssuerProfile issuer, Shipment shipment)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(shipment);
		var values = BaseValues(issuer, "PACKING SLIP", shipment.ShipmentNumber, shipment.ShipmentDate, shipment.CustomerName, shipment.ShippingAddress);
		values["Document.SalesOrderNumber"] = shipment.SalesOrderNumber;
		values["Document.Carrier"] = shipment.Carrier ?? "—";
		values["Document.TrackingNumber"] = shipment.TrackingNumber ?? "—";
		values["Document.PackedAt"] = shipment.PackedAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
		return CreateModel(issuer, "Packing Slip", shipment.ShipmentNumber, values, shipment.Lines.Select(ShipmentRow));
	}

	public static DocumentRenderModel CreateDeliveryNote(DocumentIssuerProfile issuer, Shipment shipment)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(shipment);
		var values = BaseValues(issuer, "DELIVERY NOTE", shipment.ShipmentNumber, shipment.ShipmentDate, shipment.CustomerName, shipment.ShippingAddress);
		values["Document.SalesOrderNumber"] = shipment.SalesOrderNumber;
		values["Document.Carrier"] = shipment.Carrier ?? "—";
		values["Document.TrackingNumber"] = shipment.TrackingNumber ?? "—";
		values["Document.Status"] = shipment.Status.ToString();
		return CreateModel(issuer, "Delivery Note", shipment.ShipmentNumber, values, shipment.Lines.Select(ShipmentRow));
	}

	public static DocumentRenderModel Create(DocumentIssuerProfile issuer, SalesInvoice invoice)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(invoice);
		var values = BaseValues(issuer, "INVOICE", invoice.InvoiceNumber, invoice.InvoiceDate, invoice.CustomerName, invoice.BillingAddress);
		values["Document.SalesOrderNumber"] = invoice.SalesOrderNumber;
		values["Document.ShipmentNumber"] = invoice.ShipmentNumber;
		values["Document.CustomerReference"] = invoice.CustomerReference ?? "—";
		values["Document.DueDate"] = invoice.DueDate;
		values["Document.Currency"] = invoice.Currency;
		values["Document.Status"] = invoice.Status.ToString();
		AddTotals(values, invoice.NetAmount, invoice.TaxAmount, invoice.GrossAmount, invoice.Currency);
		return CreateModel(issuer, "Sales Invoice", invoice.InvoiceNumber, values, invoice.Lines.Select(line => FinancialRow(line.PartNumber, line.Description, line.Quantity, line.UnitPrice, line.TaxRate, line.GrossAmount, invoice.Currency)));
	}

	public static DocumentRenderModel Create(DocumentIssuerProfile issuer, SalesCreditNote creditNote, SalesInvoice invoice)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(creditNote);
		ArgumentNullException.ThrowIfNull(invoice);
		var values = BaseValues(issuer, "CREDIT NOTE", creditNote.CreditNoteNumber, creditNote.CreditDate, invoice.CustomerName, invoice.BillingAddress);
		values["Document.InvoiceNumber"] = invoice.InvoiceNumber;
		values["Document.SalesOrderNumber"] = invoice.SalesOrderNumber;
		values["Document.Status"] = creditNote.Status.ToString();
		values["Document.Reason"] = creditNote.Reason;
		values["Document.Currency"] = invoice.Currency;
		AddTotals(values, -creditNote.NetAmount, -creditNote.TaxAmount, -creditNote.GrossAmount, invoice.Currency);
		var invoiceLines = invoice.Lines.ToDictionary(line => line.Id);
		var rows = creditNote.Lines.Select(line =>
		{
			invoiceLines.TryGetValue(line.SalesInvoiceLineId, out var source);
			return FinancialRow(
				source?.PartNumber ?? $"Line {line.SalesInvoiceLineId}",
				source?.Description ?? "Credited invoice line",
				line.Quantity,
				line.UnitPrice,
				line.TaxRate,
				-line.GrossAmount,
				invoice.Currency);
		});
		return CreateModel(issuer, "Credit Note", creditNote.CreditNoteNumber, values, rows);
	}

	public static DocumentRenderModel Create(DocumentIssuerProfile issuer, CustomerReturn customerReturn, Shipment shipment)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(customerReturn);
		ArgumentNullException.ThrowIfNull(shipment);
		var values = BaseValues(issuer, "CUSTOMER RETURN", customerReturn.ReturnNumber, customerReturn.ReturnDate, shipment.CustomerName, shipment.ShippingAddress);
		values["Document.ShipmentNumber"] = shipment.ShipmentNumber;
		values["Document.SalesOrderNumber"] = shipment.SalesOrderNumber;
		values["Document.Status"] = customerReturn.Status.ToString();
		values["Document.Reason"] = customerReturn.Reason;
		var shipmentLines = shipment.Lines.ToDictionary(line => line.Id);
		var rows = customerReturn.Lines.Select(line =>
		{
			shipmentLines.TryGetValue(line.ShipmentLineId, out var source);
			return new DocumentRenderRow(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["Line.Item"] = source?.PartNumber ?? $"Line {line.ShipmentLineId}",
				["Line.Description"] = source?.Description ?? "Returned shipment line",
				["Line.Quantity"] = line.Quantity.ToString("N0", CultureInfo.CurrentCulture)
			});
		});
		return CreateModel(issuer, "Customer Return", customerReturn.ReturnNumber, values, rows);
	}

	private static Dictionary<string, object?> BaseValues(
		DocumentIssuerProfile issuer,
		string title,
		string number,
		DateTime date,
		string customerName,
		string? address)
	{
		var legal = string.Join(" · ", new[] { issuer.RegistrationLine, issuer.TaxLine }.Where(value => !string.IsNullOrWhiteSpace(value)));
		var contact = string.Join(" · ", new[] { issuer.BankLine, string.Join(" · ", new[] { issuer.Email, issuer.Phone, issuer.Website }.Where(value => !string.IsNullOrWhiteSpace(value))) }.Where(value => !string.IsNullOrWhiteSpace(value)));
		return new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Company.Name"] = issuer.DisplayName,
			["Company.Address"] = issuer.PostalAddress,
			["Company.LegalLine"] = legal,
			["Company.ContactLine"] = contact,
			["Document.Title"] = title,
			["Document.Number"] = number,
			["Document.Date"] = date,
			["Customer.Name"] = customerName,
			["Customer.Address"] = address ?? string.Empty,
			["Customer.BillingAddress"] = address ?? string.Empty,
			["Customer.ShippingAddress"] = address ?? string.Empty
		};
	}

	private static DocumentRenderRow FinancialRow(
		string item,
		string description,
		decimal quantity,
		decimal unitPrice,
		decimal taxRate,
		decimal total,
		string currency) =>
		new(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Line.Item"] = item,
			["Line.Description"] = description,
			["Line.Quantity"] = quantity.ToString("N0", CultureInfo.CurrentCulture),
			["Line.UnitPrice"] = Money(unitPrice, currency),
			["Line.TaxRate"] = $"{taxRate:N0}%",
			["Line.Total"] = Money(total, currency)
		});

	private static DocumentRenderRow ShipmentRow(ShipmentLine line) =>
		new(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Line.Item"] = line.PartNumber,
			["Line.Description"] = line.Description,
			["Line.Quantity"] = line.Quantity.ToString("N0", CultureInfo.CurrentCulture)
		});

	private static void AddTotals(IDictionary<string, object?> values, decimal net, decimal tax, decimal total, string currency)
	{
		values["Document.Net"] = Money(net, currency);
		values["Document.Tax"] = Money(tax, currency);
		values["Document.Total"] = Money(total, currency);
	}

	private static DocumentRenderModel CreateModel(
		DocumentIssuerProfile issuer,
		string title,
		string subject,
		IReadOnlyDictionary<string, object?> values,
		IEnumerable<DocumentRenderRow> rows) =>
		new($"{issuer.DisplayName} - {title}", subject, issuer.LegalName, values, rows.ToArray());

	private static string Money(decimal value, string currency) => $"{value:N2} {currency}";
}
