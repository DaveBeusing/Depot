// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class CompanyProfileTests
{
	[Fact]
	public void CompleteGermanCompanyProfilePassesValidation()
	{
		var profile = CreateCompleteProfile();
		var errors = CompanyProfileService.Validate(profile);
		Assert.Empty(errors);
	}

	[Fact]
	public void TaxNumberVatOrAdditionalRegistrationIsRequired()
	{
		var profile = CreateCompleteProfile();
		profile.TaxNumber = string.Empty;
		profile.VatId = string.Empty;
		profile.AdditionalTaxRegistrations = string.Empty;

		var errors = CompanyProfileService.Validate(profile);

		Assert.Contains(errors, error => error.Contains("At least one tax registration", StringComparison.Ordinal));
	}

	[Fact]
	public void AdditionalForeignTaxRegistrationCanSatisfyTaxIdentity()
	{
		var profile = CreateCompleteProfile();
		profile.TaxNumber = string.Empty;
		profile.VatId = string.Empty;
		profile.AdditionalTaxRegistrations = "FR | VAT | FR12345678901";

		Assert.Empty(CompanyProfileService.Validate(profile));
	}

	[Fact]
	public void LiquidationRequiresLiquidator()
	{
		var profile = CreateCompleteProfile();
		profile.IsInLiquidation = true;
		profile.Liquidators = string.Empty;

		var errors = CompanyProfileService.Validate(profile);

		Assert.Contains(errors, error => error.Contains("Liquidator", StringComparison.Ordinal));
	}

	[Fact]
	public void GermanRegisteredCompanyRequiresRegistrationAndManagementDisclosure()
	{
		var profile = CreateCompleteProfile();
		profile.RegisterCourt = string.Empty;
		profile.RegisterNumber = string.Empty;
		profile.ManagingDirectors = string.Empty;

		var errors = CompanyProfileService.Validate(profile);

		Assert.Contains(errors, error => error.Contains("Registration authority", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Company registration number", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Legal representatives", StringComparison.Ordinal));
	}

	[Fact]
	public void UnregisteredForeignSoleTraderDoesNotRequireGermanCorporateFields()
	{
		var profile = CreateCompleteProfile();
		profile.CountryCode = "US";
		profile.TaxResidenceCountryCode = "US";
		profile.LegalForm = "Sole proprietorship";
		profile.IsRegisteredEntity = false;
		profile.RegisteredOffice = string.Empty;
		profile.RegisterCourt = string.Empty;
		profile.RegisterNumber = string.Empty;
		profile.ManagingDirectors = string.Empty;
		profile.PostalCode = string.Empty;

		var errors = CompanyProfileService.Validate(profile);

		Assert.DoesNotContain(errors, error => error.Contains("Registration authority", StringComparison.Ordinal));
		Assert.DoesNotContain(errors, error => error.Contains("Legal representatives", StringComparison.Ordinal));
	}

	[Fact]
	public void BranchRequiresBranchRegistrationDisclosure()
	{
		var profile = CreateCompleteProfile();
		profile.IsBranch = true;

		var errors = CompanyProfileService.Validate(profile);

		Assert.Contains(errors, error => error.Contains("Branch name", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Branch registration authority", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Branch registration number", StringComparison.Ordinal));
	}

	[Fact]
	public void FiscalRepresentativeRequiresIdentityVatAndAddress()
	{
		var profile = CreateCompleteProfile();
		profile.HasFiscalRepresentative = true;

		var errors = CompanyProfileService.Validate(profile);

		Assert.Contains(errors, error => error.Contains("Fiscal representative name", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Fiscal representative VAT", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Fiscal representative address", StringComparison.Ordinal));
	}

	[Fact]
	public void InternationalIdentifiersAndPaymentDataAreValidated()
	{
		var profile = CreateCompleteProfile();
		profile.EoriNumber = "1BAD";
		profile.LegalEntityIdentifier = "123";
		profile.Gln = "12";
		profile.DunsNumber = "123";
		profile.Iban = "DE001234";
		profile.Bic = "BAD";
		profile.DefaultIncoterm = "XYZ";

		var errors = CompanyProfileService.Validate(profile);

		Assert.Contains(errors, error => error.Contains("EORI", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("LEI", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("GLN", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("D-U-N-S", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("IBAN", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("BIC", StringComparison.Ordinal));
		Assert.Contains(errors, error => error.Contains("Incoterm", StringComparison.Ordinal));
	}

	[Fact]
	public void ValidInternationalTradeIdentifiersPassValidation()
	{
		var profile = CreateCompleteProfile();
		profile.EoriNumber = "DE123456789012345";
		profile.LegalEntityIdentifier = "529900T8BM49AURSDO55";
		profile.Gln = "4000001000005";
		profile.DunsNumber = "123456789";
		profile.Iban = "DE89370400440532013000";
		profile.Bic = "COBADEFFXXX";
		profile.SepaCreditorIdentifier = "DE98ZZZ09999999999";
		profile.DefaultIncoterm = "DAP";
		profile.DefaultIncotermPlace = "Toronto, ON, Canada";

		Assert.Empty(CompanyProfileService.Validate(profile));
	}

	[Fact]
	public void RegistrationLinesUseCountryTypeIdentifierFormat()
	{
		var profile = CreateCompleteProfile();
		profile.AdditionalTaxRegistrations = "France VAT FR123";
		profile.AdditionalRegulatoryRegistrations = "FR | EPR";

		var errors = CompanyProfileService.Validate(profile);

		Assert.Equal(2, errors.Count(error => error.Contains("must use the format", StringComparison.Ordinal)));
	}

	private static CompanyProfile CreateCompleteProfile() => new()
	{
		LegalName = "Depot Example GmbH",
		LegalForm = "GmbH",
		Street = "Example Street 1",
		PostalCode = "53111",
		City = "Bonn",
		CountryCode = "DE",
		TaxResidenceCountryCode = "DE",
		RegisteredOffice = "Bonn",
		IsRegisteredEntity = true,
		RegisterCourt = "Amtsgericht Bonn",
		RegisterType = "HRB",
		RegisterNumber = "12345",
		VatId = "DE123456789",
		ManagingDirectors = "Max Example",
		DefaultCurrency = "EUR",
		DefaultLanguage = "de-DE",
		PaymentTermsDays = 14
	};
}
