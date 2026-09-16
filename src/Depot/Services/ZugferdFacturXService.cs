// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;

using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Attachments;
using PdfSharp.Pdf.Metadata;
using PdfSharp.Pdf.PdfA;

namespace Depot.Services;

public sealed class ZugferdFacturXService
{
	public const string InvoiceDocumentType = "Invoice";
	public const string CreditNoteDocumentType = "CreditNote";

	private static readonly XFont TitleFont = new("Segoe UI", 18, XFontStyleEx.Bold);
	private static readonly XFont HeadingFont = new("Segoe UI", 10, XFontStyleEx.Bold);
	private static readonly XFont BodyFont = new("Segoe UI", 9, XFontStyleEx.Regular);
	private static readonly XFont SmallFont = new("Segoe UI", 8, XFontStyleEx.Regular);
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
		document.SetPdfA(PdfAFormats.PdfA_3b);
		document.Info.Title = $"{invoice.Seller.Name} - {(invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote ? "Credit Note" : "Invoice")} {invoice.InvoiceNumber}";
		document.Info.Subject = invoice.InvoiceNumber;
		document.Info.Author = invoice.Seller.Name;
		document.Info.Creator = "Depot";

		var metadataManager = MetadataManager.ForDocument(document);
		metadataManager.Strategy = DocumentMetadataStrategy.UserGenerated;
		document.Events.CreateDocumentMetadata += (_, args) => args.Metadata.SetMetadata(BuildXmp(invoice, createdAtUtc));

		var xmlBytes = new UTF8Encoding(false).GetBytes(xRechnungXml);
		EmbeddedFilesManager.ForDocument(document).AddFile(new EmbeddedFileInfo
		{
			NamesKey = ZugferdFacturXConformance.XmlFileName,
			FileName = ZugferdFacturXConformance.XmlFileName,
			FileType = "text/xml",
			Description = "XRechnung invoice data",
			CreationTime = new DateTimeOffset(createdAtUtc),
			ModificationTime = new DateTimeOffset(createdAtUtc),
			Data = xmlBytes,
			AFRelationship = PdfAFRelationship.Alternative
		});

		RenderInvoice(document, invoice);
		using var stream = new MemoryStream();
		document.Save(stream, false);
		var pdfBytes = stream.ToArray();
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

