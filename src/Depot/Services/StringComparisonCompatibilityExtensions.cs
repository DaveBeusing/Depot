// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

internal static class StringComparisonCompatibilityExtensions
{
	public static bool StartsWith(this string value, char prefix, StringComparison comparison) =>
		value.StartsWith(prefix.ToString(), comparison);
}
