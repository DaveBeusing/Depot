// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public static class ShellRouteNavigator
{
	public static ShellNavigationItem? FindWorkspace(this MainViewModel viewModel, ShellRoute route) =>
		viewModel.NavigationItems.FirstOrDefault(item => item.Route == route || FindPage(item, route) is not null);

	public static SecondaryNavigationItem? FindPage(this MainViewModel viewModel, ShellRoute route)
	{
		foreach (var item in viewModel.NavigationItems)
		{
			if (FindPage(item, route) is { } page) return page;
		}
		return null;
	}

	public static async Task NavigateToRouteAsync(this MainViewModel viewModel, ShellRoute route, CancellationToken cancellationToken = default)
	{
		var workspace = viewModel.NavigationItems.FirstOrDefault(item => item.Route == route);
		if (workspace is not null)
		{
			await viewModel.NavigateAsync(workspace, cancellationToken);
			return;
		}

		foreach (var item in viewModel.NavigationItems)
		{
			if (FindPage(item, route) is not { } page) continue;
			if (item.Content is not ShellModuleViewModel module) throw new InvalidOperationException("The requested route is not a module page.");
			if (!module.SetSelectedPage(page)) return;
			await viewModel.NavigateAsync(item, cancellationToken);
			return;
		}

		throw new UnauthorizedAccessException($"The requested route '{route}' is not available.");
	}

	private static SecondaryNavigationItem? FindPage(ShellNavigationItem item, ShellRoute route)
	{
		if (item.Content is not ShellModuleViewModel module) return null;
		return module.Pages.FirstOrDefault(page => page.Route == route && page.IsVisible);
	}
}
