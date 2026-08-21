// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed record ShellFeaturePage(
	ShellRoute Route,
	string Name,
	string HelpTopicId,
	SecondaryNavigationItem NavigationItem);

public sealed class ShellFeatureModule
{
	public ShellFeatureModule(ShellNavigationItem navigationItem, IReadOnlyList<ShellFeaturePage> pages)
	{
		NavigationItem = navigationItem;
		Route = navigationItem.Route;
		Name = navigationItem.Name;
		IconData = navigationItem.IconData;
		Pages = pages;
	}

	public ShellRoute Route { get; }
	public string Name { get; }
	public string IconData { get; }
	public ShellNavigationItem NavigationItem { get; }
	public IReadOnlyList<ShellFeaturePage> Pages { get; }
	public bool IsModule => Pages.Count > 0;
}

public sealed class ShellFeatureCatalog
{
	private readonly IReadOnlyList<ShellFeatureModule> _modules;

	private ShellFeatureCatalog(IReadOnlyList<ShellFeatureModule> modules) => _modules = modules;

	public IReadOnlyList<ShellFeatureModule> Modules => _modules;

	public static ShellFeatureCatalog Create(MainViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(viewModel);
		var modules = viewModel.NavigationItems.Select(CreateModule).ToArray();
		return new ShellFeatureCatalog(modules);
	}

	public ShellFeatureModule? FindModule(ShellRoute route) =>
		_modules.FirstOrDefault(module => module.Route == route || module.Pages.Any(page => page.Route == route));

	public ShellFeaturePage? FindPage(ShellRoute route) =>
		_modules.SelectMany(module => module.Pages).FirstOrDefault(page => page.Route == route);

	private static ShellFeatureModule CreateModule(ShellNavigationItem navigationItem)
	{
		var pages = navigationItem.Content is ShellModuleViewModel module
			? module.Pages.Where(page => page.IsVisible)
				.Select(page => new ShellFeaturePage(page.Route, page.Name, page.HelpTopicId, page))
				.ToArray()
			: [];
		return new ShellFeatureModule(navigationItem, pages);
	}
}
