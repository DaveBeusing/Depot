// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class CompanyProfile
{
	public long Version { get; set; }

	// Legal identity and registered establishment.
	public string LegalName { get; set; } = string.Empty;
	public string LegalForm { get; set; } = string.Empty;
	public string TradingName { get; set; } = string.Empty;
	public string Street { get; set; } = string.Empty;
	public string AddressLine2 { get; set; } = string.Empty;
	public string PostalCode { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public string CountryCode { get; set; } = "DE";
	public string TaxResidenceCountryCode { get; set; } = "DE";
	public string RegisteredOffice { get; set; } = string.Empty;
	public bool IsRegisteredEntity { get; set; } = true;
	public string RegisterCourt { get; set; } = string.Empty;
	public string RegisterType { get; set; } = "HRB";
	public string RegisterNumber { get; set; } = string.Empty;
	public string LegalEntityIdentifier { get; set; } = string.Empty;
	public string Gln { get; set; } = string.Empty;
	public string DunsNumber { get; set; } = string.Empty;

	// Corporate representation and branch disclosure.
	public string ManagingDirectors { get; set; } = string.Empty;
	public bool HasSupervisoryBoard { get; set; }
	public string SupervisoryBoardChair { get; set; } = string.Empty;
	public bool PublishesShareCapital { get; set; }
	public string ShareCapital { get; set; } = string.Empty;
	public string OutstandingCapital { get; set; } = string.Empty;
	public bool IsInLiquidation { get; set; }
	public string Liquidators { get; set; } = string.Empty;
	public bool IsBranch { get; set; }
	public string BranchName { get; set; } = string.Empty;
	public string BranchRegistrationAuthority { get; set; } = string.Empty;
	public string BranchRegistrationNumber { get; set; } = string.Empty;

	// Tax registrations. Additional registrations are one per line, e.g. "FR | VAT | FR123...".
	public string TaxNumber { get; set; } = string.Empty;
	public string VatId { get; set; } = string.Empty;
	public string BusinessId { get; set; } = string.Empty;
	public string AdditionalTaxRegistrations { get; set; } = string.Empty;
	public string OssRegistration { get; set; } = string.Empty;
	public string IossIdentificationNumber { get; set; } = string.Empty;
	public bool HasFiscalRepresentative { get; set; }
	public string FiscalRepresentativeName { get; set; } = string.Empty;
	public string FiscalRepresentativeVatId { get; set; } = string.Empty;
	public string FiscalRepresentativeAddress { get; set; } = string.Empty;

	// Customs and international trade identifiers.
	public string EoriNumber { get; set; } = string.Empty;
	public string RexNumber { get; set; } = string.Empty;
	public string AeoAuthorizationNumber { get; set; } = string.Empty;
	public string CustomsAccountReference { get; set; } = string.Empty;
	public string DefaultIncoterm { get; set; } = string.Empty;
	public string DefaultIncotermPlace { get; set; } = string.Empty;
	public string ExporterStatement { get; set; } = string.Empty;

	// Extended producer responsibility and product-market registrations.
	public string PackagingRegistrationNumber { get; set; } = string.Empty;
	public string WeeeRegistrationNumber { get; set; } = string.Empty;
	public string BatteryRegistrationNumber { get; set; } = string.Empty;
	public string AdditionalRegulatoryRegistrations { get; set; } = string.Empty;

	// Regulatory/professional disclosures used where the business is regulated.
	public string RegulatoryAuthority { get; set; } = string.Empty;
	public string ProfessionalTitle { get; set; } = string.Empty;
	public string ProfessionalTitleCountryCode { get; set; } = string.Empty;
	public string ProfessionalRulesReference { get; set; } = string.Empty;

	// Contact and payment data.
	public string Phone { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string Website { get; set; } = string.Empty;
	public string InvoiceEmail { get; set; } = string.Empty;
	public string AccountHolder { get; set; } = string.Empty;
	public string BankName { get; set; } = string.Empty;
	public string Iban { get; set; } = string.Empty;
	public string Bic { get; set; } = string.Empty;
	public string SepaCreditorIdentifier { get; set; } = string.Empty;

	// Business-document and structured-invoice defaults.
	public string DefaultCurrency { get; set; } = "EUR";
	public string DefaultLanguage { get; set; } = "de-DE";
	public int PaymentTermsDays { get; set; } = 14;
	public string EInvoiceEndpoint { get; set; } = string.Empty;
	public string EInvoiceEndpointScheme { get; set; } = string.Empty;
	public string LeitwegId { get; set; } = string.Empty;
	public string LegalFooterAdditionalText { get; set; } = string.Empty;
}
