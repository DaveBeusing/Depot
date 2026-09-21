// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class SelectionAwareWorkspaceTests
{
	[Fact]
	public void DetailTransitionsAreKeyedAndReplaceRapidSelectionAnimations()
	{
		var root = FindRepositoryRoot();
		var motion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));
		var controls = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "WorkflowControls.cs"));
		var workflows = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));

		Assert.Contains("TransitionKeyProperty", motion, StringComparison.Ordinal);
		Assert.Contains("OnTransitionKeyChanged", motion, StringComparison.Ordinal);
		Assert.Contains("Equals(args.OldValue, args.NewValue)", motion, StringComparison.Ordinal);
		Assert.Contains("HandoffBehavior.SnapshotAndReplace", motion, StringComparison.Ordinal);
		Assert.Contains("MotionPreferences.IsReducedMotionEnabled", motion, StringComparison.Ordinal);
		Assert.Contains("UIElement.OpacityProperty", motion, StringComparison.Ordinal);
		Assert.Contains("TranslateTransform.YProperty", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("WidthProperty", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("HeightProperty", motion, StringComparison.Ordinal);

		Assert.Contains("DetailTransitionKeyProperty", controls, StringComparison.Ordinal);
		Assert.Contains("TransitionKey=\"{TemplateBinding DetailTransitionKey}\"", workflows, StringComparison.Ordinal);
	}

	[Fact]
	public void ItemWorkspaceUsesDataGridSelectionIdentityAndCommandDrivenActions()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ItemsView.xaml"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ItemsViewModel.cs"));

		Assert.Contains("<DataGrid ItemsSource=\"{Binding Items}\" SelectedItem=\"{Binding SelectedItem}\"", view, StringComparison.Ordinal);
		Assert.Contains("DetailTransitionKey=\"{Binding Editor.Id}\"", view, StringComparison.Ordinal);
		Assert.Contains("Subtitle=\"{Binding SelectionContextText}\"", view, StringComparison.Ordinal);
		Assert.Contains("Visibility=\"{Binding ShowSelectedItemActivationAction, Converter={StaticResource BooleanToVisibilityConverter}}\"", view, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"State\"", view, StringComparison.Ordinal);
		Assert.Contains("ShowSelectedItemActivationAction => DeactivateItemCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
	}

	[Fact]
	public void UserWorkspaceUsesListSelectionIdentityAndCommandDrivenActions()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "UsersView.xaml"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "Users", "UserViewModel.cs"));

		Assert.Contains("<ListBox ItemsSource=\"{Binding Users}\" SelectedItem=\"{Binding SelectedUser}\"", view, StringComparison.Ordinal);
		Assert.Contains("DetailTransitionKey=\"{Binding Editor.Id}\"", view, StringComparison.Ordinal);
		Assert.Contains("Subtitle=\"{Binding SelectionContextText}\"", view, StringComparison.Ordinal);
		Assert.Contains("Visibility=\"{Binding ShowSelectedUserActivationAction, Converter={StaticResource BooleanToVisibilityConverter}}\"", view, StringComparison.Ordinal);
		Assert.Contains("ShowSelectedUserActivationAction => ToggleActiveCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
	}

	[Fact]
	public void ApprovalWorkspaceHidesDecisionActionsWithoutAnAllowedSelection()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "PurchaseOrderApprovalsView.xaml"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "PurchaseOrderApprovalsViewModel.cs"));

		Assert.Contains("DetailTransitionKey=\"{Binding SelectedApproval.Id}\"", view, StringComparison.Ordinal);
		Assert.Contains("Visibility=\"{Binding CanDecideSelected, Converter={StaticResource BooleanToVisibilityConverter}}\"", view, StringComparison.Ordinal);
		Assert.Contains("ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => CanDecideSelected)", viewModel, StringComparison.Ordinal);
		Assert.Contains("RejectCommand = new AsyncRelayCommand(RejectAsync, () => CanDecideSelected)", viewModel, StringComparison.Ordinal);
		Assert.Contains("CanDecideSelected => SelectedApproval is not null", viewModel, StringComparison.Ordinal);
	}

	[Fact]
	public void SelectionTransitionsPreserveKeyboardFocusAndVirtualizationContracts()
	{
		var root = FindRepositoryRoot();
		var motion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "MotionSystem.cs"));
		var workflows = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));
		var dataGrid = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "DataGrid.xaml"));
		var items = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ItemsView.xaml"));
		var users = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "UsersView.xaml"));

		Assert.DoesNotContain(".Focus()", motion, StringComparison.Ordinal);
		Assert.DoesNotContain("Keyboard.Focus", motion, StringComparison.Ordinal);
		Assert.Contains("FocusRequest.RequestId=\"{Binding EditorFocusRequest}\"", items, StringComparison.Ordinal);
		Assert.Contains("FocusRequest.RequestId=\"{Binding EditorFocusRequest}\"", users, StringComparison.Ordinal);
		Assert.Contains("IsKeyboardFocusWithin", workflows, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.IsVirtualizing\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("EnableRowVirtualization\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
		Assert.Contains("EnableColumnVirtualization\" Value=\"True\"", dataGrid, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
