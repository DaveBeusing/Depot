// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SalesInvoiceFinalizationTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-invoice-finalization-{Guid.NewGuid():N}.db");
	private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"INV-000042-{Guid.NewGuid():N}.xml");
	private readonly SqliteConnectionFactory _factory;
	private readonly DatabaseAccess _database;

	public SalesInvoiceFinalizationTests()
	{
		_factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(_factory).Initialize();
		SalesSchemaMigration.Migrate(_factory);
		_database = new DatabaseAccess(_factory);
		SeedFinalizableInvoice();
	}

	[Fact]
	public async Task FinalizationPreservesBuyerAndExportsExactStoredXml()
	{
		var finalizedAt = new DateTime(2026, 8, 23, 20, 0, 0, DateTimeKind.Utc);
		var runner = new DatabaseTransactionRunner(_database);
		var created = await runner.ExecuteAsync(
			(transaction, token) => SalesInvoiceFinalizationService.FinalizeAsync(transaction, Invoice(), Issuer(), finalizedAt, token),
			CancellationToken.None);

		Assert.Equal("Original Buyer GmbH", created.Buyer.Name);
		Assert.Equal("DE222222222", created.Buyer.VatIdentifier);
		Assert.Equal("Buyer Street 7", created.Buyer.Street);
		Assert.Equal("04011000-12345-34", created.Buyer.BuyerReference);
		Assert.Equal(64, created.XRechnungSha256.Length);
		Assert.Contains("urn:xeinkauf.de:kosit:xrechnung_3.0", created.XRechnungXml, StringComparison.Ordinal);

		await _database.ExecuteAsync(
			"UPDATE Customers SET Name='Renamed Buyer GmbH',VatId='DE999999999',BillingStreet='Changed Street 99',BuyerReference='CHANGED' WHERE Id=1;",
			CancellationToken.None);

		var service = new SalesInvoiceFinalizationService(_database);
		var stored = service.LoadRequired(42);
		Assert.Equal("Original Buyer GmbH", stored.Buyer.Name);
		Assert.Equal("DE222222222", stored.Buyer.VatIdentifier);
		Assert.Equal("Buyer Street 7", stored.Buyer.Street);
		Assert.Equal("04011000-12345-34", stored.Buyer.BuyerReference);
		Assert.Equal(created.XRechnungXml, stored.XRechnungXml);
		Assert.Equal(created.XRechnungSha256, stored.XRechnungSha256);

		service.ExportXRechnung(42, _exportPath);
		Assert.Equal(created.XRechnungXml, await File.ReadAllTextAsync(_exportPath));
	}

	[Fact]
	public async Task FinalizationRejectsIncompleteBuyerIdentityWithoutPersistingRecord()
	{
		await _database.ExecuteAsync("UPDATE Customers SET BuyerReference=NULL WHERE Id=1;", CancellationToken.None);
		var runner = new DatabaseTransactionRunner(_database);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(
			(transaction, token) => SalesInvoiceFinalizationService.FinalizeAsync(transaction, Invoice(), Issuer(), DateTime.UtcNow, token),
			CancellationToken.None));

		Assert.Contains("Buyer reference", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(0L, Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM SalesInvoiceFinalizations WHERE SalesInvoiceId=42;", CancellationToken.None)));
	}

	[Fact]
	public async Task FinalizationRejectsInvalidBuyerCountryCodeSyntax()
	{
		await _database.ExecuteAsync("UPDATE Customers SET BillingCountryCode='1!' WHERE Id=1;", CancellationToken.None);
		var runner = new DatabaseTransactionRunner(_database);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(
			(transaction, token) => SalesInvoiceFinalizationService.FinalizeAsync(transaction, Invoice(), Issuer(), DateTime.UtcNow, token),
			CancellationToken.None));

		Assert.Contains("country code", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("ASCII letters", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(0L, Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM SalesInvoiceFinalizations WHERE SalesInvoiceId=42;", CancellationToken.None)));
	}

	[Fact]
	public async Task LoadingFinalizationRejectsTamperedXRechnungXml()
	{
		var runner = new DatabaseTransactionRunner(_database);
		await runner.ExecuteAsync(
			(transaction, token) => SalesInvoiceFinalizationService.FinalizeAsync(transaction, Invoice(), Issuer(), DateTime.UtcNow, token),
			CancellationToken.None);
		await _database.ExecuteAsync("UPDATE SalesInvoiceFinalizations SET XRechnungXml=XRechnungXml || ' ' WHERE SalesInvoiceId=42;", CancellationToken.None);

		var service = new SalesInvoiceFinalizationService(_database);
		var exception = Assert.Throws<InvalidOperationException>(() => service.LoadRequired(42));
		Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task FinalizationRejectsZeroRatedLinesUntilTaxSemanticsAreExplicit()
	{
		var invoice = Invoice();
		invoice.Lines = [new SalesInvoiceLine { LineNumber = 1, PartNumber = "ZERO", Description = "Zero rated item", Quantity = 1, UnitPrice = 10m, TaxRate = 0m }];
		var runner = new DatabaseTransactionRunner(_database);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(
			(transaction, token) => SalesInvoiceFinalizationService.FinalizeAsync(transaction, invoice, Issuer(), DateTime.UtcNow, token),
			CancellationToken.None));

		Assert.Contains("explicit EN 16931 tax category", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	private void SeedFinalizableInvoice()
	{
		using var connection = _factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA foreign_keys=OFF;";
		command.ExecuteNonQuery();
		command.CommandText = "INSERT INTO Customers (Id,CustomerNumber,Name,BillingAddress,ContactName,Email,Phone,TaxId,VatId,BuyerReference,EInvoiceEndpoint,EInvoiceEndpointScheme,BillingStreet,BillingAddressLine2,BillingPostalCode,BillingCity,BillingCountryCode,PaymentTermsDays,Currency,IsActive) VALUES (1,'CU-000001','Original Buyer GmbH','Buyer Street 7\n53111 Bonn','Max Buyer','accounting@buyer.test','+49 228 222222','12/345/67890','DE222222222','04011000-12345-34','rechnung@buyer.test','EM','Buyer Street 7','Accounts Payable','53111','Bonn','DE',30,'EUR',1);";
		command.ExecuteNonQuery();
		command.CommandText = "INSERT INTO SalesInvoices (Id,InvoiceNumber,CustomerId,SalesOrderId,ShipmentId,InvoiceDate,DueDate,Currency,Status,CustomerReference,BillingAddress,Notes,CreatedByUserId,Version) VALUES (42,'INV-000042',1,900,901,'2026-08-23','2026-09-22','EUR',1,'PO-4711','Buyer Street 7\n53111 Bonn','Finalization test',1,1);";
		command.ExecuteNonQuery();
		command.CommandText = "PRAGMA foreign_keys=ON;";
		command.ExecuteNonQuery();
	}

	private static SalesInvoice Invoice() => new()
	{
		Id = 42,
		InvoiceNumber = "INV-000042",
		CustomerId = 1,
		CustomerName = "Original Buyer GmbH",
		SalesOrderId = 900,
		SalesOrderNumber = "SO-000900",
		ShipmentId = 901,
		ShipmentNumber = "SH-000901",
		InvoiceDate = new DateTime(2026, 8, 23),
		DueDate = new DateTime(2026, 9, 22),
		Currency = "EUR",
		CustomerReference = "PO-4711",
		BillingAddress = "Buyer Street 7\n53111 Bonn",
		Notes = "Finalization test",
		Status = SalesInvoiceStatus.Draft,
		Lines = [new SalesInvoiceLine { LineNumber = 1, PartNumber = "ITEM-1", Description = "Professional service", Quantity = 2, UnitPrice = 100m, DiscountPercent = 0m, TaxRate = 19m }]
	};

	private static DocumentIssuerProfile Issuer() => new(
		"Seller GmbH",
		"Seller",
		"GmbH",
		"Seller Street 1",
		string.Empty,
		"53113",
		"Bonn",
		"DE",
		"Amtsgericht Bonn HRB 12345",
		"VAT ID: DE111111111",
		"Jane Seller",
		"office@seller.test",
		"+49 228 111111",
		"https://seller.test",
		"invoice@seller.test",
		"Seller GmbH",
		"Test Bank",
		"DE02120300000000202051",
		"BYLADEM1001",
		"invoice@seller.test",
		"EM",
		string.Empty);

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		if (File.Exists(_exportPath)) File.Delete(_exportPath);
	}
}
