// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed class SalesInvoiceFinalizationService
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly DatabaseAccess _dataAccess;

	public SalesInvoiceFinalizationService(DatabaseAccess dataAccess)
	{
		_dataAccess = dataAccess;
	}

	public SalesInvoiceFinalization? TryLoad(long salesInvoiceId)
	{
		var rows = _dataAccess.Query(
			"SELECT BuyerPayload,XRechnungXml,XRechnungSha256,FinalizedAtUtc FROM SalesInvoiceFinalizations WHERE SalesInvoiceId=$Id;",
			reader => new SalesInvoiceFinalization(
				salesInvoiceId,
				JsonSerializer.Deserialize<DocumentBuyerProfile>(reader.GetString(0), JsonOptions) ?? throw new InvalidOperationException("Stored buyer snapshot could not be read."),
				reader.GetString(1),
				reader.GetString(2),
				DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
			new DatabaseParameter("$Id", salesInvoiceId));
		if (rows.Count == 0) return null;
		var value = rows[0];
		VerifyHash(value.XRechnungXml, value.XRechnungSha256);
		return value;
	}

	public SalesInvoiceFinalization LoadRequired(long salesInvoiceId) =>
		TryLoad(salesInvoiceId) ?? throw new InvalidOperationException($"Posted sales invoice {salesInvoiceId} has no finalized buyer/XRechnung record.");

	public void ExportXRechnung(long salesInvoiceId, string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var finalization = LoadRequired(salesInvoiceId);
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllText(path, finalization.XRechnungXml, new UTF8Encoding(false));
	}

	public static async Task<SalesInvoiceFinalization> FinalizeAsync(
		DatabaseTransactionContext transaction,
		SalesInvoice invoice,
		DocumentIssuerProfile issuer,
		DateTime finalizedAtUtc,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		ArgumentNullException.ThrowIfNull(invoice);
		ArgumentNullException.ThrowIfNull(issuer);

		var existing = await transaction.Session.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM SalesInvoiceFinalizations WHERE SalesInvoiceId=$Id;",
			cancellationToken,
			new DatabaseParameter("$Id", invoice.Id));
		if (Convert.ToInt32(existing, CultureInfo.InvariantCulture) != 0)
			throw new InvalidOperationException("This sales invoice is already finalized and its buyer/XRechnung identity cannot be replaced.");

		var customerRows = await transaction.Session.QueryAsync(
			"SELECT CustomerNumber,Name,ContactName,Email,Phone,TaxId,VatId,BuyerReference,EInvoiceEndpoint,EInvoiceEndpointScheme,BillingStreet,BillingAddressLine2,BillingPostalCode,BillingCity,BillingCountryCode FROM Customers WHERE Id=$Id;",
			reader => new Customer
			{
				Id = invoice.CustomerId,
				CustomerNumber = reader.GetString(0), Name = reader.GetString(1), ContactName = reader.IsDBNull(2) ? null : reader.GetString(2),
				Email = reader.IsDBNull(3) ? null : reader.GetString(3), Phone = reader.IsDBNull(4) ? null : reader.GetString(4), TaxId = reader.IsDBNull(5) ? null : reader.GetString(5), VatId = reader.IsDBNull(6) ? null : reader.GetString(6),
				BuyerReference = reader.IsDBNull(7) ? null : reader.GetString(7), EInvoiceEndpoint = reader.IsDBNull(8) ? null : reader.GetString(8), EInvoiceEndpointScheme = reader.IsDBNull(9) ? null : reader.GetString(9),
				BillingStreet = reader.IsDBNull(10) ? null : reader.GetString(10), BillingAddressLine2 = reader.IsDBNull(11) ? null : reader.GetString(11), BillingPostalCode = reader.IsDBNull(12) ? null : reader.GetString(12), BillingCity = reader.IsDBNull(13) ? null : reader.GetString(13), BillingCountryCode = reader.IsDBNull(14) ? null : reader.GetString(14)
			},
			cancellationToken,
			new DatabaseParameter("$Id", invoice.CustomerId));
		if (customerRows.Count == 0) throw new InvalidOperationException("Customer was not found while finalizing the invoice.");
		var customer = customerRows[0];
		var identityErrors = CustomerService.ValidateElectronicInvoiceIdentity(customer);
		if (identityErrors.Count > 0) throw new InvalidOperationException("Customer electronic-invoice identity is incomplete: " + string.Join("; ", identityErrors));
		if (string.IsNullOrWhiteSpace(invoice.BillingAddress)) throw new InvalidOperationException("The invoice has no immutable billing-address snapshot.");

		var buyer = new DocumentBuyerProfile(
			customer.Id,
			customer.CustomerNumber,
			invoice.CustomerName,
			customer.BuyerReference!,
			customer.EInvoiceEndpoint!,
			customer.EInvoiceEndpointScheme!,
			customer.TaxId ?? string.Empty,
			customer.VatId ?? string.Empty,
			invoice.BillingAddress,
			customer.BillingStreet!,
			customer.BillingAddressLine2 ?? string.Empty,
			customer.BillingPostalCode!,
			customer.BillingCity!,
			customer.BillingCountryCode!.ToUpperInvariant(),
			customer.ContactName ?? string.Empty,
			customer.Email ?? string.Empty,
			customer.Phone ?? string.Empty);

		var electronicInvoice = new ElectronicInvoice
		{
			InvoiceNumber = invoice.InvoiceNumber,
			TypeCode = ElectronicInvoiceTypeCode.Invoice,
			IssueDate = DateOnly.FromDateTime(invoice.InvoiceDate),
			DueDate = DateOnly.FromDateTime(invoice.DueDate),
			Currency = invoice.Currency,
			BuyerReference = buyer.BuyerReference,
			PurchaseOrderReference = string.IsNullOrWhiteSpace(invoice.CustomerReference) ? invoice.SalesOrderNumber : invoice.CustomerReference,
			Seller = CompanyDocumentIdentityService.ToElectronicInvoiceSeller(issuer),
			Buyer = ToElectronicInvoiceBuyer(buyer),
			Payment = new ElectronicInvoicePayment
			{
				MeansCode = "58",
				AccountIdentifier = string.IsNullOrWhiteSpace(issuer.Iban) ? null : issuer.Iban,
				AccountName = string.IsNullOrWhiteSpace(issuer.AccountHolder) ? issuer.LegalName : issuer.AccountHolder,
				FinancialInstitutionIdentifier = string.IsNullOrWhiteSpace(issuer.Bic) ? null : issuer.Bic,
				PaymentReference = invoice.InvoiceNumber,
				Terms = $"Payment due by {invoice.DueDate:yyyy-MM-dd}."
			},
			Lines = invoice.Lines.Select(line => new ElectronicInvoiceLine
			{
				Id = line.LineNumber.ToString(CultureInfo.InvariantCulture),
				Name = line.Description,
				Description = line.Description,
				Quantity = line.Quantity,
				UnitCode = "C62",
				UnitPrice = line.UnitPrice,
				DiscountPercent = line.DiscountPercent,
				TaxRate = line.TaxRate,
				TaxCategoryCode = line.TaxRate == 0m ? "Z" : "S",
				SellerItemIdentifier = line.PartNumber
			}).ToArray(),
			Note = invoice.Notes
		};

		var xml = new ElectronicInvoiceService().CreateXRechnungXml(electronicInvoice);
		var hash = ComputeHash(xml);
		var buyerPayload = JsonSerializer.Serialize(buyer, JsonOptions);
		await transaction.Session.ExecuteAsync(
			"INSERT INTO SalesInvoiceFinalizations (SalesInvoiceId,BuyerPayload,XRechnungXml,XRechnungSha256,FinalizedAtUtc) VALUES ($Id,$Buyer,$Xml,$Hash,$At);",
			cancellationToken,
			new DatabaseParameter("$Id", invoice.Id),
			new DatabaseParameter("$Buyer", buyerPayload),
			new DatabaseParameter("$Xml", xml),
			new DatabaseParameter("$Hash", hash),
			new DatabaseParameter("$At", finalizedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
		return new SalesInvoiceFinalization(invoice.Id, buyer, xml, hash, finalizedAtUtc);
	}

	public static ElectronicInvoiceParty ToElectronicInvoiceBuyer(DocumentBuyerProfile buyer) => new()
	{
		Name = buyer.Name,
		ElectronicAddress = buyer.ElectronicAddress,
		ElectronicAddressScheme = buyer.ElectronicAddressScheme,
		TaxIdentifier = string.IsNullOrWhiteSpace(buyer.TaxIdentifier) ? null : buyer.TaxIdentifier,
		VatIdentifier = string.IsNullOrWhiteSpace(buyer.VatIdentifier) ? null : buyer.VatIdentifier,
		RegistrationIdentifier = buyer.CustomerNumber,
		AddressLine1 = buyer.Street,
		AddressLine2 = string.IsNullOrWhiteSpace(buyer.AddressLine2) ? null : buyer.AddressLine2,
		PostalCode = buyer.PostalCode,
		City = buyer.City,
		CountryCode = buyer.CountryCode,
		ContactName = string.IsNullOrWhiteSpace(buyer.ContactName) ? null : buyer.ContactName,
		ContactEmail = string.IsNullOrWhiteSpace(buyer.ContactEmail) ? null : buyer.ContactEmail,
		ContactPhone = string.IsNullOrWhiteSpace(buyer.ContactPhone) ? null : buyer.ContactPhone
	};

	private static string ComputeHash(string xml) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant();
	private static void VerifyHash(string xml, string expectedHash)
	{
		var actual = ComputeHash(xml);
		if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Stored XRechnung XML failed its SHA-256 integrity check.");
	}
}
