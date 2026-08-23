// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed partial class CompanyProfileService
{
	private readonly DatabaseAccess _dataAccess;
	private readonly DatabaseProvider _provider;
	private readonly IAuthorizationService _authorization;
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private static readonly HashSet<string> Incoterms2020 = new(StringComparer.OrdinalIgnoreCase)
	{
		"EXW", "FCA", "CPT", "CIP", "DAP", "DPU", "DDP", "FAS", "FOB", "CFR", "CIF"
	};

	public CompanyProfileService(DatabaseAccess dataAccess, DatabaseProvider provider, IAuthorizationService authorization)
	{
		_dataAccess = dataAccess;
		_provider = provider;
		_authorization = authorization;
	}

	public async Task<CompanyProfile> LoadAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SettingsView);
		await EnsureSchemaAsync(cancellationToken);
		var rows = await _dataAccess.QueryAsync(
			"SELECT Payload, Version FROM CompanyProfile WHERE Id = 1;",
			reader => new { Payload = reader.GetString(0), Version = Convert.ToInt64(reader.GetValue(1)) },
			cancellationToken);
		if (rows.Count == 0) return new CompanyProfile();
		var profile = JsonSerializer.Deserialize<CompanyProfile>(rows[0].Payload, JsonOptions) ?? new CompanyProfile();
		profile.Version = rows[0].Version;
		return profile;
	}

	public async Task<CompanyProfile> SaveAsync(CompanyProfile profile, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		_authorization.RequirePermission(ApplicationPermission.SettingsManage);
		EnsureValid(profile);
		await EnsureSchemaAsync(cancellationToken);
		var payloadProfile = CloneWithoutVersion(profile);
		var payload = JsonSerializer.Serialize(payloadProfile, JsonOptions);
		var updatedUtc = DateTime.UtcNow.ToString("O");

		if (profile.Version == 0)
		{
			try
			{
				await _dataAccess.ExecuteAsync(
					"INSERT INTO CompanyProfile (Id, Payload, UpdatedUtc, Version) VALUES (1, $Payload, $UpdatedUtc, 1);",
					cancellationToken,
					new DatabaseParameter("$Payload", payload),
					new DatabaseParameter("$UpdatedUtc", updatedUtc));
				profile.Version = 1;
				return profile;
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				throw new ConcurrencyConflictException("Company profile was created by another user. Reload and try again.");
			}
		}

		var affected = await _dataAccess.ExecuteAsync(
			"UPDATE CompanyProfile SET Payload = $Payload, UpdatedUtc = $UpdatedUtc, Version = Version + 1 WHERE Id = 1 AND Version = $Version;",
			cancellationToken,
			new DatabaseParameter("$Payload", payload),
			new DatabaseParameter("$UpdatedUtc", updatedUtc),
			new DatabaseParameter("$Version", profile.Version));
		if (affected != 1) throw new ConcurrencyConflictException("Company profile was changed by another user.");
		profile.Version++;
		return profile;
	}

	public static IReadOnlyList<string> Validate(CompanyProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		var errors = new List<string>();
		Require(profile.LegalName, "Legal company name", errors);
		Require(profile.LegalForm, "Legal form", errors);
		Require(profile.Street, "Street and house number", errors);
		Require(profile.City, "City", errors);
		ValidateCountryCode(profile.CountryCode, "Country code", required: true, errors);
		ValidateCountryCode(profile.TaxResidenceCountryCode, "Tax residence country code", required: true, errors);

		if (string.Equals(profile.CountryCode, "DE", StringComparison.OrdinalIgnoreCase))
		{
			Require(profile.PostalCode, "Postal code", errors);
			Require(profile.RegisteredOffice, "Registered office / seat", errors);
		}

		if (profile.IsRegisteredEntity)
		{
			Require(profile.RegisterCourt, "Registration authority / court", errors);
			Require(profile.RegisterNumber, "Company registration number", errors);
		}

		if (RequiresGermanManagementDisclosure(profile))
			Require(profile.ManagingDirectors, "Legal representatives / management", errors);
		if (profile.HasSupervisoryBoard)
			Require(profile.SupervisoryBoardChair, "Supervisory board chair", errors);
		if (profile.PublishesShareCapital)
		{
			Require(profile.ShareCapital, "Share / registered capital", errors);
			Require(profile.OutstandingCapital, "Outstanding capital contributions", errors);
		}
		if (profile.IsInLiquidation)
			Require(profile.Liquidators, "Liquidator(s)", errors);
		if (profile.IsBranch)
		{
			Require(profile.BranchName, "Branch name", errors);
			Require(profile.BranchRegistrationAuthority, "Branch registration authority", errors);
			Require(profile.BranchRegistrationNumber, "Branch registration number", errors);
		}

		if (string.IsNullOrWhiteSpace(profile.TaxNumber) &&
			string.IsNullOrWhiteSpace(profile.VatId) &&
			string.IsNullOrWhiteSpace(profile.AdditionalTaxRegistrations))
		{
			errors.Add("At least one tax registration (tax number, VAT ID, or additional tax registration) is required.");
		}

		ValidateNoSpaces(profile.VatId, "VAT identification number", errors);
		ValidateCountryCode(profile.ProfessionalTitleCountryCode, "Professional-title country code", required: false, errors);
		ValidateAdditionalRegistrations(profile.AdditionalTaxRegistrations, "Additional tax registration", errors);
		ValidateAdditionalRegistrations(profile.AdditionalRegulatoryRegistrations, "Additional regulatory registration", errors);

		if (profile.HasFiscalRepresentative)
		{
			Require(profile.FiscalRepresentativeName, "Fiscal representative name", errors);
			Require(profile.FiscalRepresentativeVatId, "Fiscal representative VAT ID", errors);
			Require(profile.FiscalRepresentativeAddress, "Fiscal representative address", errors);
		}

		if (!string.IsNullOrWhiteSpace(profile.EoriNumber) && !EoriRegex().IsMatch(NormalizeIdentifier(profile.EoriNumber)))
			errors.Add("EORI number must start with a two-letter country code followed by up to 15 alphanumeric characters.");
		if (!string.IsNullOrWhiteSpace(profile.LegalEntityIdentifier) && !LeiRegex().IsMatch(NormalizeIdentifier(profile.LegalEntityIdentifier)))
			errors.Add("Legal Entity Identifier (LEI) must contain exactly 20 alphanumeric characters.");
		if (!string.IsNullOrWhiteSpace(profile.Gln) && !GlnRegex().IsMatch(NormalizeIdentifier(profile.Gln)))
			errors.Add("GLN must contain 13 digits.");
		if (!string.IsNullOrWhiteSpace(profile.DunsNumber) && !DunsRegex().IsMatch(NormalizeIdentifier(profile.DunsNumber)))
			errors.Add("D-U-N-S number must contain 9 digits.");
		if (!string.IsNullOrWhiteSpace(profile.Iban) && !IsValidIban(profile.Iban))
			errors.Add("IBAN is not structurally valid.");
		if (!string.IsNullOrWhiteSpace(profile.Bic) && !BicRegex().IsMatch(NormalizeIdentifier(profile.Bic)))
			errors.Add("BIC must contain 8 or 11 alphanumeric characters.");
		if (!string.IsNullOrWhiteSpace(profile.SepaCreditorIdentifier) && !SepaCreditorRegex().IsMatch(NormalizeIdentifier(profile.SepaCreditorIdentifier)))
			errors.Add("SEPA creditor identifier must start with a two-letter country code and contain 7 to 35 alphanumeric characters in total.");
		if (!string.IsNullOrWhiteSpace(profile.DefaultCurrency) && !CurrencyRegex().IsMatch(profile.DefaultCurrency.Trim().ToUpperInvariant()))
			errors.Add("Default currency must be a three-letter ISO-style currency code.");
		if (!string.IsNullOrWhiteSpace(profile.DefaultIncoterm) && !Incoterms2020.Contains(profile.DefaultIncoterm.Trim()))
			errors.Add("Default Incoterm must be one of the Incoterms 2020 rules (EXW, FCA, CPT, CIP, DAP, DPU, DDP, FAS, FOB, CFR, CIF).");
		if (!string.IsNullOrWhiteSpace(profile.DefaultIncoterm) && string.IsNullOrWhiteSpace(profile.DefaultIncotermPlace))
			errors.Add("A named place/port is required when a default Incoterm is configured.");
		if (profile.PaymentTermsDays is < 0 or > 365)
			errors.Add("Payment terms must be between 0 and 365 days.");

		return errors;
	}

	public static IReadOnlyList<string> GetRecommendations(CompanyProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		var recommendations = new List<string>();
		if (string.IsNullOrWhiteSpace(profile.Email)) recommendations.Add("Add a general business email address for commercial documents.");
		if (string.IsNullOrWhiteSpace(profile.Website)) recommendations.Add("Add the official company website.");
		if (string.IsNullOrWhiteSpace(profile.InvoiceEmail)) recommendations.Add("Add a dedicated invoice/contact email if invoices use a different mailbox.");
		if (string.IsNullOrWhiteSpace(profile.Iban)) recommendations.Add("Add payment account details if invoices request bank transfer.");
		if (string.IsNullOrWhiteSpace(profile.EoriNumber)) recommendations.Add("Add an EORI number before using Depot for EU customs import/export/transit processes.");
		if (string.IsNullOrWhiteSpace(profile.EInvoiceEndpoint)) recommendations.Add("Add the structured e-invoice endpoint/scheme when electronic invoice routing is used.");
		if (string.IsNullOrWhiteSpace(profile.DefaultIncoterm)) recommendations.Add("Consider a default Incoterm and named place for recurring international trade flows; override it per transaction where necessary.");
		return recommendations;
	}

	private static bool RequiresGermanManagementDisclosure(CompanyProfile profile)
	{
		if (!string.Equals(profile.CountryCode, "DE", StringComparison.OrdinalIgnoreCase)) return false;
		var legalForm = profile.LegalForm.Trim();
		return legalForm.Contains("GmbH", StringComparison.OrdinalIgnoreCase) ||
			legalForm.Contains("UG", StringComparison.OrdinalIgnoreCase) ||
			legalForm.Equals("AG", StringComparison.OrdinalIgnoreCase);
	}

	private static void ValidateCountryCode(string value, string field, bool required, ICollection<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			if (required) errors.Add($"{field} is required.");
			return;
		}
		if (!CountryCodeRegex().IsMatch(value.Trim().ToUpperInvariant())) errors.Add($"{field} must be a two-letter ISO 3166-1 alpha-2 code.");
	}

	private static void ValidateNoSpaces(string value, string field, ICollection<string> errors)
	{
		if (!string.IsNullOrWhiteSpace(value) && value.Any(char.IsWhiteSpace)) errors.Add($"{field} must not contain spaces.");
	}

	private static void ValidateAdditionalRegistrations(string value, string label, ICollection<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value)) return;
		var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (var line in lines)
		{
			var parts = line.Split('|', StringSplitOptions.TrimEntries);
			if (parts.Length != 3 || !CountryCodeRegex().IsMatch(parts[0].ToUpperInvariant()) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
				errors.Add($"{label} '{line}' must use the format 'CC | TYPE/SCHEME | IDENTIFIER'.");
		}
	}

	private static bool IsValidIban(string value)
	{
		var iban = NormalizeIdentifier(value);
		if (iban.Length is < 15 or > 34 || !iban.All(char.IsLetterOrDigit)) return false;
		var rearranged = iban[4..] + iban[..4];
		var remainder = 0;
		foreach (var character in rearranged)
		{
			if (char.IsDigit(character))
			{
				remainder = (remainder * 10 + (character - '0')) % 97;
			}
			else if (char.IsLetter(character))
			{
				var numeric = char.ToUpperInvariant(character) - 'A' + 10;
				remainder = (remainder * 100 + numeric) % 97;
			}
			else return false;
		}
		return remainder == 1;
	}

	private static string NormalizeIdentifier(string value) => new(value.Where(character => !char.IsWhiteSpace(character) && character != '-').Select(char.ToUpperInvariant).ToArray());

	private static void EnsureValid(CompanyProfile profile)
	{
		var errors = Validate(profile);
		if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
	}

	private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
	{
		var sql = _provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS CompanyProfile (Id INTEGER NOT NULL PRIMARY KEY CHECK(Id = 1), Payload TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1);",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS CompanyProfile (Id int NOT NULL PRIMARY KEY, Payload longtext NOT NULL, UpdatedUtc varchar(40) NOT NULL, Version bigint NOT NULL DEFAULT 1) ENGINE=InnoDB;",
			DatabaseProvider.SqlServer => "IF OBJECT_ID('dbo.CompanyProfile', 'U') IS NULL CREATE TABLE dbo.CompanyProfile (Id int NOT NULL CONSTRAINT PK_CompanyProfile PRIMARY KEY, Payload nvarchar(max) NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_CompanyProfile_Version DEFAULT 1);",
			_ => throw new NotSupportedException($"Unsupported database provider '{_provider}'.")
		};
		await _dataAccess.ExecuteAsync(sql, cancellationToken);
	}

	private static void Require(string value, string field, ICollection<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field} is required.");
	}

	private static CompanyProfile CloneWithoutVersion(CompanyProfile profile)
	{
		var clone = JsonSerializer.Deserialize<CompanyProfile>(JsonSerializer.Serialize(profile, JsonOptions), JsonOptions)!;
		clone.Version = 0;
		return clone;
	}

	[GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
	private static partial Regex CountryCodeRegex();
	[GeneratedRegex("^[A-Z]{2}[A-Z0-9]{1,15}$", RegexOptions.CultureInvariant)]
	private static partial Regex EoriRegex();
	[GeneratedRegex("^[A-Z0-9]{20}$", RegexOptions.CultureInvariant)]
	private static partial Regex LeiRegex();
	[GeneratedRegex("^[0-9]{13}$", RegexOptions.CultureInvariant)]
	private static partial Regex GlnRegex();
	[GeneratedRegex("^[0-9]{9}$", RegexOptions.CultureInvariant)]
	private static partial Regex DunsRegex();
	[GeneratedRegex("^[A-Z0-9]{8}([A-Z0-9]{3})?$", RegexOptions.CultureInvariant)]
	private static partial Regex BicRegex();
	[GeneratedRegex("^[A-Z]{2}[A-Z0-9]{5,33}$", RegexOptions.CultureInvariant)]
	private static partial Regex SepaCreditorRegex();
	[GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
	private static partial Regex CurrencyRegex();
}
