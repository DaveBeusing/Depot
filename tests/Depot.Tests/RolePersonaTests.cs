// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class RbacTestsPersonaProfiles
{
	private static readonly string[] LegacyRoleCodes =
	[
		SystemRoleCatalog.AdministratorCode,
		SystemRoleCatalog.PurchasingCode,
		SystemRoleCatalog.ApproverCode,
		SystemRoleCatalog.WarehouseOperatorCode,
		SystemRoleCatalog.SalesUserCode,
		SystemRoleCatalog.SalesManagerCode,
		SystemRoleCatalog.FinanceCode,
		SystemRoleCatalog.UserCode
	];

	private static readonly string[] PersonaRoleCodes =
	[
		SystemRoleCatalog.GoodsReceiverCode,
		SystemRoleCatalog.FulfillmentOperatorCode,
		SystemRoleCatalog.InventoryControllerCode,
		SystemRoleCatalog.AccountsReceivableCode,
		SystemRoleCatalog.AccountsPayableCode,
		SystemRoleCatalog.TreasuryCode,
		SystemRoleCatalog.AccountantControllerCode,
		SystemRoleCatalog.ManagementViewerCode,
		SystemRoleCatalog.AuditorComplianceCode,
		SystemRoleCatalog.MasterDataManagerCode,
		SystemRoleCatalog.ApplicationAdministratorCode
	];

	[Fact]
	public void CatalogContainsLegacyAndPersonaRoles()
	{
		var codes = SystemRoleCatalog.Definitions.Select(role => role.Code).ToHashSet(StringComparer.Ordinal);
		Assert.All(LegacyRoleCodes, code => Assert.Contains(code, codes));
		Assert.All(PersonaRoleCodes, code => Assert.Contains(code, codes));
		Assert.Equal(LegacyRoleCodes.Length + PersonaRoleCodes.Length, SystemRoleCatalog.Definitions.Count);
	}

	[Theory]
	[MemberData(nameof(PersonaPermissions))]
	public void PersonaPermissionSetsAreExact(string code, ApplicationPermission[] expected)
	{
		var role = Role(code);
		Assert.Equal(expected.OrderBy(value => value).ToArray(), role.Permissions.OrderBy(value => value).ToArray());
	}

	[Fact]
	public void ManagementAndAuditorRolesContainNoMutationPermissions()
	{
		var mutationPermissions = PermissionCatalog.Definitions
			.Where(definition => definition.Action is not "View" and not "Export")
			.Select(definition => definition.Permission)
			.ToHashSet();

		Assert.Empty(Role(SystemRoleCatalog.ManagementViewerCode).Permissions.Intersect(mutationPermissions));
		Assert.Empty(Role(SystemRoleCatalog.AuditorComplianceCode).Permissions.Intersect(mutationPermissions));
	}

	[Fact]
	public void ApplicationAdministratorHasNoOperationalPostingAuthority()
	{
		var role = Role(SystemRoleCatalog.ApplicationAdministratorCode);
		var operationalActions = new HashSet<string>(StringComparer.Ordinal)
		{
			"Post", "Reverse", "Approve", "Submit", "Release", "Order", "Close", "Convert", "Send"
		};
		var operationalPermissions = PermissionCatalog.Definitions
			.Where(definition => operationalActions.Contains(definition.Action))
			.Select(definition => definition.Permission)
			.ToHashSet();

		Assert.Empty(role.Permissions.Intersect(operationalPermissions));
		Assert.DoesNotContain(ApplicationPermission.PurchasingView, role.Permissions);
		Assert.DoesNotContain(ApplicationPermission.SalesView, role.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceView, role.Permissions);
	}

	[Fact]
	public void AccountsReceivableAccountsPayableAndTreasuryRemainSeparated()
	{
		var receivables = Role(SystemRoleCatalog.AccountsReceivableCode).Permissions;
		var payables = Role(SystemRoleCatalog.AccountsPayableCode).Permissions;
		var treasury = Role(SystemRoleCatalog.TreasuryCode).Permissions;

		Assert.Contains(ApplicationPermission.FinanceReceivablesView, receivables);
		Assert.DoesNotContain(ApplicationPermission.FinancePayablesView, receivables);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierInvoicesCreate, receivables);
		Assert.DoesNotContain(ApplicationPermission.FinanceBankingManage, receivables);

		Assert.Contains(ApplicationPermission.PurchasingView, payables);
		Assert.Contains(ApplicationPermission.FinancePayablesView, payables);
		Assert.DoesNotContain(ApplicationPermission.FinanceReceivablesView, payables);
		Assert.DoesNotContain(ApplicationPermission.FinanceReceivablePaymentsPost, payables);
		Assert.DoesNotContain(ApplicationPermission.FinanceBankingManage, payables);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierInvoicesApprove, payables);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierMatchExceptionsApprove, payables);

		Assert.Contains(ApplicationPermission.FinanceBankingView, treasury);
		Assert.Contains(ApplicationPermission.FinancePaymentRunsPost, treasury);
		Assert.DoesNotContain(ApplicationPermission.FinancePaymentProposalsApprove, treasury);
		Assert.DoesNotContain(ApplicationPermission.FinancePayablesView, treasury);
		Assert.DoesNotContain(ApplicationPermission.FinanceSupplierInvoicesCreate, treasury);
		Assert.DoesNotContain(ApplicationPermission.FinanceGeneralLedgerPost, treasury);
		Assert.DoesNotContain(ApplicationPermission.FinancePostingProfilesManage, treasury);
	}

	[Fact]
	public void AccountantControllerKeepsManualJournalsSeparate()
	{
		var role = Role(SystemRoleCatalog.AccountantControllerCode).Permissions;
		Assert.Contains(ApplicationPermission.FinanceGeneralLedgerPost, role);
		Assert.Contains(ApplicationPermission.FinancePostingProfilesManage, role);
		Assert.Contains(ApplicationPermission.FinanceInventoryAccountingManage, role);
		Assert.Contains(ApplicationPermission.FinanceFinancialReportingView, role);
		Assert.DoesNotContain(ApplicationPermission.FinanceManualJournalsPost, role);
		Assert.DoesNotContain(ApplicationPermission.FinanceExchangeRatesManage, role);
		Assert.DoesNotContain(ApplicationPermission.FinanceAccountingBooksManage, role);
		Assert.DoesNotContain(ApplicationPermission.FinanceTaxConfigurationManage, role);
		Assert.DoesNotContain(ApplicationPermission.FinanceNumberSequencesManage, role);
		Assert.DoesNotContain(ApplicationPermission.FinanceLocalizationManage, role);
	}

	[Fact]
	public void PersonaViewPermissionsRemainBoundedForShellNavigation()
	{
		var goodsReceiver = Role(SystemRoleCatalog.GoodsReceiverCode).Permissions;
		Assert.Contains(ApplicationPermission.PurchasingView, goodsReceiver);
		Assert.Contains(ApplicationPermission.GoodsReceiptsView, goodsReceiver);
		Assert.DoesNotContain(ApplicationPermission.PurchaseOrdersCreate, goodsReceiver);

		var fulfillment = Role(SystemRoleCatalog.FulfillmentOperatorCode).Permissions;
		Assert.Contains(ApplicationPermission.SalesOrdersView, fulfillment);
		Assert.Contains(ApplicationPermission.ShipmentsView, fulfillment);
		Assert.DoesNotContain(ApplicationPermission.SalesOrdersRelease, fulfillment);

		var inventory = Role(SystemRoleCatalog.InventoryControllerCode).Permissions;
		Assert.Contains(ApplicationPermission.StockTransfersView, inventory);
		Assert.Contains(ApplicationPermission.InventoryCountsView, inventory);
		Assert.DoesNotContain(ApplicationPermission.FinanceInventoryAccountingManage, inventory);

		var applicationAdministrator = Role(SystemRoleCatalog.ApplicationAdministratorCode).Permissions;
		Assert.Contains(ApplicationPermission.UsersView, applicationAdministrator);
		Assert.Contains(ApplicationPermission.RolesView, applicationAdministrator);
		Assert.DoesNotContain(ApplicationPermission.SalesOrdersView, applicationAdministrator);
		Assert.DoesNotContain(ApplicationPermission.FinancePayablesView, applicationAdministrator);
	}

	[Fact]
	public async Task CatalogSeedingFailsClosedOnCustomRoleCodeCollision()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-role-persona-collision-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			var data = new DatabaseAccess(factory);
			await data.ExecuteAsync(
				"UPDATE Roles SET Name = $Name, Description = $Description, IsSystem = 0 WHERE Code = $Code;",
				CancellationToken.None,
				new DatabaseParameter("$Name", "Custom Goods Receiver"),
				new DatabaseParameter("$Description", "Pre-upgrade custom role"),
				new DatabaseParameter("$Code", SystemRoleCatalog.GoodsReceiverCode));

			var exception = Assert.Throws<InvalidOperationException>(() => new DepotDatabase(factory).Initialize());
			Assert.Contains(SystemRoleCatalog.GoodsReceiverCode, exception.Message, StringComparison.Ordinal);
			Assert.Contains("reserved system role code", exception.Message, StringComparison.OrdinalIgnoreCase);

			Assert.Equal("Custom Goods Receiver", Convert.ToString(await data.ExecuteScalarAsync(
				"SELECT Name FROM Roles WHERE Code = $Code;",
				CancellationToken.None,
				new DatabaseParameter("$Code", SystemRoleCatalog.GoodsReceiverCode))));
			Assert.Equal(0L, Convert.ToInt64(await data.ExecuteScalarAsync(
				"SELECT IsSystem FROM Roles WHERE Code = $Code;",
				CancellationToken.None,
				new DatabaseParameter("$Code", SystemRoleCatalog.GoodsReceiverCode))));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[Fact]
	public async Task CatalogSeedingIsIdempotentAndRepositoryExposesAllPersonas()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-role-personas-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			var data = new DatabaseAccess(factory);

			var rolesBefore = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM Roles;", CancellationToken.None));
			var mappingsBefore = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM RolePermissions;", CancellationToken.None));

			new DepotDatabase(factory).Initialize();

			var rolesAfter = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM Roles;", CancellationToken.None));
			var mappingsAfter = Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM RolePermissions;", CancellationToken.None));
			Assert.Equal(rolesBefore, rolesAfter);
			Assert.Equal(mappingsBefore, mappingsAfter);

			var page = await new RoleRepository(data).SearchPageAsync(null, 1, 100, CancellationToken.None);
			Assert.All(PersonaRoleCodes, code => Assert.Contains(page.Items, role => role.Code == code && role.IsSystem && role.IsActive));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	public static TheoryData<string, ApplicationPermission[]> PersonaPermissions => new()
	{
		{
			SystemRoleCatalog.GoodsReceiverCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView,
				ApplicationPermission.PurchasingView, ApplicationPermission.PurchaseOrdersView,
				ApplicationPermission.GoodsReceiptsView, ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost, ApplicationPermission.GoodsReceiptsReverse,
				ApplicationPermission.SupplierReturnsView, ApplicationPermission.SupplierReturnsCreate, ApplicationPermission.SupplierReturnsEdit, ApplicationPermission.SupplierReturnsPost, ApplicationPermission.SupplierReturnsReverse
			}
		},
		{
			SystemRoleCatalog.FulfillmentOperatorCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView,
				ApplicationPermission.SalesOrdersView,
				ApplicationPermission.ShipmentsView, ApplicationPermission.ShipmentsCreate, ApplicationPermission.ShipmentsEdit, ApplicationPermission.ShipmentsPost, ApplicationPermission.ShipmentsReverse,
				ApplicationPermission.CustomerReturnsView, ApplicationPermission.CustomerReturnsCreate, ApplicationPermission.CustomerReturnsPost
			}
		},
		{
			SystemRoleCatalog.InventoryControllerCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.InventoryManage, ApplicationPermission.ItemsView,
				ApplicationPermission.StockMovementsView, ApplicationPermission.StockMovementsCreate, ApplicationPermission.StockMovementsPost, ApplicationPermission.StockMovementsReverse,
				ApplicationPermission.StockTransfersView, ApplicationPermission.StockTransfersCreate, ApplicationPermission.StockTransfersEdit, ApplicationPermission.StockTransfersPost, ApplicationPermission.StockTransfersReverse,
				ApplicationPermission.InventoryCountsView, ApplicationPermission.InventoryCountsCreate, ApplicationPermission.InventoryCountsEdit, ApplicationPermission.InventoryCountsPost, ApplicationPermission.InventoryCountsReverse,
				ApplicationPermission.MaterialIssuesView, ApplicationPermission.MaterialIssuesCreate, ApplicationPermission.MaterialIssuesEdit, ApplicationPermission.MaterialIssuesPost, ApplicationPermission.MaterialIssuesReverse,
				ApplicationPermission.MaterialReturnsView, ApplicationPermission.MaterialReturnsCreate, ApplicationPermission.MaterialReturnsEdit, ApplicationPermission.MaterialReturnsPost, ApplicationPermission.MaterialReturnsReverse,
				ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport
			}
		},
		{
			SystemRoleCatalog.AccountsReceivableCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.CustomersView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView,
				ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceReceivablePaymentsPost, ApplicationPermission.FinanceReceivablePaymentsReverse,
				ApplicationPermission.FinanceDunningView, ApplicationPermission.FinanceDunningManage,
				ApplicationPermission.ProjectsView, ApplicationPermission.ProjectFinancialsView
			}
		},
		{
			SystemRoleCatalog.AccountsPayableCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.PurchasingView, ApplicationPermission.SuppliersView, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.GoodsReceiptsView,
				ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesSubmit,
				ApplicationPermission.FinanceSupplierInvoicesPost, ApplicationPermission.FinanceSupplierInvoicesReverse,
				ApplicationPermission.ProjectsView, ApplicationPermission.ProjectAttributionsManage
			}
		},
		{
			SystemRoleCatalog.TreasuryCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceBankingManage,
				ApplicationPermission.FinanceBankStatementsCreate, ApplicationPermission.FinanceBankReconciliationManage,
				ApplicationPermission.FinancePaymentProposalsCreate, ApplicationPermission.FinancePaymentRunsPost, ApplicationPermission.FinanceCashPositionView,
				ApplicationPermission.FinanceSepaPaymentProfilesManage, ApplicationPermission.FinanceSepaPaymentExportsCreate, ApplicationPermission.FinanceSepaPaymentExportsExport, ApplicationPermission.FinanceSepaPaymentExportsManage
			}
		},
		{
			SystemRoleCatalog.AccountantControllerCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport,
				ApplicationPermission.FinanceView,
				ApplicationPermission.FinancePeriodsView, ApplicationPermission.FinancePeriodsManage,
				ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceGeneralLedgerReverse,
				ApplicationPermission.FinancePostingProfilesView, ApplicationPermission.FinancePostingProfilesManage,
				ApplicationPermission.FinanceInventoryAccountingView, ApplicationPermission.FinanceInventoryAccountingManage,
				ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceBankReconciliationManage,
				ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingManage, ApplicationPermission.FinanceFinancialReportingExport, ApplicationPermission.FinanceReportSnapshotsCreate,
				ApplicationPermission.FinanceFixedAssetsView, ApplicationPermission.FinanceFixedAssetsConfigure, ApplicationPermission.FinanceFixedAssetsManage, ApplicationPermission.FinanceFixedAssetsDepreciationPost,
				ApplicationPermission.ProjectsView, ApplicationPermission.ProjectAttributionsManage, ApplicationPermission.ProjectFinancialsView
			}
		},
		{
			SystemRoleCatalog.ManagementViewerCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView,
				ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView, ApplicationPermission.StockTransfersView, ApplicationPermission.InventoryCountsView,
				ApplicationPermission.PurchasingView, ApplicationPermission.PurchaseRequisitionsView, ApplicationPermission.SupplierSourcingView, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.GoodsReceiptsView, ApplicationPermission.SupplierReturnsView, ApplicationPermission.SuppliersView,
				ApplicationPermission.SalesView, ApplicationPermission.CustomersView, ApplicationPermission.SalesCrmView, ApplicationPermission.SalesCrmActivitiesView, ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesPricingView, ApplicationPermission.SalesOrdersView,
				ApplicationPermission.ShipmentsView, ApplicationPermission.CustomerReturnsView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView,
				ApplicationPermission.FinanceView, ApplicationPermission.FinanceExchangeRatesView, ApplicationPermission.FinancePeriodsView, ApplicationPermission.FinanceAccountingBooksView,
				ApplicationPermission.FinanceTaxConfigurationView, ApplicationPermission.FinanceNumberSequencesView, ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinancePostingProfilesView,
				ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceDunningView, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceInventoryAccountingView,
				ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceCashPositionView, ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingExport,
				ApplicationPermission.FinanceLocalizationView, ApplicationPermission.FinanceFixedAssetsView, ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport,
				ApplicationPermission.ProjectsView, ApplicationPermission.ProjectFinancialsView
			}
		},
		{
			SystemRoleCatalog.AuditorComplianceCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView, ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport,
				ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingExport, ApplicationPermission.FinanceLocalizationView, ApplicationPermission.FinanceFixedAssetsView,
				ApplicationPermission.UsersView, ApplicationPermission.RolesView,
				ApplicationPermission.AuditLogView, ApplicationPermission.AuditLogExport, ApplicationPermission.SecurityEventsView
			}
		},
		{
			SystemRoleCatalog.MasterDataManagerCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView,
				ApplicationPermission.ItemsView, ApplicationPermission.ItemsCreate, ApplicationPermission.ItemsEdit, ApplicationPermission.ItemsManage,
				ApplicationPermission.CustomersView, ApplicationPermission.CustomersCreate, ApplicationPermission.CustomersEdit,
				ApplicationPermission.SuppliersView, ApplicationPermission.SuppliersManage,
				ApplicationPermission.MasterDataView, ApplicationPermission.MasterDataManage
			}
		},
		{
			SystemRoleCatalog.ApplicationAdministratorCode,
			new ApplicationPermission[]
			{
				ApplicationPermission.DashboardView,
				ApplicationPermission.UsersView, ApplicationPermission.UsersManage, ApplicationPermission.UserSessionsTerminate,
				ApplicationPermission.RolesView, ApplicationPermission.RolesManage,
				ApplicationPermission.SettingsView, ApplicationPermission.SettingsManage,
				ApplicationPermission.DatabaseView, ApplicationPermission.DatabaseManage,
				ApplicationPermission.AuditLogView, ApplicationPermission.AuditLogExport,
				ApplicationPermission.SecurityEventsView, ApplicationPermission.SecurityEventsManage,
				ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage,
				ApplicationPermission.AdministrationView,
				ApplicationPermission.ApprovalPoliciesView, ApplicationPermission.ApprovalPoliciesManage
			}
		}
	};

	private static SystemRoleDefinition Role(string code) =>
		SystemRoleCatalog.Definitions.Single(role => string.Equals(role.Code, code, StringComparison.Ordinal));
}
