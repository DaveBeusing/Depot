// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public interface IExchangeRateSource
{
	string SourceCode { get; }
	ValueTask<ExchangeRate?> GetRateAsync(
		CurrencyCode baseCurrency,
		CurrencyCode quoteCurrency,
		DateTimeOffset effectiveAtUtc,
		CancellationToken cancellationToken = default);
}

public sealed record TaxDeterminationRequest(
	Guid LegalEntityId,
	string SellerCountryCode,
	string BuyerCountryCode,
	DateOnly TransactionDate,
	string? ProductTaxCode,
	string? CustomerTaxCode,
	decimal TaxableAmount,
	CurrencyCode Currency);

public sealed record TaxDeterminationResult(
	string TaxCode,
	decimal Rate,
	string ProviderCode,
	string? ExemptionReasonCode = null,
	string? ExemptionReason = null);

public interface ITaxDeterminationService
{
	ValueTask<TaxDeterminationResult> DetermineAsync(
		TaxDeterminationRequest request,
		CancellationToken cancellationToken = default);
}

public interface IFinanceLocalizationProvider
{
	string ProviderCode { get; }
	bool SupportsCountry(string countryCode);
	ValueTask<IReadOnlyList<string>> GetRequiredTaxRegistrationSchemesAsync(
		string countryCode,
		CancellationToken cancellationToken = default);
}
