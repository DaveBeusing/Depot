// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class GlobalSearchServiceTests
{
	[Fact]
	public async Task ShortQueryDoesNotInvokeProviders()
	{
		var calls = 0;
		var service = new GlobalSearchService([
			new StubProvider("test", (_, _, _) =>
			{
				calls++;
				return Task.FromResult<IReadOnlyList<GlobalSearchResult>>([]);
			})
		]);

		var results = await service.SearchAsync("x");

		Assert.Empty(results);
		Assert.Equal(0, calls);
	}

	[Fact]
	public async Task ResultsAreDeduplicatedRankedAndBounded()
	{
		var service = new GlobalSearchService([
			new StubProvider("first", (_, _, _) => Task.FromResult<IReadOnlyList<GlobalSearchResult>>([
				Result("item:2", GlobalSearchResultKind.Item, 2, "B", 20),
				Result("item:1", GlobalSearchResultKind.Item, 1, "A slow", 30),
				Result("item:3", GlobalSearchResultKind.Item, 3, "C", 40)
			])),
			new StubProvider("second", (_, _, _) => Task.FromResult<IReadOnlyList<GlobalSearchResult>>([
				Result("item:1", GlobalSearchResultKind.Item, 1, "A exact", 0),
				Result("customer:4", GlobalSearchResultKind.Customer, 4, "D", 10)
			]))
		]);

		var results = await service.SearchAsync("ab", 3);

		Assert.Equal(3, results.Count);
		Assert.Equal("item:1", results[0].StableId);
		Assert.Equal("A exact", results[0].Title);
		Assert.Equal("customer:4", results[1].StableId);
		Assert.Equal("item:2", results[2].StableId);
	}

	[Fact]
	public async Task CancellationStopsRunningProviderSearches()
	{
		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var service = new GlobalSearchService([
			new StubProvider("slow", async (_, _, token) =>
			{
				started.TrySetResult(true);
				await Task.Delay(Timeout.InfiniteTimeSpan, token);
				return [];
			})
		]);
		using var cancellation = new CancellationTokenSource();

		var search = service.SearchAsync("cancel", cancellationToken: cancellation.Token);
		await started.Task;
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await search);
	}

	[Fact]
	public void DuplicateProviderIdentifiersAreRejected()
	{
		var providers = new IGlobalSearchProvider[]
		{
			new StubProvider("duplicate", (_, _, _) => Task.FromResult<IReadOnlyList<GlobalSearchResult>>([])),
			new StubProvider("DUPLICATE", (_, _, _) => Task.FromResult<IReadOnlyList<GlobalSearchResult>>([]))
		};

		Assert.Throws<ArgumentException>(() => new GlobalSearchService(providers));
	}

	[Theory]
	[InlineData("SO-100", "SO-100", 0)]
	[InlineData("SO", "SO-100", 10)]
	[InlineData("100", "Order SO 100", 20)]
	[InlineData("100", "Order SO-100", 30)]
	public void RankingPrioritizesExactAndPrefixMatches(string query, string value, int expected)
	{
		Assert.Equal(expected, GlobalSearchRanking.Calculate(query, value));
	}

	private static GlobalSearchResult Result(string id, GlobalSearchResultKind kind, long entityId, string title, int rank) =>
		new(id, kind, entityId, title, "Subtitle", "Group", "TYPE", rank);

	private sealed class StubProvider : IGlobalSearchProvider
	{
		private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<GlobalSearchResult>>> _search;

		public StubProvider(string id, Func<string, int, CancellationToken, Task<IReadOnlyList<GlobalSearchResult>>> search)
		{
			Id = id;
			_search = search;
		}

		public string Id { get; }
		public bool CanSearch => true;

		public Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
			_search(query, maxResults, cancellationToken);
	}
}
