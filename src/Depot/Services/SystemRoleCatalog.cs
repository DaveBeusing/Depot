// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record SystemRoleDefinition(string Code, string Name, string Description, IReadOnlySet<ApplicationPermission> Permissions);

public static class SystemRoleCatalog
{
	public const string AdministratorCode = "ADMINISTRATOR";
	public const string PurchasingCode = "PURCHASING";
	public const string ApproverCode = "APPROVER";
	public const string WarehouseOperatorCode = "WAREHOUSE_OPERATOR";
	public const string SalesUserCode = "SALES_USER";
	public const string SalesManagerCode = "SALES_MANAGER";
	public const string FinanceCode = "FINANCE";
	public const string UserCode = "USER";
	public const string GoodsReceiverCode = "GOODS_RECEIVER";
	public const string FulfillmentOperatorCode = "FULFILLMENT_OPERATOR";
	public const string InventoryControllerCode = "INVENTORY_CONTROLLER";
	public const string AccountsReceivableCode = "ACCOUNTS_RECEIVABLE";
	public const string AccountsPayableCode = "ACCOUNTS_PAYABLE";
	public const string TreasuryCode = "TREASURY";
	public const string AccountantControllerCode = "ACCOUNTANT_CONTROLLER";
	public const string ManagementViewerCode = "MANAGEMENT_VIEWER";
	public const string AuditorComplianceCode = "AUDITOR_COMPLIANCE";
	public const string MasterDataManagerCode = "MASTER_DATA_MANAGER";
	public const string ApplicationAdministratorCode = "APPLICATION_ADMINISTRATOR";

	private static readonly IReadOnlySet<ApplicationPermission> CommonViewPermissions = Set(ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView, ApplicationPermission.ReportsView, ApplicationPermission.StockTransfersView, ApplicationPermission.InventoryCountsView);

