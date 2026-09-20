// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Repositories;

using Xunit;

namespace Depot.Tests;

public sealed class SearchQueryPlanTests
{
	[Fact]
	public void TwoCharacterQueryUsesOnlyExactAndPrefixPredicates()
	{
		var plan = SearchQueryPlan.Create("SO")!.Value;
		var predicate = plan.BuildPredicate(["OrderNumber", "Name"], ["OrderNumber", "Name", "Notes"]);

		Assert.False(plan.AllowContains);
		Assert.Contains("$SearchExact", predicate, StringComparison.Ordinal);
		Assert.Contains("$SearchPrefix", predicate, StringComparison.Ordinal);
		Assert.DoesNotContain("$SearchContains", predicate, StringComparison.Ordinal);
	}

	[Fact]
	public void ThreeCharacterQueryKeepsContainsFallbackAndWordPrefixRanking()
	{
		var plan = SearchQueryPlan.Create("100")!.Value;
		var predicate = plan.BuildPredicate(["OrderNumber"], ["OrderNumber", "Name"]);
		var rank = plan.BuildRankExpression(["OrderNumber", "Name"], ["Name"]);

		Assert.True(plan.AllowContains);
		Assert.Contains("$SearchContains", predicate, StringComparison.Ordinal);
		Assert.Contains("THEN 0", rank, StringComparison.Ordinal);
		Assert.Contains("THEN 10", rank, StringComparison.Ordinal);
		Assert.Contains("$SearchWordPrefix", rank, StringComparison.Ordinal);
		Assert.Contains("THEN 20", rank, StringComparison.Ordinal);
		Assert.EndsWith("ELSE 30 END", rank, StringComparison.Ordinal);
	}

	[Fact]
	public void HighVolumeMasterDataRepositoriesUseSharedSearchPlan()
	{
		var root = FindRepositoryRoot();
		foreach (var path in new[] { "ItemRepository.cs", "CustomerRepository.cs", "SupplierRepository.cs", "PurchaseOrderRepository.cs" })
		{
			var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", path));
			Assert.Contains("SearchQueryPlan.Create", source, StringComparison.Ordinal);
		}
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