	private static void RenderInvoice(PdfDocument document, ElectronicInvoice invoice)
	{
		var page = document.AddPage();
		var graphics = XGraphics.FromPdfPage(page);
		var y = 42d;
		graphics.DrawString(invoice.Seller.Name, HeadingFont, XBrushes.Black, new XPoint(40, y));
		y += 34;
		graphics.DrawString(invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote ? "CREDIT NOTE" : "INVOICE", TitleFont, XBrushes.Black, new XPoint(40, y));
		graphics.DrawString(invoice.InvoiceNumber, HeadingFont, XBrushes.Black, new XPoint(390, y));
		y += 30;
		graphics.DrawString($"Issue date: {invoice.IssueDate:yyyy-MM-dd}", BodyFont, XBrushes.Black, new XPoint(40, y));
		if (invoice.DueDate is { } dueDate) graphics.DrawString($"Due date: {dueDate:yyyy-MM-dd}", BodyFont, XBrushes.Black, new XPoint(220, y));
		y += 28;
		graphics.DrawString("Bill to", HeadingFont, XBrushes.Black, new XPoint(40, y));
		y += 15;
		graphics.DrawString(invoice.Buyer.Name, BodyFont, XBrushes.Black, new XPoint(40, y));
		y += 14;
		foreach (var addressLine in new[] { invoice.Buyer.AddressLine1, invoice.Buyer.AddressLine2, $"{invoice.Buyer.PostalCode} {invoice.Buyer.City}", invoice.Buyer.CountryCode }.Where(value => !string.IsNullOrWhiteSpace(value)))
		{
			graphics.DrawString(addressLine!, BodyFont, XBrushes.Black, new XPoint(40, y));
			y += 13;
		}
		y += 18;
		graphics.DrawLine(XPens.LightGray, 40, y, 555, y);
		y += 18;
		graphics.DrawString("Item", HeadingFont, XBrushes.Black, new XPoint(40, y));
		graphics.DrawString("Description", HeadingFont, XBrushes.Black, new XPoint(105, y));
		graphics.DrawString("Qty", HeadingFont, XBrushes.Black, new XPoint(355, y));
		graphics.DrawString("Unit", HeadingFont, XBrushes.Black, new XPoint(405, y));
		graphics.DrawString("Tax", HeadingFont, XBrushes.Black, new XPoint(470, y));
		graphics.DrawString("Net", HeadingFont, XBrushes.Black, new XPoint(520, y));
		y += 18;

		decimal netTotal = 0m;
		decimal taxTotal = 0m;
		foreach (var line in invoice.Lines)
		{
			if (y > page.Height.Point - 100)
			{
				graphics.Dispose();
				page = document.AddPage();
				graphics = XGraphics.FromPdfPage(page);
				y = 50;
			}
			var net = Math.Round(line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
			var tax = Math.Round(net * line.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
			netTotal += net;
			taxTotal += tax;
			graphics.DrawString(Trim(line.SellerItemIdentifier ?? line.Id, 10), BodyFont, XBrushes.Black, new XPoint(40, y));
			graphics.DrawString(Trim(line.Name, 42), BodyFont, XBrushes.Black, new XPoint(105, y));
			graphics.DrawString(line.Quantity.ToString("0.###", CultureInfo.InvariantCulture), BodyFont, XBrushes.Black, new XPoint(355, y));
			graphics.DrawString(line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture), BodyFont, XBrushes.Black, new XPoint(405, y));
			graphics.DrawString($"{line.TaxRate:0.##}%", BodyFont, XBrushes.Black, new XPoint(470, y));
			graphics.DrawString(net.ToString("0.00", CultureInfo.InvariantCulture), BodyFont, XBrushes.Black, new XPoint(520, y));
			y += 17;
		}
		y += 12;
		graphics.DrawLine(XPens.LightGray, 350, y, 555, y);
		y += 18;
		graphics.DrawString("Net", BodyFont, XBrushes.Black, new XPoint(400, y));
		graphics.DrawString($"{netTotal:0.00} {invoice.Currency}", BodyFont, XBrushes.Black, new XPoint(490, y));
		y += 16;
		graphics.DrawString("Tax", BodyFont, XBrushes.Black, new XPoint(400, y));
		graphics.DrawString($"{taxTotal:0.00} {invoice.Currency}", BodyFont, XBrushes.Black, new XPoint(490, y));
		y += 16;
		graphics.DrawString("Total", HeadingFont, XBrushes.Black, new XPoint(400, y));
		graphics.DrawString($"{netTotal + taxTotal:0.00} {invoice.Currency}", HeadingFont, XBrushes.Black, new XPoint(490, y));
		var footerY = page.Height.Point - 45;
		graphics.DrawLine(XPens.LightGray, 40, footerY - 10, 555, footerY - 10);
		graphics.DrawString(Trim($"{invoice.Seller.AddressLine1} · {invoice.Seller.PostalCode} {invoice.Seller.City} · {invoice.Seller.CountryCode}", 100), SmallFont, XBrushes.Gray, new XPoint(40, footerY));
		graphics.Dispose();
	}

	private static string BuildXmp(ElectronicInvoice invoice, DateTime createdAtUtc)
	{
		var title = Escape($"{(invoice.TypeCode == ElectronicInvoiceTypeCode.CreditNote ? "Credit Note" : "Invoice")} {invoice.InvoiceNumber}");
		var creator = Escape(invoice.Seller.Name);
		return $"""
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
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
	private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";
}
