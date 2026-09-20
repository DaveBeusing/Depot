// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Controls;

using Xunit;

namespace Depot.Tests;

public sealed class MotionSystemTests
{
	[Fact]
	public void MotionResourcesExposeCanonicalDurationsAndEasing()
	{
		var root = FindRepositoryRoot();
		var motion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Motion.xaml"));
		var theme = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Theme.xaml"));

		Assert.Contains("Motion.Duration.Fast", motion, StringComparison.Ordinal);
		Assert.Contains("0:0:0.10", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Duration.Standard", motion, StringComparison.Ordinal);
		Assert.Contains("0:0:0.16", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Duration.Emphasis", motion, StringComparison.Ordinal);
		Assert.Contains("0:0:0.22", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Ease.Out", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Ease.InOut", motion, StringComparison.Ordinal);
		Assert.Contains("ResourceDictionary Source=\"Motion.xaml\"", theme, StringComparison.Ordinal);
	}

	[Fact]
	public void ReducedMotionCollapsesNonEssentialDurations()
	{
		var reduced = MotionDurations.Resolve(MotionSpeed.Emphasis, reduceMotion: true);
		var standard = MotionDurations.Resolve(MotionSpeed.Standard, reduceMotion: false);

		Assert.True(reduced.HasTimeSpan);
		Assert.Equal(TimeSpan.Zero, reduced.TimeSpan);
		Assert.True(standard.HasTimeSpan);
		Assert.Equal(TimeSpan.FromMilliseconds(160), standard.TimeSpan);
	}

	[Fact]
	public void MotionFoundationDoesNotAnimateLayoutDimensions()
	{
		var root = FindRepositoryRoot();
		var motion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Motion.xaml"));

		Assert.DoesNotContain("Storyboard.TargetProperty=\"Width\"", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Height\"", motion, StringComparison.Ordinal);
	}


	[Fact]
	public void ShellUsesMotionContentHostsAndNavigationUsesSharedTokens()
	{
		var root = FindRepositoryRoot();
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml"));
		var navigation = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Navigation.xaml"));

		Assert.Contains("TransitionKind=\"Workspace\"", main, StringComparison.Ordinal);
		Assert.Contains("TransitionKind=\"State\"", main, StringComparison.Ordinal);
		Assert.Contains("MotionDuration Kind=Fast", navigation, StringComparison.Ordinal);
		Assert.Contains("MotionDuration Kind=Standard", navigation, StringComparison.Ordinal);
		Assert.DoesNotContain("Duration=\"{StaticResource Motion.Duration.", navigation, StringComparison.Ordinal);
		Assert.DoesNotContain("NavigationAnimationDuration", navigation, StringComparison.Ordinal);
	}

	[Fact]
	public void RuntimeMotionUsesOpacityAndRenderTransformsInsteadOfLayoutDimensions()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));

		Assert.Contains("UIElement.OpacityProperty", source, StringComparison.Ordinal);
		Assert.Contains("TranslateTransform.YProperty", source, StringComparison.Ordinal);
		Assert.DoesNotContain("WidthProperty", source, StringComparison.Ordinal);
		Assert.DoesNotContain("HeightProperty", source, StringComparison.Ordinal);
	}


	[Fact]
	public void WorkflowAndStatusSurfacesUseReusableMotionKinds()
	{
		var root = FindRepositoryRoot();
		var workflows = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));
		var status = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Status.xaml"));
		var emptyStates = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "EmptyStates.xaml"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml"));
		var navigation = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Navigation.xaml"));

		Assert.Contains("TransitionKind=\"DetailPane\"", workflows, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"State\"", workflows, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"Timeline\"", workflows, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"Status\"", status, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"State\"", emptyStates, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"NotificationBadge\"", main, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"NotificationBadge\"", navigation, StringComparison.Ordinal);
	}

	[Fact]
	public void MotionPackagePreservesDataGridVirtualizationAndAvoidsPermanentAnimation()
	{
		var root = FindRepositoryRoot();
		var dataGrid = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "DataGrid.xaml"));
		var motionFiles = string.Join("\n", new[]
		{
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Motion.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Navigation.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Status.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "EmptyStates.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "MainWindow.xaml"))
		});

		Assert.Contains("ScrollViewer.CanContentScroll\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.IsVirtualizing\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("EnableRowVirtualization\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("EnableColumnVirtualization\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
		Assert.DoesNotContain("RepeatBehavior=\"Forever\"", motionFiles, StringComparison.OrdinalIgnoreCase);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
