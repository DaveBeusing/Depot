// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Xml.Linq;
using Depot.Models;

namespace Depot.Services;

public sealed record ElectronicInvoiceValidationIssue(string Code, string Message, bool IsError = true);

public sealed record ElectronicInvoiceValidationResult(IReadOnlyList<ElectronicInvoiceValidationIssue> Issues)
{
	public bool IsValid => Issues.All(issue => !issue.IsError);
}

public sealed class ElectronicInvoiceService
{
	private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
	private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
	private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";

	public ElectronicInvoiceValidationResult Validate(ElectronicInvoice invoice)
	{
		ArgumentNullException.ThrowIfNull(invoice);
		var issues = new List<ElectronicInvoiceValidationIssue>();

		Require(invoice.InvoiceNumber, "BT-1", "Invoice number is required.", issues);
		Require(invoice.Currency, "BT-5", "Invoice currency is required.", issues);
		Require(invoice.BuyerReference, "BT-10", "Buyer reference is required for the XRechnung profile.", issues);
		ValidateParty(invoice.Seller, "seller", issues);
		ValidateParty(invoice.Buyer, "buyer", issues);

		if (invoice.Lines.Count == 0)
			issues.Add(new("BG-25", "At least one invoice line is required."));

		if (invoice.DueDate is not null && invoice.DueDate < invoice.IssueDate)
			issues.Add(new("BT-9", "Due date cannot be before the invoice issue date."));

		for (var index = 0; index < invoice.Lines.Count; index++)
		{
			var line = invoice.Lines[index];
			Require(line.Id, $"BT-126[{index}]", "Invoice line identifier is required.", issues);
			Require(line.Name, $"BT-153[{index}]", "Item name is required.", issues);
			Require(line.UnitCode, $"BT-130[{index}]", "Unit code is required.", issues);
			if (line.Quantity <= 0m)
				issues.Add(new($"BT-129[{index}]", "Invoice quantity must be greater than zero."));
			if (line.UnitPrice < 0m)
				issues.Add(new($"BT-146[{index}]", "Item net price cannot be negative."));
			if (line.DiscountPercent is < 0m or > 100m)
				issues.Add(new($"BT-147[{index}]", "Discount percent must be between 0 and 100."));
			if (line.TaxRate < 0m)
				issues.Add(new($"BT-152[{index}]", "VAT rate cannot be negative."));
		}

		return new(issues);
	}

	public XDocument CreateXRechnung(ElectronicInvoice invoice)
	{
		var validation = Validate(invoice);
		if (!validation.IsValid)
			throw new InvalidOperationException("Electronic invoice is not valid: " + string.Join("; ", validation.Issues.Where(x => x.IsError).Select(x => $"{x.Code} {x.Message}")));

		var totals = CalculateTotals(invoice);
		var root = new XElement(Rsm + "CrossIndustryInvoice",
			new XAttribute(XNamespace.Xmlns + "rsm", Rsm),
			new XAttribute(XNamespace.Xmlns + "ram", Ram),
			new XAttribute(XNamespace.Xmlns + "udt", Udt),
			CreateContext("urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0"),
			new XElement(Rsm + "ExchangedDocument",
				new XElement(Ram + "ID", invoice.InvoiceNumber),
				new XElement(Ram + "TypeCode", ((int)invoice.TypeCode).ToString(CultureInfo.InvariantCulture)),
				new XElement(Ram + "IssueDateTime", new XElement(Udt + "DateTimeString", new XAttribute("format", "102"), invoice.IssueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))),
				string.IsNullOrWhiteSpace(invoice.Note) ? null : new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", invoice.Note))),
			new XElement(Rsm + "SupplyChainTradeTransaction",
				invoice.Lines.Select(CreateLine),
				CreateHeaderAgreement(invoice),
				new XElement(Ram + "ApplicableHeaderTradeDelivery"),
				CreateSettlement(invoice, totals)));

		return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
	}

	public string CreateXRechnungXml(ElectronicInvoice invoice) => CreateXRechnung(invoice).ToString(SaveOptions.DisableFormatting);

	private static XElement CreateContext(string guideline) =>
		new(Rsm + "ExchangedDocumentContext", new XElement(Ram + "GuidelineSpecifiedDocumentContextParameter", new XElement(Ram + "ID", guideline)));

