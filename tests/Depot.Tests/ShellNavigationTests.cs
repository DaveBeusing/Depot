// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class ShellNavigationTests
{
	[Fact]
	public void SystemRoleProfilesExposeOnlyTheirIntendedShellModules()
	{
		var administrator = Permissions(SystemRoleCatalog.AdministratorCode);
		var purchasing = Permissions(SystemRoleCatalog.PurchasingCode);
		var approver = Permissions(SystemRoleCatalog.ApproverCode);
		var warehouseOperator = Permissions(SystemRoleCatalog.WarehouseOperatorCode);
		var user = Permissions(SystemRoleCatalog.UserCode);

		Assert.Equal(PermissionCatalog.All.Count, administrator.Count);
		Assert.Contains(ApplicationPermission.PurchasingView, purchasing);
		Assert.Contains(ApplicationPermission.PurchaseOrdersCreate, purchasing);
		Assert.DoesNotContain(ApplicationPermission.PurchaseOrdersApprove, purchasing);
		Assert.Contains(ApplicationPermission.PurchaseOrdersApprove, approver);
		Assert.DoesNotContain(ApplicationPermission.PurchasingView, approver);
		Assert.Contains(ApplicationPermission.PurchasingView, warehouseOperator);
		Assert.Contains(ApplicationPermission.GoodsReceiptsPost, warehouseOperator);
		Assert.DoesNotContain(ApplicationPermission.PurchaseOrdersView, warehouseOperator);
		Assert.DoesNotContain(ApplicationPermission.PurchasingView, user);
		Assert.DoesNotContain(ApplicationPermission.AdministrationView, user);
	}

	[Fact]
	public void ModuleKeepsItsSelectedSecondaryPage()
	{
		var first = new SecondaryNavigationItem("First", () => new StubViewModel(), (_, _) => Task.CompletedTask, "first");
		var second = new SecondaryNavigationItem("Second", () => new StubViewModel(), (_, _) => Task.CompletedTask, "second");
		using var module = new ShellModuleViewModel("Module", "Description", [first, second]);

		module.SelectedPage = second;

		Assert.Same(second, module.SelectedPage);
		Assert.Same(second.Content, module.CurrentViewModel);
	}

	private static IReadOnlySet<ApplicationPermission> Permissions(string code) =>
		SystemRoleCatalog.Definitions.Single(role => role.Code == code).Permissions;

	private sealed class StubViewModel : BaseViewModel;
}
