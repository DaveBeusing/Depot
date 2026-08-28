// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record SystemRoleDefinition(
	string Code,
	string Name,
	string Description,
	IReadOnlySet<ApplicationPermission> Permissions);

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

	private static readonly IReadOnlySet<ApplicationPermission> CommonViewPermissions = Set(
		ApplicationPermission.DashboardView,
		ApplicationPermission.InventoryView,
		ApplicationPermission.ItemsView,
		ApplicationPermission.StockMovementsView,
		ApplicationPermission.ReportsView,
		ApplicationPermission.StockTransfersView,
		ApplicationPermission.InventoryCountsView);

	public static IReadOnlyList<SystemRoleDefinition> Definitions { get; } =
	[
		new(AdministratorCode, "Administrator", "Protected system role with every Depot permission.", PermissionCatalog.All),
		new(PurchasingCode, "Purchasing", "Creates and manages suppliers and purchase orders.", Union(CommonViewPermissions,
			ApplicationPermission.PurchasingView,
			ApplicationPermission.PurchaseOrdersView, ApplicationPermission.PurchaseOrdersCreate,
			ApplicationPermission.PurchaseOrdersEdit, ApplicationPermission.PurchaseOrdersSubmit,
			ApplicationPermission.PurchaseOrdersOrder, ApplicationPermission.PurchaseOrdersClose,
			ApplicationPermission.SuppliersView, ApplicationPermission.SuppliersManage,
			ApplicationPermission.GoodsReceiptsView)),
		new(ApproverCode, "Approver", "Reviews purchase orders while preserving creator/approver separation.", Union(CommonViewPermissions,
			ApplicationPermission.PurchaseOrdersView, ApplicationPermission.PurchaseOrdersApprove)),
		new(WarehouseOperatorCode, "Warehouse Operator", "Operates warehouse documents and stock workflows.", Union(CommonViewPermissions,
			ApplicationPermission.PurchasingView,
			ApplicationPermission.InventoryManage,
			ApplicationPermission.StockMovementsCreate, ApplicationPermission.StockMovementsPost, ApplicationPermission.StockMovementsReverse,
			ApplicationPermission.StockTransfersCreate, ApplicationPermission.StockTransfersEdit, ApplicationPermission.StockTransfersPost, ApplicationPermission.StockTransfersReverse,
			ApplicationPermission.InventoryCountsCreate, ApplicationPermission.InventoryCountsEdit, ApplicationPermission.InventoryCountsPost, ApplicationPermission.InventoryCountsReverse,
			ApplicationPermission.GoodsReceiptsView, ApplicationPermission.GoodsReceiptsCreate, ApplicationPermission.GoodsReceiptsPost, ApplicationPermission.GoodsReceiptsReverse,
			ApplicationPermission.MaterialIssuesView, ApplicationPermission.MaterialIssuesCreate, ApplicationPermission.MaterialIssuesEdit, ApplicationPermission.MaterialIssuesPost, ApplicationPermission.MaterialIssuesReverse,
			ApplicationPermission.MaterialReturnsView, ApplicationPermission.MaterialReturnsCreate, ApplicationPermission.MaterialReturnsEdit, ApplicationPermission.MaterialReturnsPost, ApplicationPermission.MaterialReturnsReverse,
			ApplicationPermission.SupplierReturnsView, ApplicationPermission.SupplierReturnsCreate, ApplicationPermission.SupplierReturnsEdit, ApplicationPermission.SupplierReturnsPost, ApplicationPermission.SupplierReturnsReverse,
			ApplicationPermission.SalesView, ApplicationPermission.SalesOrdersView,
			ApplicationPermission.ShipmentsView, ApplicationPermission.ShipmentsCreate, ApplicationPermission.ShipmentsEdit, ApplicationPermission.ShipmentsPost, ApplicationPermission.ShipmentsReverse,
			ApplicationPermission.CustomerReturnsView, ApplicationPermission.CustomerReturnsCreate, ApplicationPermission.CustomerReturnsPost)),
		new(SalesUserCode, "Sales User", "Creates customer records, quotes and sales orders and submits them for approval.", Union(CommonViewPermissions,
			ApplicationPermission.SalesView,
			ApplicationPermission.CustomersView, ApplicationPermission.CustomersCreate, ApplicationPermission.CustomersEdit,
			ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesQuotesCreate, ApplicationPermission.SalesQuotesEdit, ApplicationPermission.SalesQuotesSend, ApplicationPermission.SalesQuotesConvert,
			ApplicationPermission.SalesPricingView,
			ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersCreate, ApplicationPermission.SalesOrdersEdit,
			ApplicationPermission.SalesOrdersSubmit)),
		new(SalesManagerCode, "Sales Manager", "Manages quotes and pricing and approves and releases sales orders.", Union(CommonViewPermissions,
			ApplicationPermission.SalesView,
			ApplicationPermission.CustomersView, ApplicationPermission.CustomersCreate, ApplicationPermission.CustomersEdit,
			ApplicationPermission.SalesQuotesView, ApplicationPermission.SalesQuotesCreate, ApplicationPermission.SalesQuotesEdit, ApplicationPermission.SalesQuotesSend, ApplicationPermission.SalesQuotesConvert,
			ApplicationPermission.SalesPricingView, ApplicationPermission.SalesPricingManage,
			ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesOrdersCreate, ApplicationPermission.SalesOrdersEdit,
			ApplicationPermission.SalesOrdersSubmit, ApplicationPermission.SalesOrdersApprove,
			ApplicationPermission.SalesOrdersRelease, ApplicationPermission.SalesOrdersCancel,
			ApplicationPermission.ShipmentsView,
			ApplicationPermission.CustomerReturnsView,
			ApplicationPermission.SalesInvoicesView, ApplicationPermission.CreditNotesView)),
		new(FinanceCode, "Finance", "Manages customer financial documents, Finance configuration and controlled General Ledger posting.", Union(CommonViewPermissions,
			ApplicationPermission.SalesView,
			ApplicationPermission.CustomersView,
			ApplicationPermission.SalesPricingView,
			ApplicationPermission.SalesOrdersView,
			ApplicationPermission.ShipmentsView,
			ApplicationPermission.CustomerReturnsView,
			ApplicationPermission.SalesInvoicesView, ApplicationPermission.SalesInvoicesCreate, ApplicationPermission.SalesInvoicesPost,
			ApplicationPermission.CreditNotesView, ApplicationPermission.CreditNotesCreate, ApplicationPermission.CreditNotesPost,
			ApplicationPermission.FinanceView, ApplicationPermission.FinanceManage,
			ApplicationPermission.FinanceExchangeRatesView, ApplicationPermission.FinanceExchangeRatesManage,
			ApplicationPermission.FinancePeriodsView, ApplicationPermission.FinancePeriodsManage,
			ApplicationPermission.FinanceAccountingBooksView, ApplicationPermission.FinanceAccountingBooksManage,
			ApplicationPermission.FinanceTaxConfigurationView, ApplicationPermission.FinanceTaxConfigurationManage,
			ApplicationPermission.FinanceNumberSequencesView, ApplicationPermission.FinanceNumberSequencesManage,
			ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceGeneralLedgerReverse,
			ApplicationPermission.FinancePostingProfilesView, ApplicationPermission.FinancePostingProfilesManage)),
		new(UserCode, "User", "Read-only access to standard operational views.", CommonViewPermissions)
	];

	private static IReadOnlySet<ApplicationPermission> Set(params ApplicationPermission[] permissions) => permissions.ToHashSet();
	private static IReadOnlySet<ApplicationPermission> Union(IEnumerable<ApplicationPermission> existing, params ApplicationPermission[] additional) => existing.Concat(additional).ToHashSet();
}
