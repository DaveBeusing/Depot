// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ZugferdFacturXConformanceFixtureTests
{
	private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ElectronicInvoice");
	private static readonly ConformanceCase[] Cases =
	[
		new("zugferd-facturx-basic.pdf", "xrechnung-cii-basic.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.StandardRated, 19m, null, null),
		new("zugferd-facturx-zero-rated.pdf", "xrechnung-cii-zero-rated.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.ZeroRated, 0m, null, null),
		new("zugferd-facturx-exempt.pdf", "xrechnung-cii-exempt.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.Exempt, 0m, null, "VAT exempt"),
		new("zugferd-facturx-reverse-charge.pdf", "xrechnung-cii-reverse-charge.xml", ElectronicInvoiceTypeCode.Invoice, ElectronicInvoiceTaxCategories.ReverseCharge, 0m, "VATEX-EU-AE", "Reverse charge"),
		new("zugferd-facturx-credit-note.pdf", "xrechnung-cii-credit-note.xml", ElectronicInvoiceTypeCode.CreditNote, ElectronicInvoiceTaxCategories.StandardRated, 19m, null, null)
	];

	[Fact]
	public void ProductionHybridArtifacts_MatchKoSITBoundMatrixAndAreWrittenForExternalValidation()
	{
		var configuredOutput = Environment.GetEnvironmentVariable("DEPOT_ZUGFERD_CONFORMANCE_DIR");
		var temporaryOutput = string.IsNullOrWhiteSpace(configuredOutput);
		var outputDirectory = temporaryOutput
			? Path.Combine(Path.GetTempPath(), $"depot-zugferd-conformance-{Guid.NewGuid():N}")
			: Path.GetFullPath(configuredOutput!);
		Directory.CreateDirectory(outputDirectory);

		try
		{
			var manifest = new List<ManifestEntry>();
			for (var index = 0; index < Cases.Length; index++)
			{
				var conformanceCase = Cases[index];
				var invoice = ElectronicInvoiceConformanceFixtureTests.CreateInvoice(
					conformanceCase.TypeCode,
					conformanceCase.TaxCategory,
					conformanceCase.TaxRate,
					conformanceCase.ExemptionReasonCode,
					conformanceCase.ExemptionReason);
				var xml = new ElectronicInvoiceService().CreateXRechnungXml(invoice);
				var retainedFixture = File.ReadAllText(Path.Combine(FixtureDirectory, conformanceCase.XmlFixtureName));
				Assert.Equal(Normalize(retainedFixture), Normalize(xml));

				var xmlBytes = new UTF8Encoding(false).GetBytes(xml);
				var documentType = conformanceCase.TypeCode == ElectronicInvoiceTypeCode.CreditNote
					? ZugferdFacturXService.CreditNoteDocumentType
					: ZugferdFacturXService.InvoiceDocumentType;
				var artifact = ZugferdFacturXService.CreateArtifact(
					documentType,
					1000 + index,
					invoice,
					xml,
					Hash(xmlBytes),
					new DateTime(2026, 9, 16, 12, 0, index, DateTimeKind.Utc));

				var pdfPath = Path.Combine(outputDirectory, conformanceCase.PdfFileName);
				File.WriteAllBytes(pdfPath, artifact.PdfBytes);

				var inspection = PdfSharpFacturXCompatibility.Inspect(artifact.PdfBytes);
				Assert.Equal(ZugferdFacturXConformance.XmlFileName, inspection.FileName);
				Assert.Equal("/Alternative", inspection.AfRelationship);
				Assert.Equal("/text/xml", inspection.MimeSubtype);
				Assert.True(FinalTrailerContainsDocumentId(artifact.PdfBytes));
				Assert.Equal(xmlBytes, inspection.EmbeddedXml);
				Assert.Contains("<pdfaid:part>3</pdfaid:part>", inspection.Xmp, StringComparison.Ordinal);
				Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", inspection.Xmp, StringComparison.Ordinal);

				manifest.Add(new ManifestEntry(
					conformanceCase.PdfFileName,
					conformanceCase.XmlFixtureName,
					documentType,
					conformanceCase.TaxCategory,
					artifact.StandardVersion,
					artifact.Profile,
					artifact.PdfAConformance,
					artifact.XmlSha256,
					artifact.PdfSha256));
			}

			File.WriteAllText(
				Path.Combine(outputDirectory, "manifest.json"),
				JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
				new UTF8Encoding(false));
		}
		finally
		{
			if (temporaryOutput && Directory.Exists(outputDirectory))
				Directory.Delete(outputDirectory, recursive: true);
		}
	}

	private static string Normalize(string xml) => XDocument.Parse(xml).ToString(SaveOptions.DisableFormatting);
	private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private static bool FinalTrailerContainsDocumentId(byte[] pdfBytes)
	{
		var text = Encoding.ASCII.GetString(pdfBytes);
		var trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);
		return trailer >= 0 && text.IndexOf("/ID", trailer, StringComparison.Ordinal) >= 0;
	}

	private sealed record ConformanceCase(
		string PdfFileName,
		string XmlFixtureName,
		ElectronicInvoiceTypeCode TypeCode,
		string TaxCategory,
		decimal TaxRate,
		string? ExemptionReasonCode,
		string? ExemptionReason);

	private sealed record ManifestEntry(
		string PdfFileName,
		string XmlFixtureName,
		string DocumentType,
		string TaxCategory,
		string StandardVersion,
		string Profile,
		string PdfAConformance,
		string XmlSha256,
		string PdfSha256);
}
