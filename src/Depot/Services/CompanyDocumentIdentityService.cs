// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed class CompanyDocumentIdentityService
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly DatabaseAccess _dataAccess;
	private readonly DatabaseProvider _provider;

	public CompanyDocumentIdentityService(DatabaseAccess dataAccess, DatabaseProvider provider)
	{
		_dataAccess = dataAccess;
		_provider = provider;
	}

	public DocumentIssuerProfile Load()
	{
		EnsureSchema();
		var rows = _dataAccess.Query(
			"SELECT Payload FROM CompanyProfile WHERE Id = 1;",
			reader => reader.GetString(0));
		if (rows.Count == 0)
			throw new InvalidOperationException("Company master data must be configured before business documents can be generated.");

		var profile = JsonSerializer.Deserialize<CompanyProfile>(rows[0], JsonOptions)
			?? throw new InvalidOperationException("Company master data could not be read.");
		var errors = CompanyProfileService.Validate(profile);
		if (errors.Count > 0)
			throw new InvalidOperationException("Company master data is incomplete: " + string.Join("; ", errors));

		return Project(profile);
	}

	public static DocumentIssuerProfile Project(CompanyProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		var errors = CompanyProfileService.Validate(profile);
		if (errors.Count > 0)
			throw new ArgumentException("Company master data is incomplete: " + string.Join("; ", errors), nameof(profile));

		var displayName = string.IsNullOrWhiteSpace(profile.TradingName) ? profile.LegalName : profile.TradingName;
		var registrationLine = profile.IsRegisteredEntity
			? string.Join(" ", new[] { profile.RegisterCourt, profile.RegisterType, profile.RegisterNumber }.Where(value => !string.IsNullOrWhiteSpace(value)))
			: string.Empty;
		var taxLine = string.Join(" · ", new[]
		{
			string.IsNullOrWhiteSpace(profile.VatId) ? null : $"VAT ID: {profile.VatId}",
			string.IsNullOrWhiteSpace(profile.TaxNumber) ? null : $"Tax no.: {profile.TaxNumber}"
		}.Where(value => !string.IsNullOrWhiteSpace(value)));

		return new DocumentIssuerProfile(
			profile.LegalName.Trim(), displayName.Trim(), profile.LegalForm.Trim(), profile.Street.Trim(), profile.AddressLine2.Trim(),
			profile.PostalCode.Trim(), profile.City.Trim(), profile.CountryCode.Trim().ToUpperInvariant(), registrationLine, taxLine,
			profile.ManagingDirectors.Trim(), profile.Email.Trim(), profile.Phone.Trim(), profile.Website.Trim(), profile.InvoiceEmail.Trim(),
			profile.AccountHolder.Trim(), profile.BankName.Trim(), profile.Iban.Trim(), profile.Bic.Trim(), profile.EInvoiceEndpoint.Trim(),
			profile.EInvoiceEndpointScheme.Trim(), profile.LegalFooterAdditionalText.Trim());
	}

	public static ElectronicInvoiceParty ToElectronicInvoiceSeller(DocumentIssuerProfile issuer)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		return new ElectronicInvoiceParty
		{
			Name = issuer.LegalName,
			TradingName = issuer.DisplayName == issuer.LegalName ? null : issuer.DisplayName,
			ElectronicAddress = string.IsNullOrWhiteSpace(issuer.EInvoiceEndpoint) ? issuer.InvoiceEmail : issuer.EInvoiceEndpoint,
			ElectronicAddressScheme = string.IsNullOrWhiteSpace(issuer.EInvoiceEndpointScheme) ? "EM" : issuer.EInvoiceEndpointScheme,
			TaxIdentifier = ExtractIdentifier(issuer.TaxLine, "Tax no.: "),
			VatIdentifier = ExtractIdentifier(issuer.TaxLine, "VAT ID: "),
			RegistrationIdentifier = ExtractRegistrationIdentifier(issuer.RegistrationLine),
			AddressLine1 = issuer.Street,
			AddressLine2 = string.IsNullOrWhiteSpace(issuer.AddressLine2) ? null : issuer.AddressLine2,
			City = issuer.City,
			PostalCode = issuer.PostalCode,
			CountryCode = issuer.CountryCode,
			ContactName = issuer.LegalName,
			ContactEmail = string.IsNullOrWhiteSpace(issuer.InvoiceEmail) ? issuer.Email : issuer.InvoiceEmail,
			ContactPhone = issuer.Phone
		};
	}

	private void EnsureSchema()
	{
		var sql = _provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS CompanyProfile (Id INTEGER NOT NULL PRIMARY KEY CHECK(Id = 1), Payload TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1);",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS CompanyProfile (Id int NOT NULL PRIMARY KEY, Payload longtext NOT NULL, UpdatedUtc varchar(40) NOT NULL, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;",
			DatabaseProvider.SqlServer => "IF OBJECT_ID('dbo.CompanyProfile', 'U') IS NULL CREATE TABLE dbo.CompanyProfile (Id int NOT NULL CONSTRAINT PK_CompanyProfile PRIMARY KEY, Payload nvarchar(max) NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_CompanyProfile_Version DEFAULT 1);",
			_ => throw new NotSupportedException($"Unsupported database provider '{_provider}'.")
		};
		_dataAccess.Execute(sql);
	}

	private static string? ExtractIdentifier(string line, string prefix) =>
		line.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].Trim();

	private static string? ExtractRegistrationIdentifier(string line)
	{
		if (string.IsNullOrWhiteSpace(line)) return null;
		var parts = line.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		return parts.Length == 0 ? null : parts[^1];
	}
}