	private static XElement CreateHeaderAgreement(ElectronicInvoice invoice) =>
		new(Ram + "ApplicableHeaderTradeAgreement",
			new XElement(Ram + "BuyerReference", invoice.BuyerReference),
			CreateParty("SellerTradeParty", invoice.Seller),
			CreateParty("BuyerTradeParty", invoice.Buyer),
			string.IsNullOrWhiteSpace(invoice.PurchaseOrderReference) ? null : new XElement(Ram + "BuyerOrderReferencedDocument", new XElement(Ram + "IssuerAssignedID", invoice.PurchaseOrderReference)));

	private static XElement CreateParty(string name, ElectronicInvoiceParty party) =>
		new(Ram + name,
			string.IsNullOrWhiteSpace(party.ElectronicAddress) ? null : new XElement(Ram + "URIUniversalCommunication", new XElement(Ram + "URIID", new XAttribute("schemeID", party.ElectronicAddressScheme ?? "EM"), party.ElectronicAddress)),
			new XElement(Ram + "Name", party.Name),
			string.IsNullOrWhiteSpace(party.RegistrationIdentifier) ? null : new XElement(Ram + "SpecifiedLegalOrganization", new XElement(Ram + "ID", party.RegistrationIdentifier)),
			new XElement(Ram + "PostalTradeAddress",
				new XElement(Ram + "PostcodeCode", party.PostalCode),
				new XElement(Ram + "LineOne", party.AddressLine1),
				string.IsNullOrWhiteSpace(party.AddressLine2) ? null : new XElement(Ram + "LineTwo", party.AddressLine2),
				new XElement(Ram + "CityName", party.City),
				new XElement(Ram + "CountryID", party.CountryCode)),
			string.IsNullOrWhiteSpace(party.VatIdentifier) ? null : new XElement(Ram + "SpecifiedTaxRegistration", new XElement(Ram + "ID", new XAttribute("schemeID", "VA"), party.VatIdentifier)),
			string.IsNullOrWhiteSpace(party.TaxIdentifier) ? null : new XElement(Ram + "SpecifiedTaxRegistration", new XElement(Ram + "ID", new XAttribute("schemeID", "FC"), party.TaxIdentifier)));

