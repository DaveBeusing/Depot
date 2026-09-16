// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;
using Xunit;

namespace Depot.Tests;

public sealed class ElectronicInvoiceTests
{
	private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ElectronicInvoice", "xrechnung-cii-basic.xml");

	[Fact]
	public void Validate_RejectsMissingXRechnungBusinessTerms()
	{
		var result = new ElectronicInvoiceService().Validate(new ElectronicInvoice { BusinessProcessId = string.Empty });
		Assert.False(result.IsValid);
		Assert.Contains(result.Issues, issue => issue.Code == "BT-1");
		Assert.Contains(result.Issues, issue => issue.Code == "BT-10");
		Assert.Contains(result.Issues, issue => issue.Code == "BT-23");
		Assert.Contains(result.Issues, issue => issue.Code == "BG-25");
		Assert.Contains(result.Issues, issue => issue.Code == "BT-34");
		Assert.Contains(result.Issues, issue => issue.Code == "BT-49");
	}

	[Fact]
	public void CreateXRechnung_EmitsCiiProfileAndStableTotals()
	{
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(CreateInvoice());
		Assert.Contains("urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100", xml);
		Assert.Contains("urn:fdc:peppol.eu:2017:poacc:billing:01:1.0", xml);
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

	[Theory]
	[InlineData(ElectronicInvoiceTaxCategories.ZeroRated)]
	[InlineData(ElectronicInvoiceTaxCategories.Exempt)]
	[InlineData(ElectronicInvoiceTaxCategories.ReverseCharge)]
	public void Validate_ZeroVatCategoriesRequireZeroRate(string category)
	{
		var invoice = CreateInvoice();
		invoice = CopyWithLine(invoice, new ElectronicInvoiceLine { Id = "1", Name = "Item A", Quantity = 1m, UnitPrice = 100m, TaxRate = 19m, TaxCategoryCode = category, TaxExemptionReason = category == ElectronicInvoiceTaxCategories.ZeroRated ? null : "Legal exemption" });
		var result = new ElectronicInvoiceService().Validate(invoice);
		Assert.False(result.IsValid);
		Assert.Contains(result.Issues, issue => issue.Code.StartsWith("BT-152", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(ElectronicInvoiceTaxCategories.Exempt)]
	[InlineData(ElectronicInvoiceTaxCategories.ReverseCharge)]
	public void Validate_ExemptTreatmentRequiresReason(string category)
	{
		var invoice = CopyWithLine(CreateInvoice(), new ElectronicInvoiceLine { Id = "1", Name = "Item A", Quantity = 1m, UnitPrice = 100m, TaxRate = 0m, TaxCategoryCode = category });
		var result = new ElectronicInvoiceService().Validate(invoice);
		Assert.False(result.IsValid);
		Assert.Contains(result.Issues, issue => issue.Code.StartsWith("BT-120", StringComparison.Ordinal));
	}

	[Fact]
	public void CreateXRechnung_EmitsReverseChargeEvidence()
	{
		var invoice = ElectronicInvoiceConformanceFixtureTests.CreateInvoice(
			ElectronicInvoiceTypeCode.Invoice,
			ElectronicInvoiceTaxCategories.ReverseCharge,
			0m,
			"VATEX-EU-AE",
			"Reverse charge");
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(invoice);
		Assert.Contains("<ram:CategoryCode>AE</ram:CategoryCode>", xml);
		Assert.Contains("<ram:ExemptionReasonCode>VATEX-EU-AE</ram:ExemptionReasonCode>", xml);
		Assert.Contains("<ram:ExemptionReason>Reverse charge</ram:ExemptionReason>", xml);
		Assert.Contains("<ram:RateApplicablePercent>0</ram:RateApplicablePercent>", xml);
	}

	[Fact]
	public void ConformanceMatrix_IsReleaseSpecific()
	{
		Assert.Equal("3.0", ElectronicInvoiceConformanceMatrix.XRechnungVersion);
		Assert.Contains("xrechnung_3.0", ElectronicInvoiceConformanceMatrix.GuidelineId, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("KoSIT-XRechnung-3.0-CII", ElectronicInvoiceConformanceMatrix.ValidatorProfile);
	}

	private static ElectronicInvoice CopyWithLine(ElectronicInvoice source, ElectronicInvoiceLine line) => new()
	{
		InvoiceNumber = source.InvoiceNumber,
		TypeCode = source.TypeCode,
		IssueDate = source.IssueDate,
		DueDate = source.DueDate,
		ActualDeliveryDate = source.ActualDeliveryDate,
		Currency = source.Currency,
		BuyerReference = source.BuyerReference,
		BusinessProcessId = source.BusinessProcessId,
		PurchaseOrderReference = source.PurchaseOrderReference,
		Seller = source.Seller,
		Buyer = source.Buyer,
		Payment = source.Payment,
		Lines = [line],
		Note = source.Note
	};

	private static string Normalize(string xml) => System.Xml.Linq.XDocument.Parse(xml).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

	private static ElectronicInvoice CreateInvoice(ElectronicInvoiceTypeCode typeCode = ElectronicInvoiceTypeCode.Invoice) => new()
	{
		InvoiceNumber = "INV-2026-0001",
		TypeCode = typeCode,
		IssueDate = new DateOnly(2026, 8, 22),
		DueDate = new DateOnly(2026, 9, 21),
		ActualDeliveryDate = new DateOnly(2026, 8, 22),
		Currency = "EUR",
		BuyerReference = "04011000-12345-03",
		Seller = new ElectronicInvoiceParty { Name = "Depot GmbH", ElectronicAddress = "seller@example.de", ElectronicAddressScheme = "EM", VatIdentifier = "DE123456789", AddressLine1 = "Example 1", City = "Bonn", PostalCode = "53111", CountryCode = "DE", ContactName = "Invoice Desk", ContactPhone = "+49 228 000000", ContactEmail = "billing@example.de" },
		Buyer = new ElectronicInvoiceParty { Name = "Customer GmbH", ElectronicAddress = "buyer@example.de", ElectronicAddressScheme = "EM", AddressLine1 = "Buyer 2", City = "Berlin", PostalCode = "10115", CountryCode = "DE" },
		Payment = new ElectronicInvoicePayment { MeansCode = "58", AccountIdentifier = "DE02120300000000202051", FinancialInstitutionIdentifier = "BYLADEM1001", Terms = "Payable within 30 days." },
		Lines = [new ElectronicInvoiceLine { Id = "1", Name = "Item A", Quantity = 2m, UnitPrice = 100m, TaxRate = 19m }]
	};
}
