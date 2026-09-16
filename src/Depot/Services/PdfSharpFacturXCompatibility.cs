// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Text;

using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Depot.Services;

internal sealed record FacturXPdfInspection(
	string FileName,
	string AfRelationship,
	string MimeSubtype,
	byte[] EmbeddedXml,
	string Xmp);

/// <summary>
/// Bridges the Factur-X/PDF-A-3 requirements to the stable PDFsharp 6.2.x API surface.
/// </summary>
internal static class PdfSharpFacturXCompatibility
{
	private const string MetadataKey = "/Metadata";
	private const string EmbeddedFilesKey = "/EmbeddedFiles";
	private const string AssociatedFilesKey = "/AF";
	private const string XmlMimeName = "/text#2Fxml";

	public static void Configure(PdfDocument document, byte[] xmlBytes, DateTime createdAtUtc)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(xmlBytes);
		if (createdAtUtc.Kind != DateTimeKind.Utc)
			throw new ArgumentException("Factur-X PDF timestamp must be UTC.", nameof(createdAtUtc));

		document.Version = 17;
		ConfigureOutputIntent(document);
		ConfigureEmbeddedInvoice(document, xmlBytes, createdAtUtc);
	}

	public static byte[] SaveWithXmp(PdfDocument document, string xmp)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(xmp);
		using var stream = new MemoryStream();
		document.Save(stream, false);
		return ReplaceMetadataIncrementally(stream.ToArray(), xmp);
	}

	internal static FacturXPdfInspection Inspect(byte[] pdfBytes)
	{
		ArgumentNullException.ThrowIfNull(pdfBytes);
		using var stream = new MemoryStream(pdfBytes, writable: false);
		using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
		var catalog = document.Internals.Catalog;

		var associated = catalog.Elements.GetArray(AssociatedFilesKey)
			?? throw new InvalidOperationException("Factur-X PDF does not contain an associated-files array.");
		if (associated.Elements.Count != 1)
			throw new InvalidOperationException("Factur-X PDF must contain exactly one associated invoice file.");
		var fileSpecification = associated.Elements.GetDictionary(0)
			?? throw new InvalidOperationException("Factur-X associated file specification is invalid.");
		var afRelationship = fileSpecification.Elements.GetName("/AFRelationship");

		var embeddedFilesTree = catalog.Names.Elements.GetDictionary(EmbeddedFilesKey)
			?? throw new InvalidOperationException("Factur-X embedded-files name tree is missing.");
		var names = embeddedFilesTree.Elements.GetArray("/Names")
			?? throw new InvalidOperationException("Factur-X embedded-files name array is missing.");
		if (names.Elements.Count != 2)
			throw new InvalidOperationException("Factur-X embedded-files name tree must contain exactly one file.");
		var fileName = names.Elements.GetString(0);
		var namedSpecification = names.Elements.GetDictionary(1)
			?? throw new InvalidOperationException("Factur-X embedded file specification is invalid.");
		if (PdfInternals.GetObjectNumber(namedSpecification) != PdfInternals.GetObjectNumber(fileSpecification))
			throw new InvalidOperationException("Factur-X associated-file and embedded-file references do not match.");

		var embeddedFiles = fileSpecification.Elements.GetDictionary("/EF")
			?? throw new InvalidOperationException("Factur-X embedded-file dictionary is missing.");
		var embeddedFile = embeddedFiles.Elements.GetDictionary("/F")
			?? throw new InvalidOperationException("Factur-X embedded XML stream is missing.");
		var mimeSubtype = embeddedFile.Elements.GetName("/Subtype");
		var xml = embeddedFile.Stream?.UnfilteredValue
			?? throw new InvalidOperationException("Factur-X embedded XML stream has no data.");

		var metadata = Dereference(catalog.Elements[MetadataKey]) as PdfDictionary
			?? throw new InvalidOperationException("Factur-X XMP metadata stream is missing.");
		var xmpBytes = metadata.Stream?.UnfilteredValue
			?? throw new InvalidOperationException("Factur-X XMP metadata stream has no data.");
		return new FacturXPdfInspection(fileName, afRelationship, mimeSubtype, xml, Encoding.UTF8.GetString(xmpBytes));
	}

	private static void ConfigureEmbeddedInvoice(PdfDocument document, byte[] xmlBytes, DateTime createdAtUtc)
	{
		using var xmlStream = new MemoryStream(xmlBytes, writable: false);
		var embeddedFile = new PdfEmbeddedFileStream(document, xmlStream);
		embeddedFile.Elements.SetName(PdfEmbeddedFileStream.Keys.Subtype, XmlMimeName);
		var parameters = embeddedFile.Elements.GetDictionary(PdfEmbeddedFileStream.Keys.Params)
			?? throw new InvalidOperationException("PDFsharp did not create embedded-file parameters.");
		parameters.Elements.SetDateTime("/CreationDate", createdAtUtc);
		parameters.Elements.SetDateTime("/ModDate", createdAtUtc);

		var fileSpecification = new PdfFileSpecification(document, embeddedFile, ZugferdFacturXConformance.XmlFileName);
		fileSpecification.Elements.SetString("/Desc", "XRechnung invoice data");
		fileSpecification.Elements.SetName("/AFRelationship", "/Alternative");
		document.Internals.AddObject(fileSpecification);
		var specificationReference = PdfInternals.GetReference(fileSpecification)
			?? throw new InvalidOperationException("PDFsharp did not create an embedded-file reference.");

		var names = new PdfArray(document);
		names.Elements.Add(new PdfString(ZugferdFacturXConformance.XmlFileName));
		names.Elements.Add(specificationReference);
		var embeddedFilesTree = new PdfDictionary(document);
		embeddedFilesTree.Elements.SetObject("/Names", names);
		document.Internals.AddObject(embeddedFilesTree);
		document.Internals.Catalog.Names.Elements.SetReference(EmbeddedFilesKey, embeddedFilesTree);

		var associatedFiles = new PdfArray(document);
		associatedFiles.Elements.Add(specificationReference);
		document.Internals.Catalog.Elements.SetObject(AssociatedFilesKey, associatedFiles);
	}

	private static void ConfigureOutputIntent(PdfDocument document)
	{
		using var profileStream = typeof(PdfDocument).Assembly.GetManifestResourceStream("PdfSharp.Resources.sRGB2014.icc")
			?? throw new InvalidOperationException("PDFsharp sRGB output profile is unavailable.");
		using var profileBuffer = new MemoryStream();
		profileStream.CopyTo(profileBuffer);

		var profile = new PdfDictionary(document);
		profile.Elements.SetInteger("/N", 3);
		profile.CreateStream(profileBuffer.ToArray());
		document.Internals.AddObject(profile);

		var outputIntent = new PdfDictionary(document);
		outputIntent.Elements.SetName("/Type", "/OutputIntent");
		outputIntent.Elements.SetName("/S", "/GTS_PDFA1");
		outputIntent.Elements.SetString("/OutputConditionIdentifier", "sRGB IEC61966-2.1");
		outputIntent.Elements.SetString("/RegistryName", "http://www.color.org");
		outputIntent.Elements.SetString("/Info", "sRGB IEC61966-2.1");
		outputIntent.Elements.SetReference("/DestOutputProfile", profile);
		var outputIntents = new PdfArray(document);
		outputIntents.Elements.Add(outputIntent);
		document.Internals.Catalog.Elements.SetObject("/OutputIntents", outputIntents);
	}

	private static byte[] ReplaceMetadataIncrementally(byte[] pdfBytes, string xmp)
	{
		int metadataObjectNumber;
		int metadataGeneration;
		int catalogObjectNumber;
		int catalogGeneration;
		int size;
		using (var readStream = new MemoryStream(pdfBytes, writable: false))
		using (var document = PdfReader.Open(readStream, PdfDocumentOpenMode.Modify))
		{
			var catalog = document.Internals.Catalog;
			var metadataReference = catalog.Elements.GetReference(MetadataKey)
				?? throw new InvalidOperationException("PDFsharp did not create a document metadata reference.");
			metadataObjectNumber = metadataReference.ObjectNumber;
			metadataGeneration = metadataReference.GenerationNumber;
			catalogObjectNumber = PdfInternals.GetObjectNumber(catalog);
			catalogGeneration = PdfInternals.GenerationNumber(catalog);
			size = document.Internals.GetAllObjects().Select(PdfInternals.GetObjectNumber).DefaultIfEmpty(0).Max() + 1;
		}

		var previousXref = FindPreviousXref(pdfBytes);
		var xmpBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(xmp);
		using var output = new MemoryStream(pdfBytes.Length + xmpBytes.Length + 512);
		output.Write(pdfBytes);
		var metadataOffset = output.Position;
		WriteAscii(output, $"\n{metadataObjectNumber} {metadataGeneration} obj\n<< /Type /Metadata /Subtype /XML /Length {xmpBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
		output.Write(xmpBytes);
		WriteAscii(output, "\nendstream\nendobj\n");
		var xrefOffset = output.Position;
		WriteAscii(output,
			$"xref\n{metadataObjectNumber} 1\n{metadataOffset:0000000000} {metadataGeneration:00000} n \n" +
			$"trailer\n<< /Size {size} /Root {catalogObjectNumber} {catalogGeneration} R /Prev {previousXref} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
		return output.ToArray();
	}

	private static long FindPreviousXref(byte[] pdfBytes)
	{
		var text = Encoding.ASCII.GetString(pdfBytes);
		var marker = text.LastIndexOf("startxref", StringComparison.Ordinal);
		if (marker < 0) throw new InvalidOperationException("PDF does not contain a startxref marker.");
		var index = marker + "startxref".Length;
		while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
		var start = index;
		while (index < text.Length && char.IsDigit(text[index])) index++;
		if (start == index || !long.TryParse(text.AsSpan(start, index - start), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
			throw new InvalidOperationException("PDF startxref value is invalid.");
		return offset;
	}

	private static PdfObject? Dereference(PdfItem? item) => item is PdfReference reference ? reference.Value : item as PdfObject;

	private static void WriteAscii(Stream stream, string value)
	{
		var bytes = Encoding.ASCII.GetBytes(value);
		stream.Write(bytes);
	}
}
