// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

internal static class FinanceGeneralLedgerRepositoryExtensions
{
	internal static async Task<ExchangeRate?> FindLatestExchangeRateAsync(
		this FinanceGeneralLedgerRepository repository,
		DatabaseTransactionContext transaction,
		CurrencyCode baseCurrency,
		CurrencyCode quoteCurrency,
		DateOnly postingDate,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		var rates = await transaction.Session.QueryAsync(
			"SELECT Id, BaseCurrencyCode, QuoteCurrencyCode, Rate, EffectiveAtUtc, SourceCode FROM FinanceExchangeRates WHERE BaseCurrencyCode=$Base AND QuoteCurrencyCode=$Quote ORDER BY EffectiveAtUtc DESC, Id DESC;",
			reader => new ExchangeRate(
				Guid.Parse(reader.GetString(0)),
				new CurrencyCode(reader.GetString(1)),
				new CurrencyCode(reader.GetString(2)),
				Convert.ToDecimal(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
				ReadDateTimeOffset(reader.GetValue(4)),
				reader.GetString(5)),
			cancellationToken,
			new DatabaseParameter("$Base", baseCurrency.Value),
			new DatabaseParameter("$Quote", quoteCurrency.Value));
		return rates.FirstOrDefault(rate => DateOnly.FromDateTime(rate.EffectiveAtUtc.UtcDateTime) <= postingDate);
	}

	private static DateTimeOffset ReadDateTimeOffset(object value)
	{
		if (value is DateTimeOffset offset) return offset.ToUniversalTime();
		if (value is DateTime dateTime) return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
		return DateTimeOffset.Parse(
			Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
			System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
	}
}
