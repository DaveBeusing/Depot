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

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
		}

		throw new DirectoryNotFoundException("Could not locate the Depot repository root from the test output directory.");
	}
}
