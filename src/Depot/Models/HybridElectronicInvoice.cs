// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public static class ZugferdFacturXConformance
{
	public const string ZugferdVersion = "2.5.2";
	public const string FacturXVersion = "1.09.2";
	public const string StandardVersion = "ZUGFeRD-2.5.2/Factur-X-1.09.2";
	public const string Profile = "XRECHNUNG";
	public const string XmlFileName = "xrechnung.xml";
	public const string PdfAConformance = "PDF/A-3B";
	public const string XmpNamespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";
	public const string XmpVersion = "1.0";
	public const string PdfAValidator = "veraPDF-1.30.2";
}

public sealed record HybridElectronicInvoiceArtifact(
	string DocumentType,
	long DocumentId,
	string StandardVersion,
	string Profile,
	string XmlFileName,
	string PdfAConformance,
	string XmlSha256,
	string PdfSha256,
	byte[] PdfBytes,
	DateTime CreatedAtUtc);
