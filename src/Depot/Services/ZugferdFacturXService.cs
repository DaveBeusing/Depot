// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.DocumentRendering;
using Depot.Models;

using PdfSharp.Pdf;

namespace Depot.Services;

public sealed class ZugferdFacturXService
{
	public const string InvoiceDocumentType = "Invoice";
	public const string CreditNoteDocumentType = "CreditNote";

	private readonly DatabaseAccess _dataAccess;

	public ZugferdFacturXService(DatabaseAccess dataAccess)
	{
		_dataAccess = dataAccess;
	}

	public HybridElectronicInvoiceArtifact? TryLoad(string documentType, long documentId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
		var rows = _dataAccess.Query(
			"SELECT StandardVersion,Profile,XmlFileName,PdfAConformance,XmlSha256,PdfSha256,PdfBytes,CreatedAtUtc FROM SalesHybridElectronicInvoiceArtifacts WHERE DocumentType=$Type AND DocumentId=$Id;",
			reader => new HybridElectronicInvoiceArtifact(
				documentType,
				documentId,
				reader.GetString(0),
				reader.GetString(1),
				reader.GetString(2),
				reader.GetString(3),
				reader.GetString(4),
				reader.GetString(5),
				reader.GetFieldValue<byte[]>(6),
				DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
			new DatabaseParameter("$Type", documentType),
			new DatabaseParameter("$Id", documentId));
		if (rows.Count == 0) return null;
		VerifyArtifact(rows[0]);
		return rows[0];
	}

	public HybridElectronicInvoiceArtifact LoadRequired(string documentType, long documentId) =>
		TryLoad(documentType, documentId) ?? throw new InvalidOperationException($"{documentType} {documentId} has no finalized ZUGFeRD/Factur-X artifact. Legacy records are not reconstructed from mutable master data.");

	public void ExportInvoice(long salesInvoiceId, string path)
	{
		var finalization = new SalesInvoiceFinalizationService(_dataAccess).LoadRequired(salesInvoiceId);
		Export(InvoiceDocumentType, salesInvoiceId, finalization.XRechnungSha256, path);
	}

	public void ExportCreditNote(long salesCreditNoteId, string path)
	{
		var finalization = new SalesCreditNoteFinalizationService(_dataAccess).LoadRequired(salesCreditNoteId);
		Export(CreditNoteDocumentType, salesCreditNoteId, finalization.XRechnungSha256, path);
	}

	public static HybridElectronicInvoiceArtifact CreateArtifact(
		string documentType,
		long documentId,
		ElectronicInvoice invoice,
		string xRechnungXml,
		string xRechnungSha256,
		DateTime createdAtUtc)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
		ArgumentNullException.ThrowIfNull(invoice);
		ArgumentException.ThrowIfNullOrWhiteSpace(xRechnungXml);
		ArgumentException.ThrowIfNullOrWhiteSpace(xRechnungSha256);
		if (createdAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Hybrid invoice artifact timestamp must be UTC.", nameof(createdAtUtc));
		if (!string.Equals(ComputeHash(Encoding.UTF8.GetBytes(xRechnungXml)), xRechnungSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The XRechnung payload does not match its finalized SHA-256 evidence.");

		using var document = new PdfDocument();
		document.Info.Title = $"{invoice.Seller.Name} - {(invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote ? "Credit Note" : "Invoice")} {invoice.InvoiceNumber}";
		document.Info.Subject = invoice.InvoiceNumber;
		document.Info.Author = invoice.Seller.Name;
		document.Info.Creator = "Depot";
		document.Info.CreationDate = createdAtUtc;

		var xmlBytes = new UTF8Encoding(false).GetBytes(xRechnungXml);
		PdfSharpFacturXCompatibility.Configure(document, xmlBytes, createdAtUtc);
		var templateType = invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote ? DocumentTemplateType.CreditNote : DocumentTemplateType.SalesInvoice;
		var renderModel = ElectronicInvoiceRenderModelFactory.Create(invoice);
		new DocumentLayoutRenderer().RenderInto(document, DefaultDocumentTemplates.Catalog.GetActive(templateType), renderModel);
		var pdfBytes = PdfSharpFacturXCompatibility.SaveWithXmp(document, BuildXmp(invoice, createdAtUtc));
		return new HybridElectronicInvoiceArtifact(
			documentType,
			documentId,
			ZugferdFacturXConformance.StandardVersion,
			ZugferdFacturXConformance.Profile,
			ZugferdFacturXConformance.XmlFileName,
			ZugferdFacturXConformance.PdfAConformance,
			xRechnungSha256.ToLowerInvariant(),
			ComputeHash(pdfBytes),
			pdfBytes,
			createdAtUtc);
	}

	internal static Task<int> InsertAsync(DatabaseTransactionContext transaction, HybridElectronicInvoiceArtifact artifact, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		ArgumentNullException.ThrowIfNull(artifact);
		VerifyArtifact(artifact);
		return transaction.Session.ExecuteAsync(
			"INSERT INTO SalesHybridElectronicInvoiceArtifacts (DocumentType,DocumentId,StandardVersion,Profile,XmlFileName,PdfAConformance,XmlSha256,PdfSha256,PdfBytes,CreatedAtUtc) VALUES ($Type,$Id,$Standard,$Profile,$XmlFile,$PdfA,$XmlHash,$PdfHash,$PdfBytes,$At);",
			cancellationToken,
			new DatabaseParameter("$Type", artifact.DocumentType),
			new DatabaseParameter("$Id", artifact.DocumentId),
			new DatabaseParameter("$Standard", artifact.StandardVersion),
			new DatabaseParameter("$Profile", artifact.Profile),
			new DatabaseParameter("$XmlFile", artifact.XmlFileName),
			new DatabaseParameter("$PdfA", artifact.PdfAConformance),
			new DatabaseParameter("$XmlHash", artifact.XmlSha256),
			new DatabaseParameter("$PdfHash", artifact.PdfSha256),
			new DatabaseParameter("$PdfBytes", artifact.PdfBytes),
			new DatabaseParameter("$At", artifact.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
	}

	private void Export(string documentType, long documentId, string expectedXmlSha256, string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var artifact = LoadRequired(documentType, documentId);
		if (!string.Equals(artifact.XmlSha256, expectedXmlSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The stored ZUGFeRD/Factur-X artifact does not reference the finalized XRechnung payload.");
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllBytes(path, artifact.PdfBytes);
	}

	private static void VerifyArtifact(HybridElectronicInvoiceArtifact artifact)
	{
		if (!string.Equals(artifact.StandardVersion, ZugferdFacturXConformance.StandardVersion, StringComparison.Ordinal) ||
			!string.Equals(artifact.Profile, ZugferdFacturXConformance.Profile, StringComparison.Ordinal) ||
			!string.Equals(artifact.XmlFileName, ZugferdFacturXConformance.XmlFileName, StringComparison.Ordinal) ||
			!string.Equals(artifact.PdfAConformance, ZugferdFacturXConformance.PdfAConformance, StringComparison.Ordinal))
			throw new InvalidOperationException("Stored ZUGFeRD/Factur-X conformance evidence is not supported by this Depot version.");
		if (!string.Equals(ComputeHash(artifact.PdfBytes), artifact.PdfSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Stored ZUGFeRD/Factur-X PDF failed its SHA-256 integrity check.");
	}

	private static string BuildXmp(ElectronicInvoice invoice, DateTime createdAtUtc)
	{
		var title = Escape($"{(invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote ? "Credit Note" : "Invoice")} {invoice.InvoiceNumber}");
		var creator = Escape(invoice.Seller.Name);
		return $"""
<?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
  <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
    <rdf:Description rdf:about=""
      xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/"
      xmlns:dc="http://purl.org/dc/elements/1.1/"
      xmlns:xmp="http://ns.adobe.com/xap/1.0/"
      xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/"
      xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#"
      xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#"
      xmlns:fx="{ZugferdFacturXConformance.XmpNamespace}">
      <pdfaid:part>3</pdfaid:part>
      <pdfaid:conformance>B</pdfaid:conformance>
      <dc:title><rdf:Alt><rdf:li xml:lang="x-default">{title}</rdf:li></rdf:Alt></dc:title>
      <dc:creator><rdf:Seq><rdf:li>{creator}</rdf:li></rdf:Seq></dc:creator>
      <xmp:CreatorTool>Depot</xmp:CreatorTool>
      <xmp:CreateDate>{createdAtUtc:O}</xmp:CreateDate>
      <fx:DocumentType>INVOICE</fx:DocumentType>
      <fx:DocumentFileName>{ZugferdFacturXConformance.XmlFileName}</fx:DocumentFileName>
      <fx:Version>{ZugferdFacturXConformance.XmpVersion}</fx:Version>
      <fx:ConformanceLevel>{ZugferdFacturXConformance.Profile}</fx:ConformanceLevel>
      <pdfaExtension:schemas>
        <rdf:Bag>
          <rdf:li rdf:parseType="Resource">
            <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>
            <pdfaSchema:namespaceURI>{ZugferdFacturXConformance.XmpNamespace}</pdfaSchema:namespaceURI>
            <pdfaSchema:prefix>fx</pdfaSchema:prefix>
            <pdfaSchema:property>
              <rdf:Seq>
                <rdf:li rdf:parseType="Resource"><pdfaProperty:name>DocumentFileName</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>Name of the embedded XML invoice file</pdfaProperty:description></rdf:li>
                <rdf:li rdf:parseType="Resource"><pdfaProperty:name>DocumentType</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>Document type</pdfaProperty:description></rdf:li>
                <rdf:li rdf:parseType="Resource"><pdfaProperty:name>Version</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>Factur-X metadata schema version</pdfaProperty:description></rdf:li>
                <rdf:li rdf:parseType="Resource"><pdfaProperty:name>ConformanceLevel</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>Factur-X conformance profile</pdfaProperty:description></rdf:li>
              </rdf:Seq>
            </pdfaSchema:property>
          </rdf:li>
        </rdf:Bag>
      </pdfaExtension:schemas>
    </rdf:Description>
  </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
	}

	private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
	private static string ComputeHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
