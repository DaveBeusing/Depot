// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class StartupDeferredInitializationTests
{
	[Fact]
	public void SalesWorkspaceIsConstructedOnlyInsideItsLazyFactory()
	{
		var root = FindRepositoryRoot();
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		var window = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml.cs"));

		Assert.DoesNotContain("var salesWorkspace = new SalesViewModel", main, StringComparison.Ordinal);
		Assert.Contains("_salesSearch = new(() =>", main, StringComparison.Ordinal);
		Assert.Contains("var workspace = new SalesViewModel", main, StringComparison.Ordinal);
		Assert.Contains("SalesViewModelCreated?.Invoke(workspace)", main, StringComparison.Ordinal);
		Assert.Contains("new SalesOverviewViewModel(_salesSearch.Value)", main, StringComparison.Ordinal);
		Assert.DoesNotContain("_observedViewModel.SalesViewModel.PropertyChanged", window, StringComparison.Ordinal);
		Assert.Contains("SalesViewModelCreated += OnSalesViewModelCreated", window, StringComparison.Ordinal);
	}

	[Fact]
	public void ShellLoadedActivatesNotificationsAfterMainWindowConstruction()
	{
		var root = FindRepositoryRoot();
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		var window = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml.cs"));
		var notifications = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "NotificationSummaryViewModel.cs"));

		Assert.Contains("public void ActivateShell() => NotificationSummaryViewModel.Activate();", main, StringComparison.Ordinal);
		Assert.Contains("viewModel.ActivateShell();", window, StringComparison.Ordinal);
		Assert.DoesNotContain("_notifications.NotificationsChanged += OnNotificationsChanged;\n\t\t_ = PollAsync", ConstructorBody(notifications), StringComparison.Ordinal);
	}

	private static string ConstructorBody(string source)
	{
		var start = source.IndexOf("public NotificationSummaryViewModel", StringComparison.Ordinal);
		var end = source.IndexOf("public void Activate()", start, StringComparison.Ordinal);
		return source[start..end];
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
