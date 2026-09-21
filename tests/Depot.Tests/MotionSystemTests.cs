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

	[Fact]
	public void ShellTabsUseReducedMotionAwareRenderOnlyStateTransitions()
	{
		var root = FindRepositoryRoot();
		var tabs = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Tabs.xaml"));
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Shell.xaml"));
		var polish = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ShellPolish.xaml"));
		var runtime = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));
		var combined = tabs + "\n" + shell + "\n" + polish;

		Assert.Contains("SelectionAccentScale", tabs, StringComparison.Ordinal);
		Assert.Contains("Storyboard.TargetProperty=\"ScaleX\"", tabs, StringComparison.Ordinal);
		Assert.Contains("controls:MotionBehavior.TransitionKind\" Value=\"State\"", tabs, StringComparison.Ordinal);
		Assert.Contains("SelectionAccentScale", shell, StringComparison.Ordinal);
		Assert.Contains("AccentScale", shell, StringComparison.Ordinal);
		Assert.Contains("MotionDuration Kind=Fast", combined, StringComparison.Ordinal);
		Assert.Contains("MotionDuration Kind=Standard", combined, StringComparison.Ordinal);
		Assert.Contains("HandoffBehavior=\"SnapshotAndReplace\"", polish, StringComparison.Ordinal);
		Assert.Contains("IconHoverScale", polish, StringComparison.Ordinal);
		Assert.Contains("SelectedHaloScale", polish, StringComparison.Ordinal);
		Assert.Contains("HandoffBehavior.SnapshotAndReplace", runtime, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Width\"", combined, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Height\"", combined, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Margin\"", combined, StringComparison.Ordinal);
		Assert.DoesNotContain("RepeatBehavior=\"Forever\"", combined, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ShellUtilityAndWorkspaceCloseButtonsUseSharedButtonFeedback()
	{
		var root = FindRepositoryRoot();
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Shell.xaml"));
		var polish = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ShellPolish.xaml"));

		Assert.Contains("x:Key=\"WorkspaceTabCloseButtonStyle\"", shell, StringComparison.Ordinal);
		Assert.Contains("controls:MotionBehavior.IsButtonFeedbackEnabled\" Value=\"True\"", shell, StringComparison.Ordinal);
		Assert.Contains("Motion.Scale.ButtonUtilityPressed", shell, StringComparison.Ordinal);
		Assert.DoesNotContain("ScaleTransform ScaleX=\"0.92\"", shell, StringComparison.Ordinal);
		Assert.Contains("x:Key=\"ActivityUtilityButtonStyle\"", polish, StringComparison.Ordinal);
		Assert.Contains("Motion.Scale.ButtonUtilityPressed", polish, StringComparison.Ordinal);
		Assert.Contains("x:Key=\"UserAvatarButtonStyle\"", polish, StringComparison.Ordinal);
		Assert.Contains("x:Key=\"BrandButtonStyle\"", polish, StringComparison.Ordinal);
		Assert.Contains("x:Key=\"FooterVersionButtonStyle\"", polish, StringComparison.Ordinal);
	}

	[Fact]
	public void ButtonResourcesHaveSingleCanonicalAuthority()
	{
		var root = FindRepositoryRoot();
		var resourceRoot = Path.Combine(root, "src", "Depot", "Resources");
		var theme = File.ReadAllText(Path.Combine(resourceRoot, "Theme.xaml"));
		var allResources = Directory.EnumerateFiles(resourceRoot, "*.xaml")
			.Select(File.ReadAllText)
			.ToArray();

		Assert.Contains("ResourceDictionary Source=\"Buttons.xaml\"", theme, StringComparison.Ordinal);
		Assert.DoesNotContain("ButtonPolish.xaml", theme, StringComparison.Ordinal);
		Assert.False(File.Exists(Path.Combine(resourceRoot, "ButtonPolish.xaml")));

		foreach (var key in new[]
		{
			"AppButtonBaseStyle",
			"PrimaryButtonStyle",
			"SecondaryButtonStyle",
			"DangerButtonStyle",
			"AppLinkButtonStyle"
		})
		{
			var declaration = $"x:Key=\"{key}\"";
			Assert.Equal(1, allResources.Sum(resource => CountOccurrences(resource, declaration)));
		}
	}

	[Fact]
	public void ButtonFeedbackUsesSharedMotionTokensAndReducedMotionGuard()
	{
		var root = FindRepositoryRoot();
		var motion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Motion.xaml"));
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));
		var buttons = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Buttons.xaml"));
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ShellPolish.xaml"));

		Assert.Contains("Motion.Scale.ButtonHover", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Scale.ButtonPressed", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Scale.ButtonLinkPressed", motion, StringComparison.Ordinal);
		Assert.Contains("Motion.Scale.ButtonUtilityPressed", motion, StringComparison.Ordinal);
		Assert.Contains("IsButtonFeedbackEnabledProperty", source, StringComparison.Ordinal);
		Assert.Contains("MotionPreferences.IsReducedMotionEnabled", source, StringComparison.Ordinal);
		Assert.Contains("ScaleTransform.ScaleXProperty", source, StringComparison.Ordinal);
		Assert.Contains("ScaleTransform.ScaleYProperty", source, StringComparison.Ordinal);
		Assert.Contains("controls:MotionBehavior.IsButtonFeedbackEnabled", buttons, StringComparison.Ordinal);
		Assert.Contains("controls:MotionBehavior.IsButtonFeedbackEnabled", shell, StringComparison.Ordinal);
		Assert.Contains("AppKeyboardFocusVisualStyle", buttons, StringComparison.Ordinal);
		Assert.DoesNotContain("FocusVisualStyle\" Value=\"{x:Null}", buttons, StringComparison.Ordinal);
		Assert.DoesNotContain("ScaleTransform ScaleX=\"0.97\"", buttons, StringComparison.Ordinal);
		Assert.DoesNotContain("ScaleTransform ScaleX=\"0.94\"", shell, StringComparison.Ordinal);
	}

	[Fact]
	public void ButtonMotionAvoidsLayoutAnimationAndPermanentEffects()
	{
		var root = FindRepositoryRoot();
		var files = string.Join("\n", new[]
		{
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Buttons.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ShellPolish.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Motion.xaml")),
			File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"))
		});

		Assert.DoesNotContain("WidthProperty", files, StringComparison.Ordinal);
		Assert.DoesNotContain("HeightProperty", files, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Width\"", files, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Height\"", files, StringComparison.Ordinal);
		Assert.DoesNotContain("RepeatBehavior=\"Forever\"", files, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void DisabledButtonsBypassInteractionAnimation()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));
		var buttons = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Buttons.xaml"));

		Assert.Contains("button.IsEnabled", source, StringComparison.Ordinal);
		Assert.Contains("OnButtonIsEnabledChanged", source, StringComparison.Ordinal);
		Assert.Contains("ResetButtonScale(button)", source, StringComparison.Ordinal);
		Assert.Contains("<Trigger Property=\"IsEnabled\" Value=\"False\">", buttons, StringComparison.Ordinal);
	}

	private static int CountOccurrences(string source, string value)
	{
		var count = 0;
		var index = 0;
		while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += value.Length;
		}
		return count;
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
