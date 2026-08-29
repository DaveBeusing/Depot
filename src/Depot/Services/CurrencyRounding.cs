// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

internal static class CurrencyRounding
{
	private static readonly HashSet<string> ZeroDecimals=new(StringComparer.OrdinalIgnoreCase){"BIF","CLP","DJF","GNF","ISK","JPY","KMF","KRW","PYG","RWF","UGX","VND","VUV","XAF","XOF","XPF"};
	private static readonly HashSet<string> ThreeDecimals=new(StringComparer.OrdinalIgnoreCase){"BHD","IQD","JOD","KWD","LYD","OMR","TND"};
	public static decimal Round(decimal value,string currency)=>decimal.Round(value,Precision(currency),MidpointRounding.ToEven);
	public static int Precision(string currency)=>ZeroDecimals.Contains(currency)?0:ThreeDecimals.Contains(currency)?3:2;
}
