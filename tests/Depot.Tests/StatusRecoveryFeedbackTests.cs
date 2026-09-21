// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class StatusRecoveryFeedbackTests
{
	[Fact]
	public void OperationStatusExposesCommandBoundRecoveryContract()
	{
		var root = FindRepositoryRoot();
		var control = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "OperationStatus.cs"));
		var status = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Status.xaml"));

		Assert.Contains("ActionCommandProperty", control, StringComparison.Ordinal);
		Assert.Contains("ActionCommandParameterProperty", control, StringComparison.Ordinal);
		Assert.Contains("HasActionPropertyKey", control, StringComparison.Ordinal);
		Assert.Contains("!string.IsNullOrWhiteSpace(status.ActionText) && status.ActionCommand is not null", control, StringComparison.Ordinal);
		Assert.Contains("Command=\"{TemplateBinding ActionCommand}\"", status, StringComparison.Ordinal);
		Assert.Contains("CommandParameter=\"{TemplateBinding ActionCommandParameter}\"", status, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"{TemplateBinding ActionText}\"", status, StringComparison.Ordinal);
		Assert.Contains("Trigger Property=\"HasAction\" Value=\"True\"", status, StringComparison.Ordinal);
		Assert.DoesNotContain("<TextBlock x:Name=\"Action\"", status, StringComparison.Ordinal);
	}

	[Fact]
	public void RecoveryButtonReliesOnNativeCommandCanExecuteState()
	{
		var root = FindRepositoryRoot();
		var status = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Status.xaml"));

		Assert.Contains("Command=\"{TemplateBinding ActionCommand}\"", status, StringComparison.Ordinal);
		Assert.DoesNotContain("IsEnabled=\"{TemplateBinding", status, StringComparison.Ordinal);
		Assert.DoesNotContain("IsEnabled=\"True\"", status, StringComparison.Ordinal);
		Assert.Contains("SecondaryButtonStyle", status, StringComparison.Ordinal);
		Assert.Contains("AppKeyboardFocusVisualStyle", File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Buttons.xaml")), StringComparison.Ordinal);
	}

	[Fact]
	public void AccessibilityAnnouncementsAndStatusSemanticsRemainIntact()
	{
		var root = FindRepositoryRoot();
		var control = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "OperationStatus.cs"));
		var status = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Status.xaml"));

		Assert.Contains("AccessibilityAutomation.Announce", control, StringComparison.Ordinal);
		Assert.Contains("status.HasError || status.Severity == OperationSeverity.Warning", control, StringComparison.Ordinal);
		Assert.Contains("Property=\"IsBusy\" Value=\"True\"", status, StringComparison.Ordinal);
		Assert.Contains("Property=\"HasError\" Value=\"True\"", status, StringComparison.Ordinal);
		Assert.Contains("OperationSeverity.Warning", status, StringComparison.Ordinal);
		Assert.Contains("OperationSeverity.Success", status, StringComparison.Ordinal);
	}

	[Fact]
	public void DocumentStatusChangesUseOneShotReducedMotionAwareScaleFeedback()
	{
		var root = FindRepositoryRoot();
		var workflow = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "WorkflowControls.cs"));
		var motion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));

		Assert.Contains("_hasPresentedStatus", workflow, StringComparison.Ordinal);
		Assert.Contains("badge._hasPresentedStatus && badge.IsLoaded && changed", workflow, StringComparison.Ordinal);
		Assert.Contains("MotionTransitionKind.StatusChange", workflow, StringComparison.Ordinal);
		Assert.Contains("MotionTransitionKind.StatusChange => 0.96d", motion, StringComparison.Ordinal);
		Assert.Contains("MotionPreferences.IsReducedMotionEnabled", motion, StringComparison.Ordinal);
		Assert.Contains("UIElement.OpacityProperty", motion, StringComparison.Ordinal);
		Assert.Contains("ScaleTransform.ScaleXProperty", motion, StringComparison.Ordinal);
		Assert.Contains("ScaleTransform.ScaleYProperty", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("WidthProperty", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("HeightProperty", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("RepeatBehavior", motion, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ExistingRefreshCommandsDriveOnlyExistingRecoveryScenarios()
	{
		var root = FindRepositoryRoot();
		var baseViewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "BaseViewModel.cs"));
		var banking = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceBankingView.xaml"));
		var reporting = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceFinancialReportingView.xaml"));
		var database = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Administration", "DatabaseSettingsView.xaml"));
		var workflows = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));

		Assert.Contains("OperationActionText = \"Reload\"", baseViewModel, StringComparison.Ordinal);
		foreach (var view in new[] { banking, reporting, database })
		{
			Assert.Contains("ActionText=\"{Binding OperationActionText}\"", view, StringComparison.Ordinal);
			Assert.Contains("ActionCommand=\"{Binding RefreshCommand}\"", view, StringComparison.Ordinal);
			Assert.Contains("Severity=\"{Binding OperationSeverity}\"", view, StringComparison.Ordinal);
		}

		Assert.Contains("Copy diagnostics", workflows, StringComparison.Ordinal);
		Assert.Contains("Open Help", workflows, StringComparison.Ordinal);
		Assert.Contains("HelpTopicId=\"troubleshooting.database-connection-failures\"", database, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
