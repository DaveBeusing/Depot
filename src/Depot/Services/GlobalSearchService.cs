// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public interface IGlobalSearchProvider
{
	string Id { get; }
	bool CanSearch { get; }
	Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);
}

public sealed class GlobalSearchService
{
	public const int MinimumQueryLength = 2;
	public const int MaximumResults = 50;

	private readonly IReadOnlyList<IGlobalSearchProvider> _providers;

	public GlobalSearchService(IEnumerable<IGlobalSearchProvider> providers)
	{
		ArgumentNullException.ThrowIfNull(providers);
		_providers = providers.Where(provider => provider is not null).ToArray();
		if (_providers.Select(provider => provider.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _providers.Count)
			throw new ArgumentException("Global-search provider identifiers must be unique.", nameof(providers));
	}

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
		string? query,
		int maxResults = 30,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var normalized = query?.Trim() ?? string.Empty;
		if (normalized.Length < MinimumQueryLength || _providers.Count == 0) return [];

		var eligibleProviders = _providers.Where(provider => provider.CanSearch).ToArray();
		if (eligibleProviders.Length == 0) return [];
		var boundedMaximum = Math.Clamp(maxResults, 1, MaximumResults);
		var perProvider = Math.Clamp(((boundedMaximum + eligibleProviders.Length - 1) / eligibleProviders.Length) + 2, 4, 12);
		var tasks = eligibleProviders.Select(provider => provider.SearchAsync(normalized, perProvider, cancellationToken)).ToArray();
		var providerResults = await Task.WhenAll(tasks);
		cancellationToken.ThrowIfCancellationRequested();

		return providerResults
			.SelectMany(results => results)
			.Where(IsValid)
			.GroupBy(result => result.StableId, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.OrderBy(result => result.Rank).ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase).First())
			.OrderBy(result => result.Rank)
			.ThenBy(result => (int)result.Kind)
			.ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
			.Take(boundedMaximum)
			.ToArray();
	}

	private static bool IsValid(GlobalSearchResult result) =>
		!string.IsNullOrWhiteSpace(result.StableId) &&
		result.EntityId > 0 &&
		!string.IsNullOrWhiteSpace(result.Title) &&
		!string.IsNullOrWhiteSpace(result.Group) &&
		!string.IsNullOrWhiteSpace(result.TypeLabel);
}
