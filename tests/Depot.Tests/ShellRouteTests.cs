// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class ShellRouteTests
{
	[Fact]
	public void RoutesNormalizeDisplayNames()
	{
		Assert.Equal("sales-orders", ShellRoute.FromName("Sales Orders").Value);
		Assert.Equal(ShellRoutes.Sales.Orders, new ShellRoute("SALES.ORDERS"));
	}

	[Fact]
	public void SecondaryNavigationUsesHelpTopicAsStableRoute()
	{
		using var item = new SecondaryNavigationItem(
			"Sales Orders",
			() => new TestViewModel(),
			(_, _) => Task.CompletedTask,
			"sales.orders");

		Assert.Equal(ShellRoutes.Sales.Orders, item.Route);
	}

	private sealed class TestViewModel : BaseViewModel;
}
