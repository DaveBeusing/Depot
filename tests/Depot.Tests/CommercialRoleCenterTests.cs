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
	[InlineData(ApplicationPermission.GoodsReceiptsPost, CommercialRoleCenterKind.ReceivingWorkspace)]
	[InlineData(ApplicationPermission.ShipmentsCreate, CommercialRoleCenterKind.FulfillmentWorkspace)]
	[InlineData(ApplicationPermission.InventoryCountsEdit, CommercialRoleCenterKind.InventoryControlWorkspace)]
	[InlineData(ApplicationPermission.FinanceReceivablesView, CommercialRoleCenterKind.ReceivablesWorkspace)]
	[InlineData(ApplicationPermission.FinancePayablesView, CommercialRoleCenterKind.PayablesWorkspace)]
	[InlineData(ApplicationPermission.FinancePaymentProposalsCreate, CommercialRoleCenterKind.TreasuryWorkspace)]
	[InlineData(ApplicationPermission.FinanceGeneralLedgerView, CommercialRoleCenterKind.AccountingControlWorkspace)]
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
	public void WarehousePersonasRemainFunctionallySeparated()
	{
		var receiver = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.GoodsReceiverCode);
		var fulfillment = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.FulfillmentOperatorCode);
		var inventory = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.InventoryControllerCode);

		Assert.Contains(ApplicationPermission.SupplierReturnsCreate, receiver.Permissions);
		Assert.DoesNotContain(ApplicationPermission.SalesOrdersView, receiver.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceView, receiver.Permissions);

		Assert.Contains(ApplicationPermission.ShipmentsPost, fulfillment.Permissions);
		Assert.DoesNotContain(ApplicationPermission.PurchaseOrdersCreate, fulfillment.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceView, fulfillment.Permissions);

		Assert.Contains(ApplicationPermission.InventoryCountsPost, inventory.Permissions);
		Assert.Contains(ApplicationPermission.StockTransfersPost, inventory.Permissions);
		Assert.Contains(ApplicationPermission.MaterialIssuesPost, inventory.Permissions);
		Assert.Contains(ApplicationPermission.MaterialReturnsPost, inventory.Permissions);
	}

	[Fact]
	public void FinancePersonasRemainSeparatedByWorkspaceAndAuthority()
	{
		var receivables = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.AccountsReceivableCode);
		var payables = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.AccountsPayableCode);
		var receivablesService = CreateService(receivables.Permissions.ToArray());
		var payablesService = CreateService(payables.Permissions.ToArray());

		Assert.True(receivablesService.CanAccess(CommercialRoleCenterKind.ReceivablesWorkspace));
		Assert.False(receivablesService.CanAccess(CommercialRoleCenterKind.PayablesWorkspace));
		Assert.DoesNotContain(ApplicationPermission.FinancePayablesView, receivables.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceBankingView, receivables.Permissions);

		Assert.True(payablesService.CanAccess(CommercialRoleCenterKind.PayablesWorkspace));
		Assert.False(payablesService.CanAccess(CommercialRoleCenterKind.ReceivablesWorkspace));
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierInvoicesApprove, payables.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierMatchExceptionsApprove, payables.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinancePaymentProposalsApprove, payables.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceBankingView, payables.Permissions);
	}

	[Fact]
	public void TreasuryAndAccountingPersonasKeepSensitiveAuthoritiesSeparated()
	{
		var treasury = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.TreasuryCode);
		var accountant = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.AccountantControllerCode);
		var treasuryService = CreateService(treasury.Permissions.ToArray());
		var accountantService = CreateService(accountant.Permissions.ToArray());

		Assert.True(treasuryService.CanAccess(CommercialRoleCenterKind.TreasuryWorkspace));
		Assert.False(treasuryService.CanAccess(CommercialRoleCenterKind.AccountingControlWorkspace));
		Assert.DoesNotContain(ApplicationPermission.FinanceGeneralLedgerView, treasury.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinancePaymentProposalsApprove, treasury.Permissions);

		Assert.True(accountantService.CanAccess(CommercialRoleCenterKind.AccountingControlWorkspace));
		Assert.False(accountantService.CanAccess(CommercialRoleCenterKind.TreasuryWorkspace));
		Assert.DoesNotContain(ApplicationPermission.FinancePaymentProposalsApprove, accountant.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinancePaymentRunsPost, accountant.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceManualJournalsPost, accountant.Permissions);
	}

	[Fact]
	public void ManualJournalAuthorityDoesNotFollowGeneralLedgerPostingAuthority()
	{
		var accountant = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.AccountantControllerCode);
		Assert.Contains(ApplicationPermission.FinanceGeneralLedgerPost, accountant.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceManualJournalsPost, accountant.Permissions);
	}

	[Fact]
	public void FinanceRoleItemsExposeOperationalColumns()
	{
		var item = new CommercialRoleItem(
			CommercialRoleItemKind.ReceivableOpenItem,
			1,
			2,
			"INV-1",
			"Customer Invoice",
			"Example Customer",
			"Open",
			125m,
			DateTime.UtcNow,
			DateTime.Today,
			4,
			"finance.receivables",
			Currency: "EUR",
			StateDetail: "Partially settled",
			NextAction: "Review / dunning");

		Assert.Equal("EUR", item.Currency);
		Assert.Equal("Partially settled", item.StateDetail);
		Assert.Equal("Review / dunning", item.NextAction);
		Assert.Equal("4 d", item.AgeDisplay);
		Assert.NotEmpty(item.DueDisplay);
	}

	[Fact]
	public void ProjectionLimitsRemainBounded()
	{
		Assert.InRange(CommercialRoleCenterService.SourceItemLimit, 1, MyWorkService.ProviderItemLimit);
		Assert.InRange(CommercialRoleCenterService.MaximumItemsPerSection, 1, MyWorkService.MaximumItemsPerSection);
	}

	[Fact]
	public void ApprovalItemCanExposeRequesterWithoutChangingDecisionAuthority()
	{
		var item = new CommercialRoleItem(
			CommercialRoleItemKind.SalesOrderApproval,
			42,
			3,
			"SO-0042",
			"Sales Order",
			"Example Customer",
			"Pending Approval",
			1250m,
			DateTime.UtcNow,
			null,
			1,
			"approvals.sales",
			7,
			true,
			true,
			"User #7");

		Assert.Equal("User #7", item.Requester);
		Assert.True(item.HasDecisionActions);
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
			null!,
			null!,
			null!,
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
