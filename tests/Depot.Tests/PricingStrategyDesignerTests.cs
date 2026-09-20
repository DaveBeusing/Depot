// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class PricingStrategyDesignerTests
{
	[Fact]
	public void DesignerUsesExistingPricingBoundariesAndVirtualizedHierarchy()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "SalesPricingViewModel.Strategy.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "SalesPricingView.xaml"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "SalesPricingService.cs"));
		var repository = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "SalesPriceListRepository.cs"));

		Assert.Contains("_pricing.AssignCustomerAsync", viewModel, StringComparison.Ordinal);
		Assert.Contains("_pricing.PreviewResolutionAsync", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("SalesPriceListRepository", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("INSERT INTO", viewModel, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Strategy designer", view, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode="Recycling"", view, StringComparison.Ordinal);
		Assert.Contains("Price resolution preview", view, StringComparison.Ordinal);
		Assert.Contains("Bulk pricing impact", view, StringComparison.Ordinal);
		Assert.Contains("CurrentPrice", view, StringComparison.Ordinal);
		Assert.Contains("CalculatedNewPrice", view, StringComparison.Ordinal);
		Assert.Contains("ApplyDesignerAssignmentCommand", view, StringComparison.Ordinal);
		Assert.Contains("PreviewResolutionAsync", service, StringComparison.Ordinal);
		Assert.Contains("ResolveCandidatesAsync(customerId, itemId, date, currency, token)", repository, StringComparison.Ordinal);
		Assert.Contains("return candidates.FirstOrDefault();", repository, StringComparison.Ordinal);
	}

	[Fact]
	public void StrategyDesignerDoesNotIntroducePersistedLayoutOrAlternativeFallbackSemantics()
	{
		var root = FindRepositoryRoot();
		var model = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "PricingStrategy.cs"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "SalesPricingViewModel.Strategy.cs"));

		Assert.Contains("PricingStrategyProjector", model, StringComparison.Ordinal);
		Assert.Contains("SalesPriceListScope.Global", model, StringComparison.Ordinal);
		Assert.Contains("SalesPriceListScope.Region", model, StringComparison.Ordinal);
		Assert.Contains("SalesPriceListScope.Customer", model, StringComparison.Ordinal);
		Assert.DoesNotContain("CanvasLeft", model, StringComparison.Ordinal);
		Assert.DoesNotContain("FallbackEdge", model, StringComparison.Ordinal);
		Assert.DoesNotContain("SaveStrategy", viewModel, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
