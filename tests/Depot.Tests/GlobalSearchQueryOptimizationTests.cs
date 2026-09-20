// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class GlobalSearchQueryOptimizationTests
{
	[Fact]
	public async Task IneligibleProviderIsNotInvoked()
	{
		var calls = 0;
		var service = new GlobalSearchService([new IneligibleProvider(() => calls++)]);

		var results = await service.SearchAsync("SO");

		Assert.Empty(results);
		Assert.Equal(0, calls);
	}

	[Fact]
	public void GlobalSearchReadRepositoryUsesBoundedReadsWithoutPageCounts()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "GlobalSearchReadRepository.cs"));

		Assert.Contains("QuerySliceAsync", source, StringComparison.Ordinal);
		Assert.DoesNotContain("QueryPageAsync", source, StringComparison.Ordinal);
		Assert.Contains("SearchFinanceJournalsAsync", source, StringComparison.Ordinal);
		Assert.Contains("FinanceJournalEntries WHERE", source, StringComparison.Ordinal);
		Assert.Contains("SearchQueryPlan", source, StringComparison.Ordinal);
	}

	[Fact]
	public void PaletteKeepsDebounceCancellationAndStaleResultGuard()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ShellPaletteWindow.GlobalSearch.cs"));

		Assert.Contains("Task.Delay(TimeSpan.FromMilliseconds(120), token)", source, StringComparison.Ordinal);
		Assert.Contains("_searchCancellation?.Cancel()", source, StringComparison.Ordinal);
		Assert.Contains("token.ThrowIfCancellationRequested()", source, StringComparison.Ordinal);
		Assert.Contains("ApplyEntries(finalEntries)", source, StringComparison.Ordinal);
	}

	private sealed class IneligibleProvider(Action invoked) : IGlobalSearchProvider
	{
		public string Id => "blocked";
		public bool CanSearch => false;
		public Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
		{
			invoked();
			return Task.FromResult<IReadOnlyList<GlobalSearchResult>>([]);
		}
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