	private static XElement CreateLine(ElectronicInvoiceLine line)
	{
		var net = Round(line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m));
		return new XElement(Ram + "IncludedSupplyChainTradeLineItem",
			new XElement(Ram + "AssociatedDocumentLineDocument", new XElement(Ram + "LineID", line.Id)),
			new XElement(Ram + "SpecifiedTradeProduct",
				string.IsNullOrWhiteSpace(line.SellerItemIdentifier) ? null : new XElement(Ram + "SellerAssignedID", line.SellerItemIdentifier),
				new XElement(Ram + "Name", line.Name),
				string.IsNullOrWhiteSpace(line.Description) ? null : new XElement(Ram + "Description", line.Description)),
			new XElement(Ram + "SpecifiedLineTradeAgreement", new XElement(Ram + "NetPriceProductTradePrice", new XElement(Ram + "ChargeAmount", Money(line.UnitPrice)))),
			new XElement(Ram + "SpecifiedLineTradeDelivery", new XElement(Ram + "BilledQuantity", new XAttribute("unitCode", line.UnitCode), Number(line.Quantity))),
			new XElement(Ram + "SpecifiedLineTradeSettlement",
				new XElement(Ram + "ApplicableTradeTax", new XElement(Ram + "TypeCode", "VAT"), new XElement(Ram + "CategoryCode", line.TaxCategoryCode), new XElement(Ram + "RateApplicablePercent", Number(line.TaxRate))),
				new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation", new XElement(Ram + "LineTotalAmount", Money(net)))));
	}

	private static XElement CreateSettlement(ElectronicInvoice invoice, InvoiceTotals totals)
	{
		var taxGroups = invoice.Lines.GroupBy(line => new { line.TaxCategoryCode, line.TaxRate });
		return new XElement(Ram + "ApplicableHeaderTradeSettlement",
			new XElement(Ram + "InvoiceCurrencyCode", invoice.Currency),
			taxGroups.Select(group =>
			{
				var basis = Round(group.Sum(line => line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m)));
				var tax = Round(basis * group.Key.TaxRate / 100m);
				return new XElement(Ram + "ApplicableTradeTax", new XElement(Ram + "CalculatedAmount", Money(tax)), new XElement(Ram + "TypeCode", "VAT"), new XElement(Ram + "BasisAmount", Money(basis)), new XElement(Ram + "CategoryCode", group.Key.TaxCategoryCode), new XElement(Ram + "RateApplicablePercent", Number(group.Key.TaxRate)));
			}),
			CreatePayment(invoice),
			string.IsNullOrWhiteSpace(invoice.Payment.Terms) && invoice.DueDate is null ? null : new XElement(Ram + "SpecifiedTradePaymentTerms",
				string.IsNullOrWhiteSpace(invoice.Payment.Terms) ? null : new XElement(Ram + "Description", invoice.Payment.Terms),
				invoice.DueDate is null ? null : new XElement(Ram + "DueDateDateTime", new XElement(Udt + "DateTimeString", new XAttribute("format", "102"), invoice.DueDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)))),
			new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
				new XElement(Ram + "LineTotalAmount", Money(totals.Net)),
				new XElement(Ram + "ChargeTotalAmount", "0.00"),
				new XElement(Ram + "AllowanceTotalAmount", "0.00"),
				new XElement(Ram + "TaxBasisTotalAmount", Money(totals.Net)),
				new XElement(Ram + "TaxTotalAmount", new XAttribute("currencyID", invoice.Currency), Money(totals.Tax)),
				new XElement(Ram + "GrandTotalAmount", Money(totals.Gross)),
				new XElement(Ram + "DuePayableAmount", Money(totals.Gross))));
	}

	private static XElement? CreatePayment(ElectronicInvoice invoice)
	{
		if (string.IsNullOrWhiteSpace(invoice.Payment.MeansCode))
			return null;
		return new XElement(Ram + "SpecifiedTradeSettlementPaymentMeans",
			new XElement(Ram + "TypeCode", invoice.Payment.MeansCode),
			string.IsNullOrWhiteSpace(invoice.Payment.AccountIdentifier) ? null : new XElement(Ram + "PayeePartyCreditorFinancialAccount", new XElement(Ram + "IBANID", invoice.Payment.AccountIdentifier)),
			string.IsNullOrWhiteSpace(invoice.Payment.FinancialInstitutionIdentifier) ? null : new XElement(Ram + "PayeeSpecifiedCreditorFinancialInstitution", new XElement(Ram + "BICID", invoice.Payment.FinancialInstitutionIdentifier)));
	}

	private static InvoiceTotals CalculateTotals(ElectronicInvoice invoice)
	{
		var net = Round(invoice.Lines.Sum(line => line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m)));
		var tax = Round(invoice.Lines.Sum(line => Round(line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m)) * line.TaxRate / 100m));
		return new(net, tax, net + tax);
	}

	private static void ValidateParty(ElectronicInvoiceParty party, string role, ICollection<ElectronicInvoiceValidationIssue> issues)
	{
		Require(party.Name, role == "seller" ? "BT-27" : "BT-44", $"{role} name is required.", issues);
		Require(party.AddressLine1, role == "seller" ? "BT-35" : "BT-50", $"{role} address is required.", issues);
		Require(party.City, role == "seller" ? "BT-37" : "BT-52", $"{role} city is required.", issues);
		Require(party.PostalCode, role == "seller" ? "BT-38" : "BT-53", $"{role} postal code is required.", issues);
		Require(party.CountryCode, role == "seller" ? "BT-40" : "BT-55", $"{role} country code is required.", issues);
		if (role == "seller" && string.IsNullOrWhiteSpace(party.VatIdentifier) && string.IsNullOrWhiteSpace(party.TaxIdentifier))
			issues.Add(new("BR-CO-09", "Seller VAT identifier or tax registration identifier is required."));
	}

	private static void Require(string? value, string code, string message, ICollection<ElectronicInvoiceValidationIssue> issues)
	{
		if (string.IsNullOrWhiteSpace(value)) issues.Add(new(code, message));
	}

	private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
	private static string Money(decimal value) => Round(value).ToString("0.00", CultureInfo.InvariantCulture);
	private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
	private sealed record InvoiceTotals(decimal Net, decimal Tax, decimal Gross);
}