	public static IReadOnlyList<SystemRoleDefinition> Definitions { get; } =
	[
		new(AdministratorCode, "Administrator", "Protected system role with every Depot permission.", PermissionCatalog.All),
		new(PurchasingCode, "Purchasing", "Creates and manages suppliers and purchase orders.", Union(CommonViewPermissions, ApplicationPermission.PurchasingView, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.PurchaseOrdersCreate, ApplicationPermission.PurchaseOrdersEdit, ApplicationPermission.PurchaseOrdersSubmit, ApplicationPermission.PurchaseOrdersOrder, ApplicationPermission.PurchaseOrdersClose, ApplicationPermission.SuppliersView, ApplicationPermission.SuppliersManage, ApplicationPermission.GoodsReceiptsView)),
		new(ApproverCode, "Approver", "Reviews purchase orders, supplier invoices and payment proposals while preserving creator/approver separation.", Union(CommonViewPermissions, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.PurchaseOrdersApprove, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesApprove, ApplicationPermission.FinanceBankingView, ApplicationPermission.FinancePaymentProposalsApprove)),
		new(WarehouseOperatorCode, "Warehouse Operator", "Operates warehouse documents and stock workflows.", Union(CommonViewPermissions, ApplicationPermission.PurchasingView, ApplicationPermission.InventoryManage, ApplicationPermission.StockMovementsCreate, ApplicationPermission.StockMovementsPost, ApplicationPermission.StockMovementsReverse, ApplicationPermission.StockTransfersCreate, ApplicationPermission.StockTransfersEdit, ApplicationPermission.StockTransfersPost, ApplicationPermission.StockTransfersReverse, ApplicationPermission.InventoryCountsCreate, ApplicationPermission.InventoryCountsEdit, ApplicationPermission.InventoryCountsPost, ApplicationPermission.InventoryCountsReverse, ApplicationPermission.GoodsReceiptsView, ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost, ApplicationPermission.GoodsReceiptsReverse, ApplicationPermission.MaterialIssuesView, ApplicationPermission.MaterialIssuesCreate, ApplicationPermission.MaterialIssuesEdit, ApplicationPermission.MaterialIssuesPost, ApplicationPermission.MaterialIssuesReverse, ApplicationPermission.MaterialReturnsView, ApplicationPermission.MaterialReturnsCreate, ApplicationPermission.MaterialReturnsEdit, ApplicationPermission.MaterialReturnsPost, ApplicationPermission.MaterialReturnsReverse, ApplicationPermission.SupplierReturnsView, ApplicationPermission.SupplierReturnsCreate, ApplicationPermission.SupplierReturnsEdit, ApplicationPermission.SupplierReturnsPost, ApplicationPermission.SupplierReturnsReverse, ApplicationPermission.SalesView, ApplicationPermission.SalesOrdersView, ApplicationPermission.ShipmentsView, ApplicationPermission.ShipmentsCreate, ApplicationPermission.ShipmentsEdit, ApplicationPermission.ShipmentsPost, ApplicationPermission.ShipmentsReverse, ApplicationPermission.CustomerReturnsView, ApplicationPermission.CustomerReturnsCreate, ApplicationPermission.CustomerReturnsPost)),
		new(SalesUserCode, "Sales User", "Creates customer records, quotes and sales orders and submits them for approval.", Union(CommonViewPermissions, ApplicationPermission.SalesView, ApplicationPermission.CustomersView, ApplicationPermission.CustomersCreate, ApplicationPermission.CustomersEdit, ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesQuotesCreate, ApplicationPermission.SalesQuotesEdit, ApplicationPermission.SalesQuotesSend, ApplicationPermission.SalesQuotesConvert, ApplicationPermission.SalesPricingView, ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersCreate, ApplicationPermission.SalesOrdersEdit, ApplicationPermission.SalesOrdersSubmit)),
		new(SalesManagerCode, "Sales Manager", "Manages quotes and pricing and approves and releases sales orders.", Union(CommonViewPermissions, ApplicationPermission.SalesView, ApplicationPermission.CustomersView, ApplicationPermission.CustomersCreate, ApplicationPermission.CustomersEdit, ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesQuotesCreate, ApplicationPermission.SalesQuotesEdit, ApplicationPermission.SalesQuotesSend, ApplicationPermission.SalesQuotesConvert, ApplicationPermission.SalesPricingView, ApplicationPermission.SalesPricingManage, ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersCreate, ApplicationPermission.SalesOrdersEdit, ApplicationPermission.SalesOrdersSubmit, ApplicationPermission.SalesOrdersApprove, ApplicationPermission.SalesOrdersRelease, ApplicationPermission.SalesOrdersCancel, ApplicationPermission.ShipmentsView, ApplicationPermission.CustomerReturnsView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView)),
		new(FinanceCode, "Finance", "Manages financial documents, inventory accounting, banking, reporting, localization, subledgers, Finance configuration and controlled General Ledger posting.", Union(CommonViewPermissions,
			ApplicationPermission.SalesView, ApplicationPermission.CustomersView, ApplicationPermission.SalesPricingView, ApplicationPermission.SalesOrdersView, ApplicationPermission.ShipmentsView, ApplicationPermission.CustomerReturnsView,
			ApplicationPermission.SalesInvoicesView, ApplicationPermission.SalesInvoicesCreate, ApplicationPermission.SalesInvoicesPost, ApplicationPermission.CreditNotesView, ApplicationPermission.CreditNotesCreate, ApplicationPermission.CreditNotesPost,
			ApplicationPermission.SuppliersView, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.GoodsReceiptsView,
			ApplicationPermission.FinanceView, ApplicationPermission.FinanceManage, ApplicationPermission.FinanceExchangeRatesView, ApplicationPermission.FinanceExchangeRatesManage, ApplicationPermission.FinancePeriodsView, ApplicationPermission.FinancePeriodsManage,
			ApplicationPermission.FinanceAccountingBooksView, ApplicationPermission.FinanceAccountingBooksManage, ApplicationPermission.FinanceTaxConfigurationView, ApplicationPermission.FinanceTaxConfigurationManage, ApplicationPermission.FinanceNumberSequencesView, ApplicationPermission.FinanceNumberSequencesManage,
			ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceGeneralLedgerReverse, ApplicationPermission.FinancePostingProfilesView, ApplicationPermission.FinancePostingProfilesManage,
			ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceReceivablesManage, ApplicationPermission.FinanceReceivablePaymentsPost, ApplicationPermission.FinanceReceivablePaymentsReverse, ApplicationPermission.FinanceDunningView, ApplicationPermission.FinanceDunningManage,
			ApplicationPermission.FinancePayablesView, ApplicationPermission.FinancePayablesManage, ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesSubmit, ApplicationPermission.FinanceSupplierInvoicesPost, ApplicationPermission.FinanceSupplierInvoicesReverse, ApplicationPermission.FinancePayablePaymentsPost, ApplicationPermission.FinancePayablePaymentsReverse,
			ApplicationPermission.FinanceInventoryAccountingView, ApplicationPermission.FinanceInventoryAccountingManage,
			ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceBankingManage, ApplicationPermission.FinanceBankStatementsCreate, ApplicationPermission.FinanceBankReconciliationManage, ApplicationPermission.FinancePaymentProposalsCreate, ApplicationPermission.FinancePaymentRunsPost, ApplicationPermission.FinanceCashPositionView,
			ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingManage, ApplicationPermission.FinanceFinancialReportingExport, ApplicationPermission.FinanceReportSnapshotsCreate,
			ApplicationPermission.FinanceLocalizationView, ApplicationPermission.FinanceLocalizationManage)),
		new(UserCode, "User", "Read-only access to standard operational views.", CommonViewPermissions),
		new(GoodsReceiverCode, "Goods Receiver", "Receives expected supplier deliveries without general Purchasing administration.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView,
			ApplicationPermission.PurchasingView, ApplicationPermission.PurchaseOrdersView,
			ApplicationPermission.GoodsReceiptsView, ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost, ApplicationPermission.GoodsReceiptsReverse)),
		new(FulfillmentOperatorCode, "Fulfillment Operator", "Executes picking, packing, shipping and customer returns for released Sales Orders.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView,
			ApplicationPermission.SalesOrdersView,
			ApplicationPermission.ShipmentsView, ApplicationPermission.ShipmentsCreate, ApplicationPermission.ShipmentsEdit, ApplicationPermission.ShipmentsPost, ApplicationPermission.ShipmentsReverse,
			ApplicationPermission.CustomerReturnsView, ApplicationPermission.CustomerReturnsCreate, ApplicationPermission.CustomerReturnsPost)),
		new(InventoryControllerCode, "Inventory Controller", "Controls inventory counts, transfers, stock corrections and inventory traceability workflows.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.InventoryView, ApplicationPermission.InventoryManage, ApplicationPermission.ItemsView,
			ApplicationPermission.StockMovementsView, ApplicationPermission.StockMovementsCreate, ApplicationPermission.StockMovementsPost, ApplicationPermission.StockMovementsReverse,
			ApplicationPermission.StockTransfersView, ApplicationPermission.StockTransfersCreate, ApplicationPermission.StockTransfersEdit, ApplicationPermission.StockTransfersPost, ApplicationPermission.StockTransfersReverse,
			ApplicationPermission.InventoryCountsView, ApplicationPermission.InventoryCountsCreate, ApplicationPermission.InventoryCountsEdit, ApplicationPermission.InventoryCountsPost, ApplicationPermission.InventoryCountsReverse,
			ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport)),
		new(AccountsReceivableCode, "Accounts Receivable", "Operates customer open items, receipts, allocations and dunning without Accounts Payable, Banking or General Ledger administration.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.CustomersView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView,
			ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceReceivablePaymentsPost, ApplicationPermission.FinanceReceivablePaymentsReverse,
			ApplicationPermission.FinanceDunningView, ApplicationPermission.FinanceDunningManage)),
		new(AccountsPayableCode, "Accounts Payable", "Processes supplier invoices and matching while keeping invoice approval and Treasury authority separate.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.PurchasingView, ApplicationPermission.SuppliersView, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.GoodsReceiptsView,
			ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesSubmit,
			ApplicationPermission.FinanceSupplierInvoicesPost, ApplicationPermission.FinanceSupplierInvoicesReverse)),
		new(TreasuryCode, "Treasury", "Operates bank statements, reconciliation, payment proposals, payment runs and cash position without supplier-invoice or General Ledger administration.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceBankingManage,
			ApplicationPermission.FinanceBankStatementsCreate, ApplicationPermission.FinanceBankReconciliationManage,
			ApplicationPermission.FinancePaymentProposalsCreate, ApplicationPermission.FinancePaymentRunsPost, ApplicationPermission.FinanceCashPositionView)),
		new(AccountantControllerCode, "Accountant / Controller", "Controls General Ledger, periods, posting profiles, inventory accounting, reconciliation and financial reporting; manual journal posting remains separately granted.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport,
			ApplicationPermission.FinanceView,
			ApplicationPermission.FinancePeriodsView, ApplicationPermission.FinancePeriodsManage,
			ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceGeneralLedgerReverse,
			ApplicationPermission.FinancePostingProfilesView, ApplicationPermission.FinancePostingProfilesManage,
			ApplicationPermission.FinanceInventoryAccountingView, ApplicationPermission.FinanceInventoryAccountingManage,
			ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceBankReconciliationManage,
			ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingManage, ApplicationPermission.FinanceFinancialReportingExport, ApplicationPermission.FinanceReportSnapshotsCreate)),
		new(ManagementViewerCode, "Management Viewer", "Read-only management access to operational and financial KPIs, reports and drilldowns.", Set(
			ApplicationPermission.DashboardView,
			ApplicationPermission.InventoryView, ApplicationPermission.ItemsView, ApplicationPermission.StockMovementsView, ApplicationPermission.StockTransfersView, ApplicationPermission.InventoryCountsView,
			ApplicationPermission.PurchasingView, ApplicationPermission.PurchaseOrdersView, ApplicationPermission.GoodsReceiptsView, ApplicationPermission.SupplierReturnsView, ApplicationPermission.SuppliersView,
			ApplicationPermission.SalesView, ApplicationPermission.CustomersView, ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesPricingView, ApplicationPermission.SalesOrdersView,
			ApplicationPermission.ShipmentsView, ApplicationPermission.CustomerReturnsView, ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView,
			ApplicationPermission.FinanceView, ApplicationPermission.FinanceExchangeRatesView, ApplicationPermission.FinancePeriodsView, ApplicationPermission.FinanceAccountingBooksView,
			ApplicationPermission.FinanceTaxConfigurationView, ApplicationPermission.FinanceNumberSequencesView, ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinancePostingProfilesView,
			ApplicationPermission.FinanceReceivablesView, ApplicationPermission.FinanceDunningView, ApplicationPermission.FinancePayablesView, ApplicationPermission.FinanceInventoryAccountingView,
			ApplicationPermission.FinanceBankingView, ApplicationPermission.FinanceCashPositionView, ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingExport,
			ApplicationPermission.FinanceLocalizationView, ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport)),
		new(AuditorComplianceCode, "Auditor / Compliance", "Read-only and export-oriented access to audit, security, user/role and relevant reporting evidence.", Set(
			ApplicationPermission.DashboardView, ApplicationPermission.ReportsView, ApplicationPermission.ReportsExport,
			ApplicationPermission.FinanceFinancialReportingView, ApplicationPermission.FinanceFinancialReportingExport, ApplicationPermission.FinanceLocalizationView,
			ApplicationPermission.UsersView, ApplicationPermission.RolesView,
			ApplicationPermission.AuditLogView, ApplicationPermission.AuditLogExport, ApplicationPermission.SecurityEventsView)),
		new(MasterDataManagerCode, "Master Data Manager", "Maintains item, customer, supplier, warehouse, location and reference master data without operational transaction posting.", Set(
			ApplicationPermission.DashboardView,
			ApplicationPermission.ItemsView, ApplicationPermission.ItemsCreate, ApplicationPermission.ItemsEdit, ApplicationPermission.ItemsManage,
			ApplicationPermission.CustomersView, ApplicationPermission.CustomersCreate, ApplicationPermission.CustomersEdit,
			ApplicationPermission.SuppliersView, ApplicationPermission.SuppliersManage,
			ApplicationPermission.MasterDataView, ApplicationPermission.MasterDataManage)),
		new(ApplicationAdministratorCode, "Application Administrator", "Administers users, roles, sessions, settings, database and security controls without implicit Sales, Warehouse or Finance posting authority.", Set(
			ApplicationPermission.DashboardView,
			ApplicationPermission.UsersView, ApplicationPermission.UsersManage, ApplicationPermission.UserSessionsTerminate,
			ApplicationPermission.RolesView, ApplicationPermission.RolesManage,
			ApplicationPermission.SettingsView, ApplicationPermission.SettingsManage,
			ApplicationPermission.DatabaseView, ApplicationPermission.DatabaseManage,
			ApplicationPermission.AuditLogView, ApplicationPermission.AuditLogExport,
			ApplicationPermission.SecurityEventsView, ApplicationPermission.SecurityEventsManage,
			ApplicationPermission.AdministrationView))
	];

	private static IReadOnlySet<ApplicationPermission> Set(params ApplicationPermission[] permissions) => permissions.ToHashSet();
	private static IReadOnlySet<ApplicationPermission> Union(IEnumerable<ApplicationPermission> existing, params ApplicationPermission[] additional) => existing.Concat(additional).ToHashSet();
}
