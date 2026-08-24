// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record DocumentBuyerProfile(
	long CustomerId,
	string CustomerNumber,
	string Name,
	string BuyerReference,
	string ElectronicAddress,
	string ElectronicAddressScheme,
	string TaxIdentifier,
	string VatIdentifier,
	string BillingAddress,
	string Street,
	string AddressLine2,
	string PostalCode,
	string City,
	string CountryCode,
	string ContactName,
	string ContactEmail,
	string ContactPhone);

public sealed record SalesInvoiceFinalization(
	long SalesInvoiceId,
	DocumentBuyerProfile Buyer,
	string XRechnungXml,
	string XRechnungSha256,
	DateTime FinalizedAtUtc);
