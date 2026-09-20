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
		Assert.Contains("Motion.Duration.Fast", navigation, StringComparison.Ordinal);
		Assert.Contains("Motion.Duration.Standard", navigation, StringComparison.Ordinal);
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

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
