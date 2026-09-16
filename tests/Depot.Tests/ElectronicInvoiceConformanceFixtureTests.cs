// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;
using Xunit;

namespace Depot.Tests;

public sealed class ElectronicInvoiceConformanceFixtureTests
{
	private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ElectronicInvoice");

	public static TheoryData<string, ElectronicInvoiceTypeCode, string, decimal, string?, string?> ConformanceCases => new()
	{
		{ "xrechnung-cii-basic.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.StandardRated, 19m, null, null },
		{ "xrechnung-cii-zero-rated.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.ZeroRated, 0m, null, null },
		{ "xrechnung-cii-exempt.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.Exempt, 0m, null, "VAT exempt" },
		{ "xrechnung-cii-reverse-charge.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.ReverseCharge, 0m, "VATEX-EU-AE", "Reverse charge" },
		{ "xrechnung-cii-credit-note.xml", ElectronicInvoiceTypeCode.CreditNote, ElectronicInvoiceTaxCategories.StandardRated, 19m, null, null }
	};

	[Theory]
	[MemberData(nameof(ConformanceCases))]
	public void GeneratorOutput_MatchesKoSITValidatedFixture(
		string fixtureName,
		ElectronicInvoiceTypeCode typeCode,
		string taxCategory,
		decimal taxRate,
		string? exemptionReasonCode,
		string? exemptionReason)
	{
		var invoice = CreateInvoice(typeCode, taxCategory, taxRate, exemptionReasonCode, exemptionReason);
		var generated = Normalize(new ElectronicInvoiceService().CreateXRechnungXml(invoice));
		var fixture = Normalize(File.ReadAllText(Path.Combine(FixtureDirectory, fixtureName)));

		Assert.Equal(fixture, generated);
	}

	private static string Normalize(string xml) =>
		System.Xml.Linq.XDocument.Parse(xml).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

	internal static ElectronicInvoice CreateInvoice(
		ElectronicInvoiceTypeCode typeCode,
		string taxCategory,
		decimal taxRate,
		string? exemptionReasonCode,
		string? exemptionReason) => new()
	{
		InvoiceNumber = "INV-2026-0001",
		TypeCode = typeCode,
		IssueDate = new DateOnly(2026, 8, 22),
		DueDate = new DateOnly(2026, 9, 21),
		ActualDeliveryDate = new DateOnly(2026, 8, 22),
		Currency = "EUR",
		BuyerReference = "04011000-12345-03",
		Seller = new ElectronicInvoiceParty
		{
			Name = "Depot GmbH",
			ElectronicAddress = "seller@example.de",
			ElectronicAddressScheme = "EM",
			VatIdentifier = "DE123456789",
			AddressLine1 = "Example 1",
			City = "Bonn",
			PostalCode = "53111",
			CountryCode = "DE",
			ContactName = "Invoice Desk",
			ContactPhone = "+49 228 000000",
			ContactEmail = "billing@example.de"
		},
		Buyer = new ElectronicInvoiceParty
		{
			Name = "Customer GmbH",
			ElectronicAddress = "buyer@example.de",
			ElectronicAddressScheme = "EM",
			AddressLine1 = "Buyer 2",
			City = "Berlin",
			PostalCode = "10115",
			CountryCode = "DE"
		},
		Payment = new ElectronicInvoicePayment
		{
			MeansCode = "58",
			AccountIdentifier = "DE02120300000000202051",
			FinancialInstitutionIdentifier = "BYLADEM1001",
			Terms = "Payable within 30 days."
		},
		Lines =
		[
			new ElectronicInvoiceLine
			{
				Id = "1",
				Name = "Item A",
				Quantity = 2m,
				UnitPrice = 100m,
				TaxRate = taxRate,
				TaxCategoryCode = taxCategory,
				TaxExemptionReasonCode = exemptionReasonCode,
				TaxExemptionReason = exemptionReason
			}
		]
	};
}
