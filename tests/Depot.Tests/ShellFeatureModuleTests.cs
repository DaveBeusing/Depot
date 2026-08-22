// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class ShellFeatureModuleTests
{
	[Fact]
	public void ModuleExposesStableRouteAndVisiblePages()
	{
		using var page = new SecondaryNavigationItem("Orders", () => new TestViewModel(), (_, _) => Task.CompletedTask, "sales.orders");
		using var moduleViewModel = new ShellModuleViewModel("Sales", "Sales", [page]);
		using var navigation = new ShellNavigationItem("Sales", string.Empty, () => moduleViewModel, (_, _) => Task.CompletedTask, "sales.overview", route: ShellRoutes.Sales.Module);
		var module = new ShellFeatureModule(navigation, [new ShellFeaturePage(page.Route, page.Name, page.HelpTopicId, page)]);

		Assert.Equal(ShellRoutes.Sales.Module, module.Route);
		Assert.Equal(ShellRoutes.Sales.Orders, Assert.Single(module.Pages).Route);
	}

	private sealed class TestViewModel : BaseViewModel;
}
