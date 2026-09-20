// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Repositories;

internal readonly record struct SearchQueryPlan(string Query)
{
	public const int MinimumContainsLength = 3;

	public bool AllowContains => Query.Length >= MinimumContainsLength;
	public string Exact => Query;
	public string Prefix => $"{Query}%";
	public string WordPrefix => $"% {Query}%";
	public string Contains => $"%{Query}%";

	public static SearchQueryPlan? Create(string? value)
	{
		var normalized = value?.Trim();
		return string.IsNullOrWhiteSpace(normalized) ? null : new SearchQueryPlan(normalized);
	}

	public string BuildPredicate(IReadOnlyList<string> prefixColumns, IReadOnlyList<string> containsColumns)
	{
		if (prefixColumns.Count == 0) throw new ArgumentException("At least one prefix-search column is required.", nameof(prefixColumns));
		var predicates = prefixColumns.Select(column => $"({column} = $SearchExact OR {column} LIKE $SearchPrefix)").ToList();
		if (AllowContains) predicates.AddRange(containsColumns.Select(column => $"{column} LIKE $SearchContains"));
		return $"({string.Join(" OR ", predicates)})";
	}

	public string BuildRankExpression(IReadOnlyList<string> exactPrefixColumns, IReadOnlyList<string> wordPrefixColumns)
	{
		var exact = string.Join(" OR ", exactPrefixColumns.Select(column => $"{column} = $SearchExact"));
		var prefix = string.Join(" OR ", exactPrefixColumns.Select(column => $"{column} LIKE $SearchPrefix"));
		var rank = $"CASE WHEN {exact} THEN 0 WHEN {prefix} THEN 10";
		if (AllowContains && wordPrefixColumns.Count > 0)
			rank += $" WHEN {string.Join(" OR ", wordPrefixColumns.Select(column => $"{column} LIKE $SearchWordPrefix"))} THEN 20";
		return rank + " ELSE 30 END";
	}
}
