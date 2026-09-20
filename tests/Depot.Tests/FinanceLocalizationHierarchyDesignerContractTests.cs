// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class FinanceLocalizationHierarchyDesignerContractTests
{
	[Fact]
	public void DesignerUsesExistingLocalizationWorkspaceAndCustomUiContract()
	{
		var root=FindRepositoryRoot();
		var view=File.ReadAllText(Path.Combine(root,"src","Depot","Views","FinanceLocalizationView.xaml"));

		Assert.Contains("Header=\"Hierarchy Designer\"",view,StringComparison.Ordinal);
		Assert.Contains("LocalizationHierarchyRows",view,StringComparison.Ordinal);
		Assert.Contains("AssignmentTimelineRows",view,StringComparison.Ordinal);
		Assert.Contains("HierarchyRegistryEntries",view,StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"",view,StringComparison.Ordinal);
		Assert.Contains("WorkspaceSectionStyle",view,StringComparison.Ordinal);
		Assert.Contains("AppListBoxStyle",view,StringComparison.Ordinal);
		Assert.Contains("AppDataGridCompactStyle",view,StringComparison.Ordinal);
		Assert.Contains("controls:TextInput",view,StringComparison.Ordinal);
		Assert.Contains("controls:AppCheckBox",view,StringComparison.Ordinal);
		Assert.Contains("CanEditHierarchySelection",view,StringComparison.Ordinal);
	}

	[Fact]
	public void AssignmentDesignerRequiresSharedServiceValidationBeforeItsSaveAction()
	{
		var root=FindRepositoryRoot();
		var view=File.ReadAllText(Path.Combine(root,"src","Depot","Views","FinanceLocalizationView.xaml"));
		var designer=File.ReadAllText(Path.Combine(root,"src","Depot","ViewModels","FinanceLocalizationViewModel.HierarchyDesigner.cs"));
		var viewModel=File.ReadAllText(Path.Combine(root,"src","Depot","ViewModels","FinanceLocalizationViewModel.cs"));
		var service=File.ReadAllText(Path.Combine(root,"src","Depot","Services","FinanceLocalizationService.cs"));

		Assert.Contains("ValidateDesignerAssignmentCommand",view,StringComparison.Ordinal);
		Assert.Contains("IsEnabled=\"{Binding CanSaveDesignerAssignment}\"",view,StringComparison.Ordinal);
		Assert.Contains("_localization.ValidateAssignmentAsync(CreateAssignmentDraft(), token)",designer,StringComparison.Ordinal);
		Assert.Contains("_localization.SaveAssignmentAsync(value,token)",viewModel,StringComparison.Ordinal);
		Assert.Equal(2,Count(service,"ValidateAssignmentCoreAsync(transaction, value, packCode, token)"));
		Assert.Contains("FinanceLocalizationHierarchyRules.ThrowIfInvalidPack",service,StringComparison.Ordinal);
		Assert.DoesNotContain("FinanceLocalizationRepository",designer,StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess",designer,StringComparison.Ordinal);
	}

	[Fact]
	public void BuiltInsAndComplianceMeaningRemainExplicit()
	{
		var root=FindRepositoryRoot();
		var view=File.ReadAllText(Path.Combine(root,"src","Depot","Views","FinanceLocalizationView.xaml"));
		var model=File.ReadAllText(Path.Combine(root,"src","Depot","Models","FinanceLocalizationHierarchyDesigner.cs"));

		Assert.Contains("Built-in packs are read-only",view,StringComparison.Ordinal);
		Assert.Contains("not compliance certification",view,StringComparison.OrdinalIgnoreCase);
		Assert.Contains("public bool IsReadOnly => Pack.IsBuiltIn",model,StringComparison.Ordinal);
		Assert.Contains("CountryMismatch",model,StringComparison.Ordinal);
		Assert.Contains("Overlap",model,StringComparison.Ordinal);
	}

	private static int Count(string source,string value)
	{
		var count=0;
		for(var index=0;(index=source.IndexOf(value,index,StringComparison.Ordinal))>=0;index+=value.Length) count++;
		return count;
	}

	private static string FindRepositoryRoot()
	{
		for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
			if(File.Exists(Path.Combine(directory.FullName,"Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
