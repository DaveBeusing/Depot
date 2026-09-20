// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class SearchIdentifierCoverageTests
{
	[Fact]
	public void IdentifierHeavyRepositoriesUseExactPrefixFirstPlan()
	{
		var root = FindRepositoryRoot();
		foreach (var path in new[]
		{
			"ItemRepository.cs",
			"CustomerRepository.cs",
			"SupplierRepository.cs",
			"PurchaseOrderRepository.cs",
			"SalesOrderRepository.cs",
			"SalesInvoiceRepository.cs",
			"ShipmentRepository.cs",
			"SalesQuoteRepository.cs"
		})
		{
			var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", path));
			Assert.Contains("SearchQueryPlan.Create", source, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void DedicatedGlobalSearchReadsStayBoundedAndCountFree()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "GlobalSearchReadRepository.cs"));

		Assert.Contains("QuerySliceAsync", source, StringComparison.Ordinal);
		Assert.DoesNotContain("QueryPageAsync", source, StringComparison.Ordinal);
		Assert.DoesNotContain("SELECT COUNT(*)", source, StringComparison.Ordinal);
		Assert.Contains("SearchFinanceJournalsAsync", source, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
