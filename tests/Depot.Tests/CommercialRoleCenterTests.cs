// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class CommercialRoleCenterTests
{
	[Theory]
	[InlineData(ApplicationPermission.SalesQuotesView, CommercialRoleCenterKind.SalesWorkspace)]
	[InlineData(ApplicationPermission.SalesOrdersApprove, CommercialRoleCenterKind.SalesControlCenter)]
	[InlineData(ApplicationPermission.PurchaseOrdersView, CommercialRoleCenterKind.BuyerWorkbench)]
	[InlineData(ApplicationPermission.PurchaseOrdersApprove, CommercialRoleCenterKind.ApprovalInbox)]
	[InlineData(ApplicationPermission.SalesOrdersApprove, CommercialRoleCenterKind.ApprovalInbox)]
	[InlineData(ApplicationPermission.FinanceSupplierInvoicesApprove, CommercialRoleCenterKind.ApprovalInbox)]
	[InlineData(ApplicationPermission.FinancePaymentProposalsApprove, CommercialRoleCenterKind.ApprovalInbox)]
	public void RoleCenterVisibilityFollowsEffectivePermissions(ApplicationPermission permission, CommercialRoleCenterKind expected)
	{
		var service = CreateService(permission);
		Assert.True(service.CanAccess(expected));
	}

	[Fact]
	public void CrossRoleUserSeesUnionOfCommercialRoleCenters()
	{
		var service = CreateService(
			ApplicationPermission.SalesQuotesView,
			ApplicationPermission.SalesOrdersApprove,
			ApplicationPermission.PurchaseOrdersView,
			ApplicationPermission.FinanceSupplierInvoicesApprove);

		Assert.True(service.CanAccess(CommercialRoleCenterKind.SalesWorkspace));
		Assert.True(service.CanAccess(CommercialRoleCenterKind.SalesControlCenter));
		Assert.True(service.CanAccess(CommercialRoleCenterKind.BuyerWorkbench));
		Assert.True(service.CanAccess(CommercialRoleCenterKind.ApprovalInbox));
	}

	[Fact]
	public void ApprovalPermissionAloneExposesInboxWithoutGeneralModuleViewPermission()
	{
		var service = CreateService(ApplicationPermission.FinancePaymentProposalsApprove);

		Assert.True(service.CanAccess(CommercialRoleCenterKind.ApprovalInbox));
		Assert.False(service.CanAccess(CommercialRoleCenterKind.SalesWorkspace));
		Assert.False(service.CanAccess(CommercialRoleCenterKind.BuyerWorkbench));
	}

	[Fact]
	public void PurchasingRoleCanSeeSupplierReturnAttention()
	{
		var purchasing = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.PurchasingCode);
		Assert.Contains(ApplicationPermission.SupplierReturnsView, purchasing.Permissions);
	}

	[Fact]
	public void ProjectionLimitsRemainBounded()
	{
		Assert.InRange(CommercialRoleCenterService.SourceItemLimit, 1, MyWorkService.ProviderItemLimit);
		Assert.InRange(CommercialRoleCenterService.MaximumItemsPerSection, 1, MyWorkService.MaximumItemsPerSection);
	}

	[Fact]
	public void EmptySectionCarriesExplicitEmptyState()
	{
		var section = new CommercialRoleSection("Submitted", "No submitted orders.", []);
		Assert.True(section.IsEmpty);
		Assert.Equal("No submitted orders.", section.EmptyText);
	}

	private static CommercialRoleCenterService CreateService(params ApplicationPermission[] permissions)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 42, Email = "role-center@test.local", DisplayName = "Role Center", IsActive = true }, permissions);
		return new CommercialRoleCenterService(
			authorization,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!);
	}
}
