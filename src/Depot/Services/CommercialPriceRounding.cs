// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

internal static class CommercialPriceRounding
{
	public static decimal Round(decimal value,string currency,CommercialRoundingStrategy strategy)
	{
		if(value<0m)throw new ArgumentOutOfRangeException(nameof(value));
		return strategy switch
		{
			CommercialRoundingStrategy.CurrencyPrecision=>CurrencyRounding.Round(value,currency),
			CommercialRoundingStrategy.Nearest0_01=>RoundIncrement(value,0.01m,currency),
			CommercialRoundingStrategy.Nearest0_05=>RoundIncrement(value,0.05m,currency),
			CommercialRoundingStrategy.Nearest0_10=>RoundIncrement(value,0.10m,currency),
			CommercialRoundingStrategy.Nearest0_50=>RoundIncrement(value,0.50m,currency),
			CommercialRoundingStrategy.Ending99=>RoundEnding99(value,currency),
			_=>throw new ArgumentOutOfRangeException(nameof(strategy))
		};
	}

	private static decimal RoundIncrement(decimal value,decimal increment,string currency)
	{
		var rounded=decimal.Round(value/increment,0,MidpointRounding.AwayFromZero)*increment;
		return CurrencyRounding.Round(rounded,currency);
	}

	private static decimal RoundEnding99(decimal value,string currency)
	{
		if(CurrencyRounding.Precision(currency)!=2)throw new InvalidOperationException("The .99 ending strategy requires a two-decimal currency.");
		if(value==0m)return 0m;
		var rounded=Math.Ceiling(value+0.01m)-0.01m;
		return CurrencyRounding.Round(rounded,currency);
	}
}
