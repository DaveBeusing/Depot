// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class RolePermissionDesignerContractTests
{
	[Fact]
	public void DesignerUsesExistingAdministrationShellAndVirtualizedCustomControls()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "RolesView.xaml"));

		Assert.Contains("Header=\"Permission Matrix\"", view, StringComparison.Ordinal);
		Assert.Contains("Role detail / permission map", view, StringComparison.Ordinal);
		Assert.Contains("PermissionMatrixRows", view, StringComparison.Ordinal);
		Assert.Contains("DesignerRoles", view, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", view, StringComparison.Ordinal);
		Assert.Contains("AppListBoxStyle", view, StringComparison.Ordinal);
		Assert.Contains("AppDataGridCompactStyle", view, StringComparison.Ordinal);
		Assert.Contains("controls:AppCheckBox", view, StringComparison.Ordinal);
		Assert.Contains("controls:TextInput", view, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name", view, StringComparison.Ordinal);
	}

	[Fact]
	public void SystemRolesAreVisibleButEditingStillUsesExistingProtection()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "RolesView.xaml"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "Users", "RoleViewModel.cs"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "RoleService.cs"));

		Assert.Contains("System:", view, StringComparison.Ordinal);
		Assert.Contains("IsEnabled=\"{Binding DataContext.CanEdit", view, StringComparison.Ordinal);
		Assert.Contains("SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsSystem)", viewModel, StringComparison.Ordinal);
		Assert.Contains("Protected system roles cannot be changed.", service, StringComparison.Ordinal);
		Assert.Contains("Protected system roles cannot be deactivated or reactivated manually.", service, StringComparison.Ordinal);
	}

	[Fact]
	public void DesignerSeparatesDirectPermissionsFromEffectiveUserImpact()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "RolesView.xaml"));
		var designer = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "Users", "RoleViewModel.PermissionDesigner.cs"));
		var roleViewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "Users", "RoleViewModel.cs"));

		Assert.Contains("persisted direct permissions only", view, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Effective permission impact", view, StringComparison.Ordinal);
		Assert.Contains("AuthorizationService remains the runtime authority", view, StringComparison.Ordinal);
		Assert.Contains("_service.GetEffectivePermissionUsersAsync", designer, StringComparison.Ordinal);
		Assert.Contains("_service.SaveAsync", roleViewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("RoleRepository", designer, StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess", designer, StringComparison.Ordinal);
	}

	[Fact]
	public void SeparationAdvisoriesRemainExplanatoryAndKeyboardPathHasNoMouseOnlyHandler()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "RolesView.xaml"));
		var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Users", "RolesView.xaml.cs"));
		var model = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "RolePermissionDesigner.cs"));

		Assert.Contains("do not alter AuthorizationService decisions or block RoleService saves", view, StringComparison.Ordinal);
		Assert.Contains("public bool IsBlocking => false", model, StringComparison.Ordinal);
		Assert.Contains("SelectMatrixRoleCommand", view, StringComparison.Ordinal);
		Assert.Contains("CommandParameter=\"{Binding Role}\"", view, StringComparison.Ordinal);
		Assert.DoesNotContain("Mouse", codeBehind, StringComparison.Ordinal);
		Assert.DoesNotContain("DragDrop", codeBehind, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
