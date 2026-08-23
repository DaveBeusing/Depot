// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class CompanyDocumentIdentityTests
{
	[Fact]
	public void ProjectionUsesPublishableCompanyData()
	{
		var profile = CreateProfile();
		var issuer = CompanyDocumentIdentityService.Project(profile);

		Assert.Equal("Depot Trading", issuer.DisplayName);
		Assert.Equal("Depot GmbH", issuer.LegalName);
		Assert.Contains("HRB 12345", issuer.RegistrationLine, StringComparison.Ordinal);
		Assert.Contains("DE123456789", issuer.TaxLine, StringComparison.Ordinal);
		Assert.Equal("DE", issuer.CountryCode);
	}

	[Fact]
	public void ProjectionDoesNotExposeSensitiveTradeIdentifiers()
	{
		var profile = CreateProfile();
		profile.IossIdentificationNumber = "IM2760000001";
		profile.CustomsAccountReference = "CUSTOMS-SECRET";
		profile.EoriNumber = "DE123456789012345";

		var issuer = CompanyDocumentIdentityService.Project(profile);
		var serialized = System.Text.Json.JsonSerializer.Serialize(issuer);

		Assert.DoesNotContain(profile.IossIdentificationNumber, serialized, StringComparison.Ordinal);
		Assert.DoesNotContain(profile.CustomsAccountReference, serialized, StringComparison.Ordinal);
		Assert.DoesNotContain(profile.EoriNumber, serialized, StringComparison.Ordinal);
	}

	[Fact]
	public void ElectronicInvoiceSellerUsesIssuerIdentity()
	{
		var issuer = CompanyDocumentIdentityService.Project(CreateProfile());
		var seller = CompanyDocumentIdentityService.ToElectronicInvoiceSeller(issuer);

		Assert.Equal(issuer.LegalName, seller.Name);
		Assert.Equal(issuer.Street, seller.AddressLine1);
		Assert.Equal(issuer.City, seller.City);
		Assert.Equal("DE123456789", seller.VatIdentifier);
		Assert.Equal(issuer.InvoiceEmail, seller.ContactEmail);
	}

	[Fact]
	public void IncompleteCompanyProfileCannotBecomeDocumentIssuer()
	{
		var profile = CreateProfile();
		profile.LegalName = string.Empty;

		var exception = Assert.Throws<ArgumentException>(() => CompanyDocumentIdentityService.Project(profile));
		Assert.Contains("Legal company name", exception.Message, StringComparison.Ordinal);
	}

	private static CompanyProfile CreateProfile() => new()
	{
		LegalName = "Depot GmbH",
		LegalForm = "GmbH",
		TradingName = "Depot Trading",
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
		ManagingDirectors = "Max Example",
		VatId = "DE123456789",
		Email = "info@example.invalid",
		Phone = "+49 228 123456",
		InvoiceEmail = "invoice@example.invalid",
		EInvoiceEndpoint = "invoice@example.invalid",
		EInvoiceEndpointScheme = "EM",
		Iban = "DE89370400440532013000",
		Bic = "COBADEFFXXX"
	};
}
