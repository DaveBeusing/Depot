// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class ShellNavigationInformationArchitectureTests
{
	[Fact]
	public void PrimaryNavigationKeepsRouteOnlyWorkspacesOutOfTheActivityBar()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));

		Assert.Contains("PrimaryNavigationItems => NavigationItems.Where(item => item.IsPrimaryNavigationVisible)", source, StringComparison.Ordinal);
		Assert.Contains("AddModule(\"Role Centers\", Icons.Work", source, StringComparison.Ordinal);
		Assert.Contains("roleCenterPages, isPrimaryNavigationVisible: false, showContextNavigation: false", source, StringComparison.Ordinal);
		Assert.Contains("AddModule(\"Approvals\", Icons.Approvals", source, StringComparison.Ordinal);
		Assert.Contains("approvalPages, isPrimaryNavigationVisible: false", source, StringComparison.Ordinal);
		Assert.Contains("HelpService.FallbackTopicId, \"Home\")", source, StringComparison.Ordinal);
	}

	[Fact]
	public void ActivityBarExpansionIsAccessibleAndDoesNotNavigate()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml"));
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Shell.xaml"));
		var polish = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ShellPolish.xaml"));

		Assert.Contains("Title=\"Depot\"", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Depot - Inventory Management", xaml, StringComparison.Ordinal);
		Assert.Contains("Width=\"{Binding NavigationPaneWidth}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding PrimaryNavigationItems}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Command=\"{Binding ToggleNavigationCommand}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"{Binding NavigationToggleLabel}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("ShowSelectedContextNavigation", xaml, StringComparison.Ordinal);
		Assert.Contains("ShowSelectedContextNavigation => SelectedNavigationItem?.ShowContextNavigation == true", source, StringComparison.Ordinal);
		Assert.Contains("NavigationPaneWidth => IsNavigationExpanded ? 220d : 52d", source, StringComparison.Ordinal);
		Assert.Contains("private void ToggleNavigation() => IsNavigationExpanded = !IsNavigationExpanded;", source, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name", shell, StringComparison.Ordinal);
		Assert.Contains("NavigationLabel", polish, StringComparison.Ordinal);

		var toggleStart = source.IndexOf("private void ToggleNavigation()", StringComparison.Ordinal);
		Assert.True(toggleStart >= 0);
		var toggleEnd = source.IndexOf('\n', toggleStart);
		Assert.DoesNotContain("SelectedNavigationItem", source[toggleStart..toggleEnd], StringComparison.Ordinal);
	}

	[Fact]
	public void HiddenRoleCenterRoutesRemainAvailableToSearchAndWorkspaceProductivity()
	{
		var root = FindRepositoryRoot();
		var catalog = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ShellFeatureModule.cs"));
		var palette = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ShellPaletteWindow.xaml.cs"));
		var productivity = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "WorkspaceProductivityViewModel.cs"));

		Assert.Contains("viewModel.NavigationItems.Select(CreateModule)", catalog, StringComparison.Ordinal);
		Assert.DoesNotContain("viewModel.PrimaryNavigationItems.Select(CreateModule)", catalog, StringComparison.Ordinal);
		Assert.Contains("navigationItem.Pages", catalog, StringComparison.Ordinal);
		Assert.DoesNotContain("navigationItem.Content is ShellModuleViewModel", catalog, StringComparison.Ordinal);
		Assert.Contains("ShellFeatureCatalog.Create(_viewModel).Modules", palette, StringComparison.Ordinal);
		Assert.Contains("ShellFeatureCatalog.Create(main).Modules", productivity, StringComparison.Ordinal);
		Assert.Contains("\"role-centers.sales-workspace\"", productivity, StringComparison.Ordinal);
	}

	[Fact]
	public void PaletteDiscoversAdministrationSectionsWithoutCreatingAdministrationWorkspace()
	{
		var root = FindRepositoryRoot();
		var palette = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ShellPaletteWindow.xaml.cs"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		var administration = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "Administration", "AdministrationViewModel.cs"));

		Assert.Contains("_viewModel.AdministrationNavigationItems", palette, StringComparison.Ordinal);
		Assert.DoesNotContain("_viewModel.AdministrationViewModel.NavigationItems", palette, StringComparison.Ordinal);
		Assert.Contains("AdministrationNavigationItems = AdministrationViewModel.CreateNavigationItems(authorizationService)", main, StringComparison.Ordinal);
		Assert.Contains("internal static IReadOnlyList<NavigationItem> CreateNavigationItems", administration, StringComparison.Ordinal);
	}

	[Fact]
	public void RouteDiscoveryUsesPageMetadataWithoutMaterializingWorkspaceContent()
	{
		var root = FindRepositoryRoot();
		var navigator = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ShellRouteNavigator.cs"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));

		Assert.Contains("item.Pages.FirstOrDefault", navigator, StringComparison.Ordinal);
		Assert.DoesNotContain("if (item.Content is not ShellModuleViewModel module) return null;", navigator, StringComparison.Ordinal);
		Assert.Contains("pages:pages", main, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
			directory = directory.Parent;
		}
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
