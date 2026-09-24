// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class RolePermissionDesignerProjectionTests
{
	[Fact]
	public void EveryCatalogPermissionMapsToExactlyOneRequiredDesignerGroup()
	{
		var projection = RolePermissionDesignerProjector.Project([], PermissionCatalog.Definitions);

		Assert.Equal(PermissionCatalog.Definitions.Count, projection.Permissions.Count);
		Assert.Equal(PermissionCatalog.Definitions.Count, projection.Permissions.Select(value => value.Permission).Distinct().Count());
		Assert.Equal(Enum.GetValues<RolePermissionGroupKind>(), projection.MatrixRows.Select(value => value.Group).ToArray());
		Assert.All(projection.MatrixRows, row => Assert.NotEmpty(row.Permissions));
	}

	[Fact]
	public void MatrixProjectsDirectPermissionsWithoutInventingRoleInheritance()
	{
		var role = new Role
		{
			Id = 7,
			Code = "SALES_TEST",
			Name = "Sales test",
			IsActive = true,
			Permissions = [ApplicationPermission.SalesView, ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersCreate]
		};

		var projection = RolePermissionDesignerProjector.Project([role], PermissionCatalog.Definitions);
		var sales = projection.MatrixRows.Single(value => value.Group == RolePermissionGroupKind.Sales);
		var finance = projection.MatrixRows.Single(value => value.Group == RolePermissionGroupKind.Finance);

		Assert.Equal(3, sales.Cells.Single().DirectPermissionCount);
		Assert.Equal(0, finance.Cells.Single().DirectPermissionCount);
		Assert.True(sales.Cells.Single().HasAny);
		Assert.False(finance.Cells.Single().HasAny);
	}

	[Fact]
	public void SystemRoleRemainsVisibleAndIdentifiedAsProtected()
	{
		var role = new Role
		{
			Id = 1,
			Code = SystemRoleCatalog.ApplicationAdministratorCode,
			Name = "Application Administrator",
			IsSystem = true,
			IsActive = true,
			Permissions = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.ApplicationAdministratorCode).Permissions.ToArray()
		};

		var projection = RolePermissionDesignerProjector.Project([role], PermissionCatalog.Definitions);

		Assert.True(projection.Roles.Single().IsSystem);
		Assert.All(projection.MatrixRows.SelectMany(value => value.Cells), value => Assert.True(value.IsSystem));
	}

	[Fact]
	public void SeparationAdvisoriesAreExplanatoryAndNeverBlocking()
	{
		var advisories = RolePermissionDesignerProjector.Advisories(
		[
			ApplicationPermission.PurchaseOrdersCreate,
			ApplicationPermission.PurchaseOrdersApprove,
			ApplicationPermission.PurchaseRequisitionsManage,
			ApplicationPermission.PurchaseRequisitionsApprove,
			ApplicationPermission.FinancePaymentProposalsCreate,
			ApplicationPermission.FinancePaymentProposalsApprove
		]);

		Assert.Equal(3, advisories.Count);
		Assert.All(advisories, value => Assert.False(value.IsBlocking));
		Assert.Contains(advisories, value => value.Code == "PURCHASE_CREATE_APPROVE");
		Assert.Contains(advisories, value => value.Code == "REQUISITION_MANAGE_APPROVE");
		Assert.Contains(advisories, value => value.Code == "PAYMENT_PROPOSAL_CREATE_APPROVE");
	}
}
