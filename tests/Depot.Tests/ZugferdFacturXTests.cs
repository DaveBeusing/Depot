// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ZugferdFacturXTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-zugferd-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;
	private readonly DatabaseAccess _database;

	public ZugferdFacturXTests()
	{
		_factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(_factory).Initialize();
		SalesSchemaMigration.Migrate(_factory);
		_database = new DatabaseAccess(_factory);
	}

	[Fact]
	public void SalesSchemaFourteenCreatesHybridArtifactStore()
	{
		using var connection = _factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';";
		Assert.Equal(14L, Convert.ToInt64(command.ExecuteScalar()));
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SalesHybridElectronicInvoiceArtifacts';";
		Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
	}

	[Fact]
	public async Task HybridArtifactEmbedsExactFinalizedXRechnungAndPersistsIntegrityEvidence()
	{
		var invoice = Invoice();
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(invoice);
		var xmlHash = Hash(Encoding.UTF8.GetBytes(xml));
		var createdAt = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
		var artifact = ZugferdFacturXService.CreateArtifact(ZugferdFacturXService.InvoiceDocumentType, 42, invoice, xml, xmlHash, createdAt);

		Assert.Equal(ZugferdFacturXConformance.StandardVersion, artifact.StandardVersion);
		Assert.Equal("XRECHNUNG", artifact.Profile);
		Assert.Equal("xrechnung.xml", artifact.XmlFileName);
		Assert.Equal("PDF/A-3B", artifact.PdfAConformance);
		Assert.Equal(xmlHash, artifact.XmlSha256);
		Assert.Equal(Hash(artifact.PdfBytes), artifact.PdfSha256);
		Assert.True(artifact.PdfBytes.Length > 1000);

		var inspection = PdfSharpFacturXCompatibility.Inspect(artifact.PdfBytes);
		Assert.Equal("xrechnung.xml", inspection.FileName);
		Assert.Equal("/Alternative", inspection.AfRelationship);
		Assert.Equal("/text#2Fxml", inspection.MimeSubtype);
		Assert.Equal(Encoding.UTF8.GetBytes(xml), inspection.EmbeddedXml);
		Assert.Contains("<pdfaid:part>3</pdfaid:part>", inspection.Xmp, StringComparison.Ordinal);
		Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", inspection.Xmp, StringComparison.Ordinal);
		Assert.Contains("<fx:DocumentFileName>xrechnung.xml</fx:DocumentFileName>", inspection.Xmp, StringComparison.Ordinal);
		Assert.Contains("<fx:ConformanceLevel>XRECHNUNG</fx:ConformanceLevel>", inspection.Xmp, StringComparison.Ordinal);

		var runner = new DatabaseTransactionRunner(_database);
		await runner.ExecuteAsync((transaction, token) => ZugferdFacturXService.InsertAsync(transaction, artifact, token), CancellationToken.None);
		var stored = new ZugferdFacturXService(_database).LoadRequired(ZugferdFacturXService.InvoiceDocumentType, 42);
		Assert.Equal(artifact.PdfSha256, stored.PdfSha256);
		Assert.Equal(artifact.PdfBytes, stored.PdfBytes);
	}

	[Fact]
	public void HybridArtifactRejectsMismatchedFinalizedXmlHash()
	{
		var invoice = Invoice();
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(invoice);
		var exception = Assert.Throws<InvalidOperationException>(() => ZugferdFacturXService.CreateArtifact(
			ZugferdFacturXService.InvoiceDocumentType,
			42,
			invoice,
			xml,
			new string('0', 64),
			new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc)));
		Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task LoadingHybridArtifactRejectsTamperedPdfBytes()
	{
		var invoice = Invoice();
		var xml = new ElectronicInvoiceService().CreateXRechnungXml(invoice);
		var artifact = ZugferdFacturXService.CreateArtifact(
			ZugferdFacturXService.InvoiceDocumentType,
			42,
			invoice,
			xml,
			Hash(Encoding.UTF8.GetBytes(xml)),
			new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));
		var runner = new DatabaseTransactionRunner(_database);
		await runner.ExecuteAsync((transaction, token) => ZugferdFacturXService.InsertAsync(transaction, artifact, token), CancellationToken.None);
		await _database.ExecuteAsync("UPDATE SalesHybridElectronicInvoiceArtifacts SET PdfBytes=X'25504446' WHERE DocumentType='Invoice' AND DocumentId=42;", CancellationToken.None);

		var exception = Assert.Throws<InvalidOperationException>(() => new ZugferdFacturXService(_database).LoadRequired(ZugferdFacturXService.InvoiceDocumentType, 42));
		Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	private static ElectronicInvoice Invoice() => new()
	{
		InvoiceNumber = "INV-F3-0001",
		TypeCode = ElectronicInvoiceTypeCode.Invoice,
		IssueDate = new DateOnly(2026, 9, 16),
		DueDate = new DateOnly(2026, 10, 16),
		Currency = "EUR",
		BuyerReference = "04011000-12345-34",
		Seller = new ElectronicInvoiceParty
		{
			Name = "Seller GmbH",
			ElectronicAddress = "invoice@seller.test",
			ElectronicAddressScheme = "EM",
			VatIdentifier = "DE111111111",
			ContactName = "Seller Contact",
			ContactPhone = "+49 228 5550100",
			ContactEmail = "contact@seller.test",
			AddressLine1 = "Seller Street 1",
			City = "Bonn",
			PostalCode = "53113",
			CountryCode = "DE"
		},
		Buyer = new ElectronicInvoiceParty
		{
			Name = "Buyer GmbH",
			ElectronicAddress = "invoice@buyer.test",
			ElectronicAddressScheme = "EM",
			VatIdentifier = "DE222222222",
			AddressLine1 = "Buyer Street 7",
			City = "Bonn",
			PostalCode = "53111",
			CountryCode = "DE"
		},
		Payment = new ElectronicInvoicePayment
		{
			MeansCode = "58",
			AccountIdentifier = "DE02120300000000202051",
			PaymentReference = "INV-F3-0001"
		},
		Lines =
		[
			new ElectronicInvoiceLine
			{
				Id = "1",
				Name = "Professional service",
				Quantity = 1m,
				UnitPrice = 100m,
				TaxRate = 19m,
				TaxCategoryCode = ElectronicInvoiceTaxCategories.StandardRated,
				SellerItemIdentifier = "ITEM-1"
			}
		]
	};

	private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
