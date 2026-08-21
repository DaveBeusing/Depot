// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class NavigationHistoryTests
{
	[Fact]
	public void BackAndForwardPreserveRouteOrder()
	{
		var history = new NavigationHistoryService();
		history.Record(ShellRoutes.Dashboard);
		history.Record(ShellRoutes.Sales.Orders);
		history.Record(ShellRoutes.Warehouse.Shipping);

		Assert.Equal(ShellRoutes.Sales.Orders, history.GoBack());
		Assert.Equal(ShellRoutes.Dashboard, history.GoBack());
		Assert.Equal(ShellRoutes.Sales.Orders, history.GoForward());
		Assert.True(history.CanGoForward);
	}

	[Fact]
	public void RecordingNewRouteClearsForwardHistory()
	{
		var history = new NavigationHistoryService();
		history.Record(ShellRoutes.Dashboard);
		history.Record(ShellRoutes.Sales.Orders);
		Assert.NotNull(history.GoBack());
		history.Record(ShellRoutes.Inventory.Items);

		Assert.False(history.CanGoForward);
	}
}
