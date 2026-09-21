// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;

using Xunit;

namespace Depot.Tests;

public sealed class WorkflowActionHierarchyTests
{
	[Fact]
	public void WorkflowActionBarProvidesOptionalAccessibleOverflowPresentation()
	{
		var root = FindRepositoryRoot();
		var control = File.ReadAllText(Path.Combine(root, "src", "Depot", "Controls", "WorkflowControls.cs"));
		var resources = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));

		Assert.Contains("OverflowActionsProperty", control, StringComparison.Ordinal);
		Assert.Contains("IsOverflowVisibleProperty", control, StringComparison.Ordinal);
		Assert.DoesNotContain("Depot.Services", control, StringComparison.Ordinal);
		Assert.Contains("WorkflowOverflowToggleStyle", resources, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"More actions\"", resources, StringComparison.Ordinal);
		Assert.Contains("<Popup", resources, StringComparison.Ordinal);
		Assert.Contains("Property=\"IsOverflowVisible\" Value=\"False\"", resources, StringComparison.Ordinal);
		Assert.Contains("AppKeyboardFocusVisualStyle", resources, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"State\"", resources, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Width\"", resources, StringComparison.Ordinal);
		Assert.DoesNotContain("Storyboard.TargetProperty=\"Height\"", resources, StringComparison.Ordinal);
	}

	[Fact]
	public void SalesQuotePrimaryActionPriorityIsCommandDriven()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "SalesCommercialViewModels.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "SalesQuotesView.xaml"));

		Assert.Contains("ShowSendQuotePrimaryAction => SendQuoteCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("ShowAcceptQuotePrimaryAction => !ShowSendQuotePrimaryAction && AcceptQuoteCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("ShowConvertQuotePrimaryAction => !ShowSendQuotePrimaryAction && !ShowAcceptQuotePrimaryAction && ConvertQuoteCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("ShowConvertQuoteSecondaryAction => ConvertQuoteCommand.CanExecute(null) && !ShowConvertQuotePrimaryAction", viewModel, StringComparison.Ordinal);
		Assert.Contains("IsOverflowVisible=\"{Binding ShowQuoteDocumentActions}\"", view, StringComparison.Ordinal);
		Assert.Contains("<controls:WorkflowActionBar.OverflowActions>", view, StringComparison.Ordinal);
		Assert.Contains("Content=\"Quote PDF\"", view, StringComparison.Ordinal);
		Assert.Contains("Content=\"Email quote\"", view, StringComparison.Ordinal);
		Assert.Contains("Style=\"{StaticResource DangerButtonStyle}\" Visibility=\"{Binding ShowRejectQuoteAction", view, StringComparison.Ordinal);

		AssertContextualPrimaryButtons(view, "Mark sent", "Accept", "Convert to order");
	}

	[Fact]
	public void SalesOrderPrimaryActionPriorityIsSharedAcrossOrderSurfaces()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "SalesViewModel.cs"));
		var ordersView = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "SalesOrdersView.xaml"));
		var salesView = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "SalesView.xaml"));

		Assert.Contains("ShowSubmitOrderPrimaryAction => SubmitCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("ShowReleaseOrderPrimaryAction => !ShowSubmitOrderPrimaryAction && ReleaseCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("IsOverflowVisible=\"{Binding Workspace.ShowOrderDocumentActions}\"", ordersView, StringComparison.Ordinal);
		Assert.Contains("IsOverflowVisible=\"{Binding ShowOrderDocumentActions}\"", salesView, StringComparison.Ordinal);
		Assert.Contains("Content=\"Order confirmation PDF\"", ordersView, StringComparison.Ordinal);
		Assert.Contains("Content=\"Order confirmation PDF\"", salesView, StringComparison.Ordinal);

		AssertContextualPrimaryButtons(ordersView, "Submit", "Release reserved");
		AssertContextualPrimaryButtons(salesView, "Submit", "Release reserved");
	}

	[Fact]
	public void PurchaseOrderPrimaryActionPriorityUsesExistingCommands()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ProcurementViewModel.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "PurchaseOrdersView.xaml"));

		Assert.Contains("ShowReopenOrderPrimaryAction => ReopenRejectedCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("ShowSubmitOrderPrimaryAction => !ShowReopenOrderPrimaryAction && SubmitForApprovalCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);
		Assert.Contains("ShowPlaceOrderPrimaryAction => !ShowReopenOrderPrimaryAction && !ShowSubmitOrderPrimaryAction && PlaceOrderCommand.CanExecute(null)", viewModel, StringComparison.Ordinal);

		AssertContextualPrimaryButtons(view, "Reopen draft", "Submit for approval", "Place order");
	}

	[Fact]
	public void ContextualActionViewsDoNotContainMultipleUnconditionalPrimaryButtons()
	{
		var root = FindRepositoryRoot();
		var paths = new[]
		{
			Path.Combine(root, "src", "Depot", "Views", "SalesQuotesView.xaml"),
			Path.Combine(root, "src", "Depot", "Views", "SalesOrdersView.xaml"),
			Path.Combine(root, "src", "Depot", "Views", "SalesView.xaml"),
			Path.Combine(root, "src", "Depot", "Views", "PurchaseOrdersView.xaml")
		};

		foreach (var path in paths)
		{
			var content = File.ReadAllText(path);
			foreach (Match actionBarMatch in Regex.Matches(content, @"<controls:WorkflowActionBar\b[\s\S]*?</controls:WorkflowActionBar>"))
			{
				var actionBar = actionBarMatch.Value;
				var primaryStart = actionBar.IndexOf("<controls:WorkflowActionBar.PrimaryAction>", StringComparison.Ordinal);
				if (primaryStart < 0) continue;
				var primaryEnd = actionBar.IndexOf("</controls:WorkflowActionBar.PrimaryAction>", primaryStart, StringComparison.Ordinal);
				Assert.True(primaryEnd > primaryStart);
				var primary = actionBar[primaryStart..primaryEnd];
				var primaryCount = CountOccurrences(primary, "PrimaryButtonStyle");
				if (primaryCount <= 1) continue;

				Assert.Equal(primaryCount, CountOccurrences(primary, "PrimaryAction, Converter={StaticResource BooleanToVisibilityConverter}"));
				Assert.Equal(primaryCount, CountOccurrences(primary, "MotionBehavior.TransitionKind=\"State\""));
			}
		}
	}

	private static void AssertContextualPrimaryButtons(string view, params string[] contents)
	{
		foreach (var content in contents)
		{
			var marker = $"Content=\"{content}\"";
			var index = view.IndexOf(marker, StringComparison.Ordinal);
			Assert.True(index >= 0, $"Expected action '{content}'.");
			var start = view.LastIndexOf("<Button", index, StringComparison.Ordinal);
			var end = view.IndexOf("/>", index, StringComparison.Ordinal);
			Assert.True(start >= 0 && end > start);
			var button = view[start..(end + 2)];
			Assert.Contains("PrimaryButtonStyle", button, StringComparison.Ordinal);
			Assert.Contains("PrimaryAction, Converter={StaticResource BooleanToVisibilityConverter}", button, StringComparison.Ordinal);
			Assert.Contains("MotionBehavior.TransitionKind=\"State\"", button, StringComparison.Ordinal);
		}
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
