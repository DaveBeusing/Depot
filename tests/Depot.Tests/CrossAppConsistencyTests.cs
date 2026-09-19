// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Xml.Linq;

using Depot.Controls;

using Xunit;

namespace Depot.Tests;

public sealed class CrossAppConsistencyTests
{
	[Theory]
	[InlineData("Draft", StatusBadgeVariant.Neutral)]
	[InlineData("Waiting", StatusBadgeVariant.Warning)]
	[InlineData("Ready", StatusBadgeVariant.Primary)]
	[InlineData("DueToday", StatusBadgeVariant.Warning)]
	[InlineData("Overdue", StatusBadgeVariant.Error)]
	[InlineData("Blocked", StatusBadgeVariant.Error)]
	[InlineData("Completed", StatusBadgeVariant.Success)]
	[InlineData("Rejected", StatusBadgeVariant.Error)]
	[InlineData("Reversed", StatusBadgeVariant.Error)]
	[InlineData("Error", StatusBadgeVariant.Error)]
	public void UnifiedStatusLanguageMapsCanonicalStatesWithoutRenamingThem(string status, StatusBadgeVariant expected)
	{
		var presentation = WorkflowStatusLanguage.Resolve(status);

		Assert.Equal(expected, presentation.Variant);
		Assert.False(string.IsNullOrWhiteSpace(presentation.Glyph));
		Assert.Equal(status == "DueToday" ? "Due Today" : status, presentation.Text);
	}

	[Fact]
	public void WorkspaceTabsExposeDirtyFocusAndSessionLocalReopenContracts()
	{
		var root = FindRepositoryRoot();
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Shell.xaml"));
		var tabs = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "WorkspaceTabControl.cs"));
		var window = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml.cs"));

		Assert.Contains("Unsaved changes", shell, StringComparison.Ordinal);
		Assert.Contains("IsKeyboardFocusWithin", shell, StringComparison.Ordinal);
		Assert.Contains("Reopen Closed Tab", shell, StringComparison.Ordinal);
		Assert.Contains("ReopenClosedTabCommand", tabs, StringComparison.Ordinal);
		Assert.Contains("item.IsDocument ? null : item", tabs, StringComparison.Ordinal);
		Assert.Contains("WorkspaceTabs.ReopenLastClosedTab()", window, StringComparison.Ordinal);
		Assert.Contains("key == Key.T", window, StringComparison.Ordinal);
	}

	[Fact]
	public void StatusBarUsesSafeContextInsteadOfDetailedConnectionEndpoint()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml"));
		var status = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "ConnectionStatusService.cs"));

		Assert.Contains("ConnectionStatus.Context", xaml, StringComparison.Ordinal);
		Assert.Contains("Database context", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("ToolTip=\"{Binding ConnectionStatus.Detail}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Path.GetFileName(settings.LocalDatabasePath)", status, StringComparison.Ordinal);
		Assert.Contains("SQL Server ·", status, StringComparison.Ordinal);
		Assert.Contains("MySQL/MariaDB ·", status, StringComparison.Ordinal);
	}

	[Fact]
	public void SearchUsesCanonicalGroupsAndContextBoundSuggestions()
	{
		var root = FindRepositoryRoot();
		var palette = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ShellPaletteWindow.xaml.cs"));
		var registry = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ShellCommandRegistry.cs"));
		var global = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ShellPaletteWindow.GlobalSearch.cs"));

		foreach (var group in new[] { "Suggested", "Commands", "Workspaces", "Records", "Recent" })
			Assert.Contains($"\"{group}\"", palette + global, StringComparison.Ordinal);
		Assert.Contains("ContextRoute", registry, StringComparison.Ordinal);
		Assert.Contains("contextRoute == activeRoute ? \"Suggested\" : \"Commands\"", palette, StringComparison.Ordinal);
		Assert.Contains("PaletteGroupOrder", palette, StringComparison.Ordinal);
	}

	[Fact]
	public void WelcomeAndNotificationMyWorkContractStayWorkOriented()
	{
		var root = FindRepositoryRoot();
		var welcome = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "WelcomeView.xaml"));
		var docs = File.ReadAllText(Path.Combine(root, "docs", "CrossAppConsistency.md"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));

		Assert.Contains("Open your default workspace, review My Work", welcome, StringComparison.Ordinal);
		Assert.Contains("Notification = something happened", docs, StringComparison.Ordinal);
		Assert.Contains("My Work = something currently requires work", docs, StringComparison.Ordinal);
		Assert.Contains("NavigateToNotificationAsync", main, StringComparison.Ordinal);
		Assert.Contains("No second task persistence", docs, StringComparison.Ordinal);
	}

	[Fact]
	public void ShellChromeProvidesVisibleKeyboardFocusForFocusableChromeButtons()
	{
		var root = FindRepositoryRoot();
		var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
		var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
		var polish = XDocument.Load(Path.Combine(root, "src", "Depot", "Resources", "ShellPolish.xaml"));
		var shell = XDocument.Load(Path.Combine(root, "src", "Depot", "Resources", "Shell.xaml"));

		foreach (var key in new[] { "BrandButtonStyle", "UserAvatarButtonStyle", "FooterVersionButtonStyle" })
		{
			var style = polish.Descendants(x + "Style").Single(element => (string?)element.Attribute(xaml + "Key") == key);
			Assert.Contains(style.Descendants(x + "Trigger"), trigger => (string?)trigger.Attribute("Property") == "IsKeyboardFocused" && (string?)trigger.Attribute("Value") == "True");
		}

		var closeStyle = shell.Descendants(x + "Style").Single(element => (string?)element.Attribute(xaml + "Key") == "WorkspaceTabCloseButtonStyle");
		Assert.Contains(closeStyle.Descendants(x + "Trigger"), trigger => (string?)trigger.Attribute("Property") == "IsKeyboardFocused" && (string?)trigger.Attribute("Value") == "True");
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
