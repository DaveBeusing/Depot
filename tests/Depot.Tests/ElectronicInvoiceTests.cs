// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

namespace Depot.Tests;

public sealed class ElectronicInvoiceTests
{
	private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ElectronicInvoice", "xrechnung-cii-basic.xml");

	[Fact]
	public void Validate_RejectsMissingXRechnungBusinessTerms()
	{
		var service = new ElectronicInvoiceService();
		var result = service.Validate(new ElectronicInvoice());
		Assert.False(result.IsValid);
		Assert.Contains(result.Issues, issue => issue.Code == "BT-1");
		Assert.Contains(result.Issues, issue => issue.Code == "BT-10");
		Assert.Contains(result.Issues, issue => issue.Code == "BG-25");
	}

	[Fact]
	public void CreateXRechnung_EmitsCiiProfileAndStableTotals()
	{
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(CreateInvoice());
		Assert.Contains("urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100", xml);
		Assert.Contains("urn:xeinkauf.de:kosit:xrechnung_3.0", xml);
		Assert.Contains("INV-2026-0001", xml);
		Assert.Contains("238.00", xml);
		Assert.Contains("200.00", xml);
		Assert.Contains("38.00", xml);
	}

	[Fact]
	public void CreateXRechnung_MatchesKoSITValidatedFixture()
	{
		var generated = Normalize(new ElectronicInvoiceService().CreateXRechnungXml(CreateInvoice()));
		var fixture = Normalize(File.ReadAllText(FixturePath));
		Assert.Equal(fixture, generated);
	}

	[Fact]
	public void CreateXRechnung_CreditNoteUsesCreditNoteTypeCode()
	{
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(CreateInvoice(ElectronicInvoiceTypeCode.CreditNote));
		Assert.Contains("<ram:TypeCode>381</ram:TypeCode>", xml);
	}

	private static string Normalize(string xml) => System.Xml.Linq.XDocument.Parse(xml).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

	private static ElectronicInvoice CreateInvoice(ElectronicInvoiceTypeCode typeCode = ElectronicInvoiceTypeCode.Invoice) => new()
	{
		InvoiceNumber = "INV-2026-0001",
		TypeCode = typeCode,
		IssueDate = new DateOnly(2026, 8, 22),
		DueDate = new DateOnly(2026, 9, 21),
		Currency = "EUR",
		BuyerReference = "04011000-12345-03",
		Seller = new ElectronicInvoiceParty { Name = "Depot GmbH", VatIdentifier = "DE123456789", AddressLine1 = "Example 1", City = "Bonn", PostalCode = "53111", CountryCode = "DE" },
		Buyer = new ElectronicInvoiceParty { Name = "Customer GmbH", AddressLine1 = "Buyer 2", City = "Berlin", PostalCode = "10115", CountryCode = "DE" },
		Payment = new ElectronicInvoicePayment { MeansCode = "58", AccountIdentifier = "DE02120300000000202051", FinancialInstitutionIdentifier = "BYLADEM1001", Terms = "Payable within 30 days." },
		Lines = [new ElectronicInvoiceLine { Id = "1", Name = "Item A", Quantity = 2m, UnitPrice = 100m, TaxRate = 19m }]
	};
}
