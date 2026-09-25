// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ApplicationPermission
{
	DashboardView, InventoryView, InventoryManage, ItemsView, ItemsCreate, ItemsEdit, ItemsManage,
	StockMovementsView, StockMovementsCreate, StockMovementsPost, StockMovementsReverse, ReportsView, ReportsExport,
	StockTransfersView, StockTransfersCreate, StockTransfersEdit, StockTransfersPost, StockTransfersReverse,
	InventoryCountsView, InventoryCountsCreate, InventoryCountsEdit, InventoryCountsPost, InventoryCountsReverse,
	PurchasingView, PurchaseOrdersView, PurchaseOrdersCreate, PurchaseOrdersEdit, PurchaseOrdersSubmit, PurchaseOrdersApprove, PurchaseOrdersOrder, PurchaseOrdersClose,
	ReplenishmentView, ReplenishmentSuggestionsManage, ReplenishmentPoliciesManage,
	PurchaseRequisitionsView, PurchaseRequisitionsManage, PurchaseRequisitionsApprove, SupplierSourcingView, SupplierSourcingManage, SupplierSourcingConvert,
	GoodsReceiptsView, GoodsReceiptsCreate, GoodsReceiptsPost, GoodsReceiptsReverse,
	MaterialIssuesView, MaterialIssuesCreate, MaterialIssuesEdit, MaterialIssuesPost, MaterialIssuesReverse,
	MaterialReturnsView, MaterialReturnsCreate, MaterialReturnsEdit, MaterialReturnsPost, MaterialReturnsReverse,
	SupplierReturnsView, SupplierReturnsCreate, SupplierReturnsEdit, SupplierReturnsPost, SupplierReturnsReverse,
	SalesView, CustomersView, CustomersCreate, CustomersEdit,
	SalesCrmView, SalesCrmRecordsManage, SalesCrmManage, SalesCrmActivitiesView, SalesCrmActivitiesManage,
	SalesQuotesView, SalesQuotesCreate, SalesQuotesEdit, SalesQuotesSend, SalesQuotesConvert,
	SalesPricingView, SalesPricingManage,
	SubscriptionContractsView, SubscriptionContractsManage, SubscriptionBillingGenerate,
	SalesOrdersView, SalesOrdersCreate, SalesOrdersEdit, SalesOrdersSubmit, SalesOrdersApprove, SalesOrdersRelease, SalesOrdersCancel,
	ShipmentsView, ShipmentsCreate, ShipmentsEdit, ShipmentsPost, ShipmentsReverse,
	CustomerReturnsView, CustomerReturnsCreate, CustomerReturnsPost,
	SalesInvoicesView, SalesInvoicesCreate, SalesInvoicesPost, CreditNotesView, CreditNotesCreate, CreditNotesPost,
	FinanceView, FinanceManage, FinanceExchangeRatesView, FinanceExchangeRatesManage, FinancePeriodsView, FinancePeriodsManage,
	FinanceAccountingBooksView, FinanceAccountingBooksManage, FinanceTaxConfigurationView, FinanceTaxConfigurationManage,
	FinanceNumberSequencesView, FinanceNumberSequencesManage, FinanceGeneralLedgerView, FinanceGeneralLedgerPost, FinanceGeneralLedgerReverse,
	FinanceManualJournalsPost, FinancePostingProfilesView, FinancePostingProfilesManage,
	FinanceReceivablesView, FinanceReceivablesManage, FinanceReceivablePaymentsPost, FinanceReceivablePaymentsReverse,
	FinanceReceivableWriteOffsPost, FinanceReceivableWriteOffsReverse, FinanceDunningView, FinanceDunningManage,
	FinancePayablesView, FinancePayablesManage, FinanceSupplierInvoicesCreate, FinanceSupplierInvoicesSubmit, FinanceSupplierInvoicesApprove,
	FinanceSupplierMatchExceptionsApprove, FinanceSupplierInvoicesPost, FinanceSupplierInvoicesReverse, FinancePayablePaymentsPost, FinancePayablePaymentsReverse,
	FinanceInventoryAccountingView, FinanceInventoryAccountingManage,
	FinanceBankingView, FinanceBankingManage, FinanceBankStatementsCreate, FinanceBankReconciliationManage,
	FinancePaymentProposalsCreate, FinancePaymentProposalsApprove, FinancePaymentRunsPost, FinanceCashPositionView,
	FinanceSepaPaymentProfilesManage, FinanceSepaPaymentExportsCreate, FinanceSepaPaymentExportsExport, FinanceSepaPaymentExportsManage,
	FinanceFinancialReportingView, FinanceFinancialReportingManage, FinanceFinancialReportingExport, FinanceReportSnapshotsCreate,
	FinanceBudgetingView, FinanceBudgetingManage, FinanceBudgetingApprove, FinanceBudgetingLock,
	FinanceLocalizationView, FinanceLocalizationManage,
	FinanceFixedAssetsView, FinanceFixedAssetsConfigure, FinanceFixedAssetsManage, FinanceFixedAssetsDepreciationPost,
	ProjectsView, ProjectsManage, ProjectAttributionsManage, ProjectFinancialsView,
	SuppliersView, SuppliersManage, MasterDataView, MasterDataManage, ImportManage, UsersView, UsersManage, UserSessionsTerminate, RolesView, RolesManage,
	DatabaseView, DatabaseManage, AuditLogView, AuditLogExport, SecurityEventsView, SecurityEventsManage, SettingsView, SettingsManage, DocumentTemplatesView, DocumentTemplatesManage, AdministrationView, ApprovalPoliciesView, ApprovalPoliciesManage
}
