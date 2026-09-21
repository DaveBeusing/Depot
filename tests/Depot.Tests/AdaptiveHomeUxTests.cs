// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class AdaptiveHomeUxTests
{
	[Fact]
	public void MyWorkPresentationUsesSemanticValuesAndUnifiedStates()
	{
		var money = new MyWorkItem(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrderApproval, 1, "SO-1", "Sales approval", "Customer", "Pending Approval", 1250m, DateTime.Today, null, MyWorkPriority.High, "approvals.sales", "Review", 42);
		var quantity = new MyWorkItem(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.Shipment, 2, "SHP-1", "Packed shipment", "Customer", "Ready", 25m, DateTime.Today.AddDays(-1), null, MyWorkPriority.Critical, "sales.shipping", "Post", 42);

		Assert.EndsWith(" EUR", money.AmountOrQuantityDisplay, StringComparison.Ordinal);
		Assert.Equal("Due today", money.DueStateDisplay);
		Assert.Equal("High", money.PriorityDisplay);
		Assert.EndsWith(" pcs", quantity.AmountOrQuantityDisplay, StringComparison.Ordinal);
		Assert.Equal("Overdue", quantity.DueStateDisplay);
		Assert.Equal("High", quantity.PriorityDisplay);
	}

	[Fact]
	public void MyWorkDefaultsToNeedsActionAndExposesRequiredQuickFilters()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "MyWorkPanel.xaml"));
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "DashboardViewModel.cs"));

		Assert.Contains("SelectedIndex=\"0\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Tag=\"Overdue\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Tag=\"Today\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Tag=\"HighPriority\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Style=\"{StaticResource FilterChipToggleStyle}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("IsChecked=\"{Binding IsAllMyWorkFilter, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("IsChecked=\"{Binding IsOverdueMyWorkFilter, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("IsChecked=\"{Binding IsTodayMyWorkFilter, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("IsChecked=\"{Binding IsHighPriorityMyWorkFilter, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("MyWorkOverdueCount", xaml, StringComparison.Ordinal);
		Assert.Contains("MyWorkTodayCount", xaml, StringComparison.Ordinal);
		Assert.Contains("MyWorkHighPriorityCount", xaml, StringComparison.Ordinal);
		Assert.Contains("<controls:Card", xaml, StringComparison.Ordinal);
		Assert.Contains("<controls:FilterBar", xaml, StringComparison.Ordinal);
		Assert.Contains("Style=\"{StaticResource AppTabControlStyle}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Style=\"{StaticResource AppDataGridCompactStyle}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("<controls:EmptyState", xaml, StringComparison.Ordinal);
		Assert.Contains("MyWorkQuickFilter.Overdue", source, StringComparison.Ordinal);
		Assert.Contains("MyWorkQuickFilter.Today", source, StringComparison.Ordinal);
		Assert.Contains("MyWorkQuickFilter.HighPriority", source, StringComparison.Ordinal);
	}

	[Fact]
	public void MyWorkFilterCountsStayOnTheLoadedSnapshotAndPrimaryActionIsExplicit()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "MyWorkPanel.xaml"));
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "DashboardViewModel.cs"));

		Assert.Contains("CountMyWorkItems(MyWorkQuickFilter.Overdue)", source, StringComparison.Ordinal);
		Assert.Contains("_myWorkSnapshot.Sections", source, StringComparison.Ordinal);
		Assert.Contains("MatchesMyWorkFilter(item, filter, today)", source, StringComparison.Ordinal);
		Assert.Contains("NotifyMyWorkFilterPresentation()", source, StringComparison.Ordinal);
		Assert.Contains("Style=\"{StaticResource PrimaryButtonStyle}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Visibility=\"{Binding HasPrimaryAction", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public void MyWorkActionPresentationRequiresBothActionAndRoute()
	{
		var actionable = new MyWorkItem(MyWorkSectionKind.NeedsMyAction, MyWorkItemKind.SalesOrder, 1, "SO-1", "Order", "Customer", "Ready", null, null, null, MyWorkPriority.Normal, "sales.orders", "Open", 42);
		var noAction = actionable with { PrimaryAction = string.Empty };
		var noRoute = actionable with { RouteId = string.Empty };

		Assert.True(actionable.HasPrimaryAction);
		Assert.False(noAction.HasPrimaryAction);
		Assert.False(noRoute.HasPrimaryAction);
	}

	[Fact]
	public void HomeUsesPermissionAdaptiveProjectionWithoutHardCodedRoles()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "DashboardViewModel.cs"));
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "DashboardView.xaml"));

		Assert.Contains("_authorization.HasPermission(ApplicationPermission.", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Role ==", source, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding AdaptiveKpis}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding HomeQuickActions}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("<views:MyWorkPanel />", xaml, StringComparison.Ordinal);
		Assert.Contains("<controls:Card Style=\"{StaticResource WorkspaceCardStyle}\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public void OperationalPermissionsPrioritizePersonaRelevantKpisWithoutRoleChecks()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "DashboardViewModel.cs"));

		Assert.Contains("warehouseOperational", source, StringComparison.Ordinal);
		Assert.Contains("fulfillmentOperational", source, StringComparison.Ordinal);
		Assert.Contains("purchasingOperational", source, StringComparison.Ordinal);
		Assert.Contains("treasuryOperational", source, StringComparison.Ordinal);
		Assert.Contains("OrderByDescending(candidate => candidate.Priority)", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Role ==", source, StringComparison.Ordinal);
	}

	[Fact]
	public void WorkspaceContinuationIsCompactAndDoesNotDuplicateOperationalQuickActions()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "WorkspaceProductivityPanel.xaml"));

		Assert.Contains("Continue working", xaml, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding Favorites}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding Recents}\"", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("ItemsSource=\"{Binding QuickActions}\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public void MyWorkKeepsKeyboardAndFailureIsolationAffordances()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "MyWorkPanel.xaml"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "MyWorkService.cs"));

		Assert.Contains("KeyboardNavigation.TabNavigation=\"Continue\"", xaml, StringComparison.Ordinal);
		Assert.Contains("IsMyWorkLoading", xaml, StringComparison.Ordinal);
		Assert.Contains("new MyWorkProviderFailure", service, StringComparison.Ordinal);
		Assert.Contains("Some work sources are unavailable", File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "DashboardViewModel.cs")), StringComparison.Ordinal);
	}

	[Fact]
	public void HomeAppliesDashboardAndMyWorkProgressivelyWithLatestRequestGuards()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "DashboardViewModel.cs"));

		Assert.Contains("ApplyDashboardWhenReadyAsync", source, StringComparison.Ordinal);
		Assert.Contains("ApplyMyWorkWhenReadyAsync", source, StringComparison.Ordinal);
		Assert.Contains("Task.WhenAll(dashboardApplyTask, myWorkApplyTask)", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Task.WhenAll(dashboardTask, myWorkTask)", source, StringComparison.Ordinal);
		Assert.Contains("if (!request.IsCurrent) return;", source, StringComparison.Ordinal);
		Assert.Contains("progress.RecordFirstContent(\"dashboard\")", source, StringComparison.Ordinal);
		Assert.Contains("progress.RecordFirstContent(\"my-work\")", source, StringComparison.Ordinal);
	}

	[Fact]
	public void HomeManualShellRefreshStillReloadsTheWholeDashboard()
	{
		var root = FindRepositoryRoot();
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		Assert.Contains("AddDirect(ApplicationPermission.DashboardView, \"Dashboard\"", shell, StringComparison.Ordinal);
		Assert.Contains("(viewModel, token) => viewModel.LoadAsync(token)", shell, StringComparison.Ordinal);
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
