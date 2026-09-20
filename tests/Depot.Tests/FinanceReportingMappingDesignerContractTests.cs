// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class FinanceReportingMappingDesignerContractTests
{
	[Fact]
	public void DesignerStaysInsideReportingWorkspaceAndUsesExistingServiceBoundary()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceFinancialReportingView.xaml"));
		var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceFinancialReportingView.xaml.cs"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinanceFinancialReportingViewModel.MappingDesigner.cs"));

		Assert.Contains("Header=\"Mapping Designer\"", view, StringComparison.Ordinal);
		Assert.Contains("MappingAccountList", view, StringComparison.Ordinal);
		Assert.Contains("MappingTargetList", view, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", view, StringComparison.Ordinal);
		Assert.Contains("WorkspaceSectionStyle", view, StringComparison.Ordinal);
		Assert.Contains("controls:TextInput", view, StringComparison.Ordinal);
		Assert.Contains("controls:AppCheckBox", view, StringComparison.Ordinal);
		Assert.Contains("AppDataGridCompactStyle", view, StringComparison.Ordinal);
		Assert.Contains("OnMappingTargetDrop", codeBehind, StringComparison.Ordinal);
		Assert.Contains("ApplyMappingDesignerTarget", codeBehind, StringComparison.Ordinal);
		Assert.Contains("_reporting.SaveMappingAsync", viewModel, StringComparison.Ordinal);
		Assert.Contains("_reporting.GenerateAsync", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("FinanceFinancialReportingRepository", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("INSERT INTO", viewModel, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void DesignerExposesCoverageValidationOrderingAndExistingReportPreview()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceFinancialReportingView.xaml"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinanceFinancialReportingViewModel.MappingDesigner.cs"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "FinanceFinancialReportingService.cs"));

		Assert.Contains("MappingCoverageSummary", view, StringComparison.Ordinal);
		Assert.Contains("MappingValidationText", view, StringComparison.Ordinal);
		Assert.Contains("MoveMappingUpCommand", view, StringComparison.Ordinal);
		Assert.Contains("MoveMappingDownCommand", view, StringComparison.Ordinal);
		Assert.Contains("RefreshMappingPreviewCommand", view, StringComparison.Ordinal);
		Assert.Contains("MappingPreviewRows", view, StringComparison.Ordinal);
		Assert.Contains("FinanceReportingMappingValidator.Validate", viewModel, StringComparison.Ordinal);
		Assert.Contains("FinanceReportingMappingProjector.ApplyTarget", viewModel, StringComparison.Ordinal);
		Assert.Contains("FinanceReportingMappingValidator.ThrowIfInvalid", service, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
