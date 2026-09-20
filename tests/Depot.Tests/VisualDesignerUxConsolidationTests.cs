// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;

using Xunit;

namespace Depot.Tests;

public sealed class VisualDesignerUxConsolidationTests
{
	[Fact]
	public void SharedValidationIssueTemplateKeepsAccessibleSeverityAndMessageContract()
	{
		var root = FindRepositoryRoot();
		var resources = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Workflows.xaml"));

		Assert.Contains("x:Key=\"DesignerValidationIssueTemplate\"", resources, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"{Binding Message}\"", resources, StringComparison.Ordinal);
		Assert.Contains("Text=\"{Binding Severity}\"", resources, StringComparison.Ordinal);
		Assert.Contains("Text=\"{Binding Message}\"", resources, StringComparison.Ordinal);
	}

	[Fact]
	public void ProductiveDesignersReuseSharedValidationIssuePresentation()
	{
		var root = FindRepositoryRoot();
		var posting = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinancePostingFlowDesignerView.xaml"));
		var import = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ImportView.xaml"));

		Assert.Contains("ItemsSource=\"{Binding Issues}\" ItemTemplate=\"{StaticResource DesignerValidationIssueTemplate}\"", posting, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding MappingIssues}\" ItemTemplate=\"{StaticResource DesignerValidationIssueTemplate}\"", import, StringComparison.Ordinal);
	}

	[Fact]
	public void ConsolidationPreservesKeyboardAccessibilityAndVirtualizationContracts()
	{
		var root = FindRepositoryRoot();
		var posting = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinancePostingFlowDesignerView.xaml"));
		var import = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ImportView.xaml"));

		Assert.Contains("Content=\"Add selected rule\"", posting, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.HelpText=\"Use arrow keys to select nodes.", posting, StringComparison.Ordinal);
		Assert.Contains("EnableColumnVirtualization=\"True\"", import, StringComparison.Ordinal);
		Assert.Contains("EnableRowVirtualization=\"True\"", import, StringComparison.Ordinal);
		Assert.Contains("SelectedItem=\"{Binding SelectedTarget, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", import, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
		}

		throw new DirectoryNotFoundException("Could not locate the Depot repository root from the test output directory.");
	}
}
