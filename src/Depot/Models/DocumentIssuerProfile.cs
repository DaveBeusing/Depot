// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

/// <summary>
/// Publishable company identity used on business documents. Deliberately excludes
/// sensitive or process-specific registrations such as IOSS and customs accounts.
/// </summary>
public sealed record DocumentIssuerProfile(
	string LegalName,
	string DisplayName,
	string LegalForm,
	string Street,
	string AddressLine2,
	string PostalCode,
	string City,
	string CountryCode,
	string RegistrationLine,
	string TaxLine,
	string ManagingDirectors,
	string Email,
	string Phone,
	string Website,
	string InvoiceEmail,
	string AccountHolder,
	string BankName,
	string Iban,
	string Bic,
	string EInvoiceEndpoint,
	string EInvoiceEndpointScheme,
	string LegalFooterAdditionalText)
{
	public string PostalAddress => string.Join(", ", new[]
	{
		Street,
		AddressLine2,
		string.Join(" ", new[] { PostalCode, City }.Where(value => !string.IsNullOrWhiteSpace(value)))
	}.Where(value => !string.IsNullOrWhiteSpace(value)));

	public string BankLine => string.Join(" · ", new[]
	{
		string.IsNullOrWhiteSpace(BankName) ? null : $"Bank: {BankName}",
		string.IsNullOrWhiteSpace(Iban) ? null : $"IBAN: {Iban}",
		string.IsNullOrWhiteSpace(Bic) ? null : $"BIC: {Bic}"
	}.Where(value => !string.IsNullOrWhiteSpace(value)));
}
