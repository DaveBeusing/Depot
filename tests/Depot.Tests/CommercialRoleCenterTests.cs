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
	public void ManagementComplianceMasterDataAndApplicationAdminPersonasExposeTheirWorkspaces()
	{
		var management = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ManagementViewerCode);
		var auditor = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.AuditorComplianceCode);
		var masterData = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.MasterDataManagerCode);
		var applicationAdmin = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ApplicationAdministratorCode);

		Assert.True(CreateService(management.Permissions.ToArray()).CanAccess(CommercialRoleCenterKind.ManagementCockpit));
		Assert.True(CreateService(auditor.Permissions.ToArray()).CanAccess(CommercialRoleCenterKind.AuditComplianceCenter));
		Assert.True(CreateService(masterData.Permissions.ToArray()).CanAccess(CommercialRoleCenterKind.MasterDataWorkspace));
		Assert.True(CreateService(applicationAdmin.Permissions.ToArray()).CanAccess(CommercialRoleCenterKind.ApplicationAdministrationCenter));
	}

	[Fact]
	public void GovernanceRoleCentersRequireCompleteReadCapabilities()
	{
		Assert.False(CreateService(ApplicationPermission.DashboardView).CanAccess(CommercialRoleCenterKind.ManagementCockpit));
		Assert.False(CreateService(ApplicationPermission.AuditLogView).CanAccess(CommercialRoleCenterKind.AuditComplianceCenter));
		Assert.False(CreateService(ApplicationPermission.MasterDataView).CanAccess(CommercialRoleCenterKind.MasterDataWorkspace));
		Assert.False(CreateService(ApplicationPermission.UsersView).CanAccess(CommercialRoleCenterKind.ApplicationAdministrationCenter));

		var management = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ManagementViewerCode);
		Assert.False(CreateService(management.Permissions.Where(permission => permission != ApplicationPermission.FinanceBankingView).ToArray()).CanAccess(CommercialRoleCenterKind.ManagementCockpit));
		Assert.False(CreateService(management.Permissions.Where(permission => permission != ApplicationPermission.ReportsView).ToArray()).CanAccess(CommercialRoleCenterKind.ManagementCockpit));

		var applicationAdmin = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ApplicationAdministratorCode);
		Assert.False(CreateService(applicationAdmin.Permissions.Where(permission => permission != ApplicationPermission.AuditLogView).ToArray()).CanAccess(CommercialRoleCenterKind.ApplicationAdministrationCenter));
	}

	[Fact]
	public void ManagementAndAuditPersonasRemainReadOnlyExceptExplicitExports()
	{
		var management = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ManagementViewerCode);
		var auditor = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.AuditorComplianceCode);

		foreach (var permission in new[]
		{
			ApplicationPermission.SalesOrdersCreate, ApplicationPermission.SalesOrdersEdit, ApplicationPermission.SalesOrdersApprove, ApplicationPermission.SalesOrdersRelease,
			ApplicationPermission.PurchaseOrdersCreate, ApplicationPermission.PurchaseOrdersEdit, ApplicationPermission.PurchaseOrdersApprove, ApplicationPermission.PurchaseOrdersOrder,
			ApplicationPermission.StockMovementsPost, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinancePayablePaymentsPost,
			ApplicationPermission.UsersManage, ApplicationPermission.RolesManage, ApplicationPermission.SecurityEventsManage, ApplicationPermission.UserSessionsTerminate
		})
		{
			Assert.DoesNotContain(permission, management.Permissions);
			Assert.DoesNotContain(permission, auditor.Permissions);
		}

		Assert.Contains(ApplicationPermission.AuditLogExport, auditor.Permissions);
		Assert.Contains(ApplicationPermission.ReportsExport, auditor.Permissions);
		Assert.Contains(ApplicationPermission.FinanceFinancialReportingExport, auditor.Permissions);
	}

	[Fact]
	public void MasterDataAndApplicationAdminDoNotGainOperationalPostingAuthority()
	{
		var masterData = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.MasterDataManagerCode);
		var applicationAdmin = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ApplicationAdministratorCode);

		Assert.Contains(ApplicationPermission.ItemsManage, masterData.Permissions);
		Assert.Contains(ApplicationPermission.MasterDataManage, masterData.Permissions);
		Assert.Contains(ApplicationPermission.CustomersEdit, masterData.Permissions);
		Assert.Contains(ApplicationPermission.SuppliersManage, masterData.Permissions);

		Assert.Contains(ApplicationPermission.UsersManage, applicationAdmin.Permissions);
		Assert.Contains(ApplicationPermission.RolesManage, applicationAdmin.Permissions);
		Assert.Contains(ApplicationPermission.SettingsManage, applicationAdmin.Permissions);
		Assert.Contains(ApplicationPermission.DatabaseManage, applicationAdmin.Permissions);

		foreach (var permission in new[]
		{
			ApplicationPermission.StockMovementsPost,
			ApplicationPermission.PurchaseOrdersOrder,
			ApplicationPermission.SalesOrdersRelease,
			ApplicationPermission.FinanceGeneralLedgerPost,
			ApplicationPermission.FinancePaymentRunsPost
		})
		{
			Assert.DoesNotContain(permission, masterData.Permissions);
			Assert.DoesNotContain(permission, applicationAdmin.Permissions);
		}
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
	public void QuickActionPresentationDefinesOnePrimaryAndExplicitSupportingKinds()
	{
		var primary = new CommercialRoleQuickAction("Review", "review", "route", CommercialRoleQuickActionPresentation.Primary);
		var secondary = new CommercialRoleQuickAction("Create", "create", "route", CommercialRoleQuickActionPresentation.Secondary);
		var overflow = new CommercialRoleQuickAction("More", "more", "route", CommercialRoleQuickActionPresentation.Overflow);

		Assert.True(primary.IsPrimary);
		Assert.False(primary.IsSecondary);
		Assert.True(secondary.IsSecondary);
		Assert.True(overflow.IsOverflow);
	}

	[Fact]
	public void RoleCenterSnapshotUsesServiceOrderAsDeterministicActionPriority()
	{
		var root = FindRepositoryRoot();
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "CommercialRoleCenterService.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "CommercialRoleCenterView.xaml"));

		Assert.Contains("0 => CommercialRoleQuickActionPresentation.Primary", service, StringComparison.Ordinal);
		Assert.Contains("1 or 2 => CommercialRoleQuickActionPresentation.Secondary", service, StringComparison.Ordinal);
		Assert.Contains("_ => CommercialRoleQuickActionPresentation.Overflow", service, StringComparison.Ordinal);
		Assert.Contains("<controls:WorkflowActionBar", view, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding PrimaryQuickActions}\"", view, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding SecondaryQuickActions}\"", view, StringComparison.Ordinal);
		Assert.Contains("ItemsSource=\"{Binding OverflowQuickActions}\"", view, StringComparison.Ordinal);
		Assert.DoesNotContain("ItemsSource=\"{Binding QuickActions}\"", view, StringComparison.Ordinal);
	}

	[Fact]
	public void RoleCenterKpiChangeFeedbackRequiresAPreviouslyPresentedDifferentValue()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "CommercialRoleCenterViewModel.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "CommercialRoleCenterView.xaml"));

		Assert.Contains("_hasPresentedKpis &&", source, StringComparison.Ordinal);
		Assert.Contains("_presentedKpiValues.TryGetValue", source, StringComparison.Ordinal);
		Assert.Contains("!string.Equals(previousValue, kpi.Value, StringComparison.Ordinal)", source, StringComparison.Ordinal);
		Assert.Contains("Binding=\"{Binding HasChanged}\" Value=\"True\"", view, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind\" Value=\"StatusChange\"", view, StringComparison.Ordinal);
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

	[Fact]
	public void RoleCenterColumnProfilesCoverEveryPersonaWithFocusedDefaults()
	{
		foreach (var kind in Enum.GetValues<CommercialRoleCenterKind>())
		{
			var profile = CommercialRoleColumnProfiles.Get(kind);
			Assert.StartsWith("role-center.", profile.WorkspaceId, StringComparison.Ordinal);
			Assert.InRange(profile.VisibleColumnIds.Count, 5, 8);
			Assert.True(profile.IsVisible(CommercialRoleColumnProfiles.Reference));
		}
	}

	[Fact]
	public void ApprovalReceivableAndPayableProfilesUseTaskSpecificColumns()
	{
		var approval = CommercialRoleColumnProfiles.Get(CommercialRoleCenterKind.ApprovalInbox);
		Assert.True(approval.IsVisible(CommercialRoleColumnProfiles.Requester));
		Assert.True(approval.IsVisible(CommercialRoleColumnProfiles.Age));
		Assert.False(approval.IsVisible(CommercialRoleColumnProfiles.Currency));

		var receivables = CommercialRoleColumnProfiles.Get(CommercialRoleCenterKind.ReceivablesWorkspace);
		Assert.Equal("Customer", receivables.GetHeader(CommercialRoleColumnProfiles.Context, "Context"));
		Assert.Equal("Allocation", receivables.GetHeader(CommercialRoleColumnProfiles.StateDetail, "State"));

		var payables = CommercialRoleColumnProfiles.Get(CommercialRoleCenterKind.PayablesWorkspace);
		Assert.Equal("Supplier", payables.GetHeader(CommercialRoleColumnProfiles.Context, "Context"));
		Assert.Equal("Match", payables.GetHeader(CommercialRoleColumnProfiles.StateDetail, "State"));
	}

	[Fact]
	public void WarehouseProfilesStayCompactWithoutInventingBusinessState()
	{
		var receiving = CommercialRoleColumnProfiles.Get(CommercialRoleCenterKind.ReceivingWorkspace);
		var fulfillment = CommercialRoleColumnProfiles.Get(CommercialRoleCenterKind.FulfillmentWorkspace);

		Assert.Equal("Expected date", receiving.GetHeader(CommercialRoleColumnProfiles.Due, "Due"));
		Assert.Equal("Receipt state", receiving.GetHeader(CommercialRoleColumnProfiles.StateDetail, "State"));
		Assert.Equal("Requested date", fulfillment.GetHeader(CommercialRoleColumnProfiles.Due, "Due"));
		Assert.Equal("Picking / Packing", fulfillment.GetHeader(CommercialRoleColumnProfiles.StateDetail, "State"));
	}

	[Fact]
	public void FilteredSectionCarriesDistinctEmptyState()
	{
		var section = new CommercialRoleSection("Due Today", "Nothing is due today.", [], true);
		Assert.True(section.IsEmpty);
		Assert.Equal("No work items match the current filter.", section.DisplayEmptyText);
	}

	[Fact]
	public void ApprovalItemKeepsSingleDominantRowAction()
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

		Assert.True(item.CanApprove);
		Assert.False(item.OpenIsPrimary);
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
			null!,
			null!,
			null!,
			null!);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}

}
