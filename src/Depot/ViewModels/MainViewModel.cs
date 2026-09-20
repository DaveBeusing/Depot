// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.Services.Help;
using Depot.Services.Import;
using Depot.ViewModels.Administration;
using Depot.ViewModels.Help;

namespace Depot.ViewModels;

public sealed class MainViewModel : BaseViewModel, IDisposable
{
	private readonly IAuthorizationService _authorization;
	private readonly SessionService _session;
	private readonly INotificationNavigationService _notificationNavigation;
	private readonly IFileDialogService _fileDialogs;
	private readonly CommercialRoleCenterService _commercialRoleCenters;
	private readonly WelcomeViewModel _welcome;
	private readonly Lazy<DashboardViewModel> _dashboard;
	private readonly Lazy<CommercialRoleCenterViewModel> _salesWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _salesControlRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _buyerWorkbenchRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _approvalInboxRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _receivingWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _fulfillmentWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _inventoryControlWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _receivablesWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _payablesWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _treasuryWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _accountingControlWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _managementCockpitRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _auditComplianceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _masterDataWorkspaceRoleCenter;
	private readonly Lazy<CommercialRoleCenterViewModel> _applicationAdministrationRoleCenter;
	private readonly Lazy<InventoryViewModel> _inventory;
	private readonly Lazy<ItemsViewModel> _items;
	private readonly Lazy<MovementsViewModel> _movements;
	private readonly Lazy<StockTransfersViewModel> _stockTransfers;
	private readonly Lazy<InventoryCountsViewModel> _inventoryCounts;
	private readonly Lazy<MaterialIssuesViewModel> _materialIssues;
	private readonly Lazy<MaterialReturnsViewModel> _materialReturns;
	private readonly Lazy<SupplierReturnsViewModel> _supplierReturns;
	private readonly Lazy<ProcurementViewModel> _procurement;
	private readonly Lazy<PurchaseOverviewViewModel> _purchaseOverview;
	private readonly Lazy<PurchaseOrdersPageViewModel> _purchaseOrdersPage;
	private readonly Lazy<GoodsReceiptsPageViewModel> _goodsReceiptsPage;
	private readonly Lazy<PurchaseOrderApprovalsViewModel> _purchaseOrderApprovals;
	private readonly Lazy<SalesViewModel> _salesSearch;
	private readonly Lazy<SalesOverviewViewModel> _salesOverview;
	private readonly Lazy<SalesQuotesViewModel> _salesQuotes;
	private readonly Lazy<SalesPricingViewModel> _salesPricing;
	private readonly Lazy<CustomersViewModel> _salesCustomers;
	private readonly Lazy<SalesOrdersViewModel> _salesOrders;
	private readonly Lazy<SalesApprovalsViewModel> _salesApprovals;
	private readonly Lazy<ShippingViewModel> _salesShipping;
	private readonly Lazy<SalesInvoicesViewModel> _salesInvoices;
	private readonly Lazy<FinanceReceivablesViewModel> _financeReceivables;
	private readonly Lazy<FinancePayablesViewModel> _financePayables;
	private readonly Lazy<FinanceInventoryAccountingViewModel> _financeInventoryAccounting;
	private readonly Lazy<FinanceBankingViewModel> _financeBanking;
	private readonly Lazy<FinanceFinancialReportingViewModel> _financeFinancialReporting;
	private readonly Lazy<FinancePostingFlowDesignerViewModel> _financePostingFlowDesigner;
	private readonly Lazy<ReportsViewModel> _reports;
	private readonly Lazy<ImportViewModel> _import;
	private readonly Lazy<AdministrationViewModel> _administration;
	private readonly Lazy<HelpViewModel> _help;
	private readonly Lazy<NotificationCenterViewModel> _notificationCenter;
	private readonly NavigationLoadState _notificationLoadState = new();
	private CancellationTokenSource? _navigationCancellation;
	private ShellNavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;
	private bool _isNavigationExpanded;
	private BaseViewModel? _viewModelBeforeHelp;
	private BaseViewModel? _viewModelBeforeNotifications;
	private bool _disposed;

	public MainViewModel(
		ItemService itemService,
		StockService stockService,
		DashboardService dashboardService,
		MyWorkService myWorkService,
		CommercialRoleCenterService commercialRoleCenterService,
		MovementService movementService,
		ReportService reportService,
		FinanceGeneralLedgerService financeGeneralLedgerService,
		FinanceAccountsReceivableService financeReceivablesService,
		FinanceAccountsPayableService financePayablesService,
		FinanceInventoryAccountingService financeInventoryAccountingService,
		FinanceInventoryCostingService financeInventoryCostingService,
		FinanceInventoryMovementAccountingService financeInventoryMovementAccountingService,
		FinanceBankingService financeBankingService,
		FinanceFinancialReportingService financeFinancialReportingService,
		PurposeService purposeService,
		ReasonCodeService reasonCodeService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService,
		SupplierCategoryService supplierCategoryService,
		SupplierService supplierService,
		SupplierItemService supplierItemService,
		PurchaseOrderService purchaseOrderService,
		PurchaseOrderApprovalService purchaseOrderApprovalService,
		PurchaseOrderHistoryService purchaseOrderHistoryService,
		GoodsReceiptService goodsReceiptService,
		StockTransferService stockTransferService,
		InventoryCountService inventoryCountService,
		MaterialIssueService materialIssueService,
		MaterialReturnService materialReturnService,
		SupplierReturnService supplierReturnService,
		SalesServices salesServices,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		UserService userService,
		RoleService roleService,
		IAuthorizationService authorizationService,
		SessionService sessionService,
		ImportService importService,
		IFileDialogService fileDialogService,
		SettingsService settingsService,
		ConnectionStatusService connectionStatusService,
		DatabaseConnectionTester databaseConnectionTester,
		DatabaseManagementService databaseManagementService,
		AuditLogService auditLogService,
		UserSessionAdministrationService userSessionAdministrationService,
		SecurityEventService securityEventService,
		ApplicationInformationService applicationInformationService,
		IHelpService helpService,
		HelpMarkdownRenderer helpRenderer,
		INotificationService notificationService,
		INotificationNavigationService notificationNavigationService)
	{
		_authorization = authorizationService;
		_session = sessionService;
		_notificationNavigation = notificationNavigationService;
		_fileDialogs = fileDialogService;
		_commercialRoleCenters = commercialRoleCenterService;
		ConnectionStatus = connectionStatusService;
		NotificationSummaryViewModel = new NotificationSummaryViewModel(notificationService);
		LogoutCommand = new RelayCommand(Logout);
		ToggleNavigationCommand = new RelayCommand(ToggleNavigation);
		HelpCommand = new RelayCommand(() => _ = OpenHelpAsync());
		NotificationCommand = new RelayCommand(() => _ = OpenNotificationsAsync());
		_welcome = new WelcomeViewModel(CurrentUserDisplayName, DateTime.Now);


		_dashboard = new(() => new DashboardViewModel(dashboardService, myWorkService, authorizationService));
		_salesWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.SalesWorkspace));
		_salesControlRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.SalesControlCenter));
		_buyerWorkbenchRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.BuyerWorkbench));
		_approvalInboxRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.ApprovalInbox));
		_receivingWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.ReceivingWorkspace));
		_fulfillmentWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.FulfillmentWorkspace));
		_inventoryControlWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.InventoryControlWorkspace));
		_receivablesWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.ReceivablesWorkspace));
		_payablesWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.PayablesWorkspace));
		_treasuryWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.TreasuryWorkspace));
		_accountingControlWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.AccountingControlWorkspace));
		_managementCockpitRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.ManagementCockpit));
		_auditComplianceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.AuditComplianceCenter));
		_masterDataWorkspaceRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.MasterDataWorkspace));
		_applicationAdministrationRoleCenter = new(() => new CommercialRoleCenterViewModel(commercialRoleCenterService, CommercialRoleCenterKind.ApplicationAdministrationCenter));
		_inventory = new(() => new InventoryViewModel(stockService));
		_items = new(() => new ItemsViewModel(itemService, manufacturerService, categoryService, unitOfMeasureService, packagingService, salesServices.ItemCosts));
		_movements = new(() => new MovementsViewModel(movementService, reasonCodeService, fileDialogService, MarkInventoryPagesStale));
		_stockTransfers = new(() => new StockTransfersViewModel(stockTransferService, warehouseService, fileDialogService, reasonCodeService));
		_inventoryCounts = new(() => new InventoryCountsViewModel(inventoryCountService, warehouseService, fileDialogService, reasonCodeService));
		_materialIssues = new(() => new MaterialIssuesViewModel(materialIssueService, reasonCodeService, fileDialogService));
		_materialReturns = new(() => new MaterialReturnsViewModel(materialReturnService, reasonCodeService, fileDialogService));
		_supplierReturns = new(() => new SupplierReturnsViewModel(supplierReturnService, supplierService, reasonCodeService, fileDialogService));
		_procurement = new(() => new ProcurementViewModel(purchaseOrderService, purchaseOrderHistoryService, goodsReceiptService, supplierService, itemService, fileDialogService, reasonCodeService, MarkPurchasingPagesStale, MarkInventoryPagesStale, salesServices.Timeline, OpenWorkflowTimelineItemAsync));
		_purchaseOverview = new(() => new PurchaseOverviewViewModel(purchaseOrderService));
		_purchaseOrdersPage = new(() => new PurchaseOrdersPageViewModel(_procurement.Value));
		_goodsReceiptsPage = new(() => new GoodsReceiptsPageViewModel(_procurement.Value));
		_purchaseOrderApprovals = new(() => new PurchaseOrderApprovalsViewModel(purchaseOrderApprovalService, fileDialogService));
		_salesSearch = new(() =>
		{
			var workspace = new SalesViewModel(salesServices.Customers, salesServices.Orders, salesServices.Shipments, salesServices.Invoices, salesServices.Items, salesServices.Pricing, salesServices.Authorization, fileDialogService, salesServices.Documents);
			SalesViewModelCreated?.Invoke(workspace);
			return workspace;
		});
		_salesOverview = new(() => new SalesOverviewViewModel(_salesSearch.Value));
		_salesQuotes = new(() => new SalesQuotesViewModel(salesServices.Quotes, salesServices.Pricing, salesServices.Customers, salesServices.Items, fileDialogService, salesServices.Documents));
		_salesPricing = new(() => new SalesPricingViewModel(salesServices.Pricing, salesServices.Customers, salesServices.Items, categoryService, manufacturerService, salesServices.PriceListGeneration));
		_salesCustomers = new(() => new CustomersViewModel(_salesSearch.Value, salesServices.Customers, salesServices.Pricing));
		_salesOrders = new(() => new SalesOrdersViewModel(_salesSearch.Value, salesServices.Pricing, salesServices.Timeline, OpenWorkflowTimelineItemAsync));
		_salesApprovals = new(() => new SalesApprovalsViewModel(_salesSearch.Value));
		_salesShipping = new(() => new ShippingViewModel(_salesSearch.Value, salesServices.Packing, fileDialogService, salesServices.Documents, salesServices.Timeline, OpenWorkflowTimelineItemAsync));
		_salesInvoices = new(() => new SalesInvoicesViewModel(_salesSearch.Value, salesServices.Invoices, fileDialogService, salesServices.Documents, salesServices.Email, salesServices.Timeline, OpenWorkflowTimelineItemAsync));
		_financePostingFlowDesigner = new(() => new FinancePostingFlowDesignerViewModel(financeGeneralLedgerService));
		_financeReceivables = new(() => new FinanceReceivablesViewModel(financeReceivablesService));
		_financePayables = new(() => new FinancePayablesViewModel(financePayablesService, salesServices.Timeline, OpenWorkflowTimelineItemAsync));
		_financeInventoryAccounting = new(() => new FinanceInventoryAccountingViewModel(financeInventoryAccountingService, financeInventoryCostingService, financeInventoryMovementAccountingService));
		_financeBanking = new(() => new FinanceBankingViewModel(financeBankingService));
		_financeFinancialReporting = new(() => new FinanceFinancialReportingViewModel(financeFinancialReportingService, fileDialogService));
		_reports = new(() => new ReportsViewModel(reportService, fileDialogService));
		_import = new(() => new ImportViewModel(importService, fileDialogService));
		_administration = new(() => new AdministrationViewModel(_import.Value, itemService, purposeService, reasonCodeService, manufacturerService, categoryService, unitOfMeasureService, packagingService, supplierCategoryService, supplierService, supplierItemService, warehouseService, storageLocationService, userService, roleService, authorizationService, settingsService, connectionStatusService, databaseConnectionTester, databaseManagementService, auditLogService, userSessionAdministrationService, securityEventService, fileDialogService, applicationInformationService));
		_help = new(() => CreateHelpViewModel(helpService, helpRenderer));
		_notificationCenter = new(() => CreateNotificationCenterViewModel(notificationService, notificationNavigationService));

		notificationNavigationService.SetNavigationHandler(NavigateToNotificationAsync);
		BuildNavigation();
		CurrentViewModel = _welcome;
	}

	public ObservableCollection<ShellNavigationItem> NavigationItems { get; } = [];
	public IEnumerable<ShellNavigationItem> PrimaryNavigationItems => NavigationItems.Where(item => item.IsPrimaryNavigationVisible);
	public RelayCommand LogoutCommand { get; }
	public RelayCommand ToggleNavigationCommand { get; }
	public RelayCommand HelpCommand { get; }
	public RelayCommand NotificationCommand { get; }
	public ConnectionStatusService ConnectionStatus { get; }
	public NotificationSummaryViewModel NotificationSummaryViewModel { get; }
	public event EventHandler? LogoutRequested;
	internal event Action<SalesViewModel>? SalesViewModelCreated;
	internal SalesViewModel? CreatedSalesViewModel => _salesSearch.IsValueCreated ? _salesSearch.Value : null;

	public WelcomeViewModel WelcomeViewModel => _welcome;
	public DashboardViewModel DashboardViewModel => _dashboard.Value;
	public InventoryViewModel InventoryViewModel => _inventory.Value;
	public ItemsViewModel ItemsViewModel => _items.Value;
	public MovementsViewModel MovementsViewModel => _movements.Value;
	public StockTransfersViewModel StockTransfersViewModel => _stockTransfers.Value;
	public InventoryCountsViewModel InventoryCountsViewModel => _inventoryCounts.Value;
	public MaterialIssuesViewModel MaterialIssuesViewModel => _materialIssues.Value;
	public MaterialReturnsViewModel MaterialReturnsViewModel => _materialReturns.Value;
	public SupplierReturnsViewModel SupplierReturnsViewModel => _supplierReturns.Value;
	public ProcurementViewModel ProcurementViewModel => _procurement.Value;
	public PurchaseOverviewViewModel PurchaseOverviewViewModel => _purchaseOverview.Value;
	public PurchaseOrdersPageViewModel PurchaseOrdersPageViewModel => _purchaseOrdersPage.Value;
	public GoodsReceiptsPageViewModel GoodsReceiptsPageViewModel => _goodsReceiptsPage.Value;
	public PurchaseOrderApprovalsViewModel PurchaseOrderApprovalsViewModel => _purchaseOrderApprovals.Value;
	public SalesViewModel SalesViewModel => _salesSearch.Value;
	public SalesOverviewViewModel SalesOverviewViewModel => _salesOverview.Value;
	public SalesQuotesViewModel SalesQuotesViewModel => _salesQuotes.Value;
	public SalesPricingViewModel SalesPricingViewModel => _salesPricing.Value;
	public CustomersViewModel CustomersViewModel => _salesCustomers.Value;
	public SalesOrdersViewModel SalesOrdersViewModel => _salesOrders.Value;
	public SalesApprovalsViewModel SalesApprovalsViewModel => _salesApprovals.Value;
	public ShippingViewModel ShippingViewModel => _salesShipping.Value;
	public SalesInvoicesViewModel SalesInvoicesViewModel => _salesInvoices.Value;
	public FinanceReceivablesViewModel FinanceReceivablesViewModel => _financeReceivables.Value;
	public FinancePayablesViewModel FinancePayablesViewModel => _financePayables.Value;
	public FinanceInventoryAccountingViewModel FinanceInventoryAccountingViewModel => _financeInventoryAccounting.Value;
	public FinanceBankingViewModel FinanceBankingViewModel => _financeBanking.Value;
	public FinanceFinancialReportingViewModel FinanceFinancialReportingViewModel => _financeFinancialReporting.Value;
	public ReportsViewModel ReportsViewModel => _reports.Value;
	public ImportViewModel ImportViewModel => _import.Value;
	public AdministrationViewModel AdministrationViewModel => _administration.Value;
	public HelpViewModel HelpViewModel => _help.Value;
	public NotificationCenterViewModel NotificationCenterViewModel => _notificationCenter.Value;

	public ShellNavigationItem? SelectedNavigationItem { get => _selectedNavigationItem; set { if (value is null || _selectedNavigationItem == value) return; _ = NavigateAsync(value); } }
	public BaseViewModel? CurrentViewModel { get => _currentViewModel; private set { if (_currentViewModel == value) return; _currentViewModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsHelpOpen)); } }
	public bool IsHelpOpen => _help.IsValueCreated && CurrentViewModel == _help.Value;
	public bool IsNavigationExpanded { get => _isNavigationExpanded; private set { if (_isNavigationExpanded == value) return; _isNavigationExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(NavigationPaneWidth)); OnPropertyChanged(nameof(NavigationToggleLabel)); } }
	public double NavigationPaneWidth => IsNavigationExpanded ? 220d : 52d;
	public string NavigationToggleLabel => IsNavigationExpanded ? "Collapse navigation" : "Expand navigation";
	public bool ShowSelectedContextNavigation => SelectedNavigationItem?.ShowContextNavigation == true;
	public string CurrentUserDisplayName => _authorization.CurrentUser?.DisplayName ?? string.Empty;
	public string CurrentUserRole => _authorization.CurrentUser is { Roles.Count: > 0 } user ? string.Join(", ", user.Roles.Select(role => role.Name)) : "No active role";
	public string CurrentHelpTopicId { get { if (CurrentViewModel is ShellModuleViewModel module && module.SelectedPage is { } page) { if (page.IsContentCreated && page.Content is AdministrationViewModel administration) return administration.HelpTopicId; return page.HelpTopicId; } return SelectedNavigationItem?.HelpTopicId ?? HelpService.FallbackTopicId; } }

	public bool ConfirmDiscardChanges(BaseViewModel? viewModel) { if (!UnsavedChangesGuard.TryGet(viewModel, out var changes) || changes is null) return true; var confirmed = _fileDialogs.Confirm(new ConfirmationDialogRequest("Discard unsaved changes?", $"The current {changes.Name} contains unsaved changes. Discard them and continue?", true)); if (confirmed) changes.Discard(); return confirmed; }
	public bool ConfirmDiscardChanges(ShellNavigationItem item) => !item.IsContentCreated || ConfirmDiscardChanges(item.Content);

	public async Task NavigateAsync(ShellNavigationItem target, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!ReferenceEquals(target, _selectedNavigationItem) && !ConfirmDiscardChanges(CurrentViewModel)) { OnPropertyChanged(nameof(SelectedNavigationItem)); return; }
		var navigation = BeginNavigation(cancellationToken); SetSelectedNavigationItem(target); CurrentViewModel = target.Content;
		try { await target.ActivateAsync(navigation.Token); } catch (OperationCanceledException) when (navigation.IsCancellationRequested) { } catch (Exception exception) { FailOperation(exception, $"{target.Name} could not be loaded"); }
	}
	public async Task RefreshCurrentAsync(CancellationToken cancellationToken = default) { var selected = SelectedNavigationItem; if (selected is null) return; var navigation = BeginNavigation(cancellationToken); try { await selected.RefreshAsync(navigation.Token); } catch (OperationCanceledException) when (navigation.IsCancellationRequested) { } }
	public async Task OpenHelpAsync(string? topicId = null, CancellationToken cancellationToken = default) { var help = HelpViewModel; if (CurrentViewModel != help && !ConfirmDiscardChanges(CurrentViewModel)) return; var targetTopicId = topicId ?? CurrentHelpTopicId; if (CurrentViewModel != help) _viewModelBeforeHelp = CurrentViewModel; CancelNavigationLoad(); CurrentViewModel = help; await help.OpenAsync(targetTopicId, cancellationToken); }
	public async Task OpenNotificationsAsync(CancellationToken cancellationToken = default) { var notifications = NotificationCenterViewModel; if (CurrentViewModel != notifications && !ConfirmDiscardChanges(CurrentViewModel)) return; if (CurrentViewModel != notifications) _viewModelBeforeNotifications = CurrentViewModel; CancelNavigationLoad(); CurrentViewModel = notifications; await _notificationLoadState.ActivateAsync(notifications.LoadAsync, cancellationToken); }
	public void ActivateShell() => NotificationSummaryViewModel.Activate();
	public void SetApplicationActive(bool isActive) { NotificationSummaryViewModel.SetApplicationActive(isActive); if (_notificationCenter.IsValueCreated) _notificationCenter.Value.SetApplicationActive(isActive); }

	public async Task OpenSalesQuickItemAsync(SalesQuickOpenItem item, CancellationToken cancellationToken = default)
	{
		var (moduleName, pageName, workspace) = item.Kind switch { SalesQuickOpenKind.Customer => ("Sales", "Customers", CustomersViewModel.Workspace), SalesQuickOpenKind.SalesOrder => ("Sales", "Sales Orders", SalesOrdersViewModel.Workspace), SalesQuickOpenKind.Shipment or SalesQuickOpenKind.CustomerReturn => ("Warehouse", "Shipping", ShippingViewModel.Workspace), SalesQuickOpenKind.Invoice or SalesQuickOpenKind.CreditNote => ("Sales", "Invoices", SalesInvoicesViewModel.Workspace), _ => ("Sales", "Overview", SalesOverviewViewModel.Workspace) };
		await NavigateToModulePageAsync(moduleName, pageName, cancellationToken); await workspace.OpenQuickItemAsync(item, cancellationToken);
	}

	public async Task OpenWorkflowTimelineItemAsync(WorkflowTimelineItem item, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (!item.CanNavigate || !ConfirmDiscardChanges(CurrentViewModel)) return;
		var route = new ShellRoute(item.RouteId!);
		switch (item.Kind)
		{
			case WorkflowTimelineKind.SalesQuote:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await SalesQuotesViewModel.OpenQuoteAsync(item.EntityId, cancellationToken);
				break;
			case WorkflowTimelineKind.SalesOrder:
			case WorkflowTimelineKind.SalesApproval:
			case WorkflowTimelineKind.ReservationRelease:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, item.EntityId, item.DisplayNumber, item.Title), cancellationToken);
				break;
			case WorkflowTimelineKind.Shipment:
			case WorkflowTimelineKind.ShipmentReversal:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Shipment, item.EntityId, item.DisplayNumber, item.Title), cancellationToken);
				break;
			case WorkflowTimelineKind.CustomerReturn:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.CustomerReturn, item.EntityId, item.DisplayNumber, item.Title), cancellationToken);
				break;
			case WorkflowTimelineKind.SalesInvoice:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Invoice, item.EntityId, item.DisplayNumber, item.Title), cancellationToken);
				break;
			case WorkflowTimelineKind.SalesCreditNote:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.CreditNote, item.EntityId, item.DisplayNumber, item.Title), cancellationToken);
				break;
			case WorkflowTimelineKind.Receivable:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinanceReceivablesViewModel.OpenOpenItemAsync(item.EntityId, cancellationToken);
				break;
			case WorkflowTimelineKind.PurchaseOrder:
			case WorkflowTimelineKind.PurchaseApproval:
			case WorkflowTimelineKind.PurchaseOrdered:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await ProcurementViewModel.OpenOrderAsync(item.EntityId, cancellationToken);
				break;
			case WorkflowTimelineKind.GoodsReceipt:
			case WorkflowTimelineKind.GoodsReceiptReversal:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await ProcurementViewModel.OpenReceiptAsync(item.EntityId, cancellationToken);
				break;
			case WorkflowTimelineKind.SupplierInvoice:
			case WorkflowTimelineKind.SupplierMatch:
			case WorkflowTimelineKind.SupplierApproval:
			case WorkflowTimelineKind.SupplierDocumentReversal:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinancePayablesViewModel.OpenDocumentAsync(item.EntityId, cancellationToken);
				break;
			case WorkflowTimelineKind.Payable:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinancePayablesViewModel.OpenOpenItemAsync(item.EntityId, cancellationToken);
				break;
			default:
				await this.NavigateToRouteAsync(route, cancellationToken);
				break;
		}
	}

	public async Task OpenMyWorkItemAsync(MyWorkItem item, CancellationToken cancellationToken = default)
	{
		if (!ConfirmDiscardChanges(CurrentViewModel)) return;
		var route = new ShellRoute(item.RouteId);
		switch (item.Kind)
		{
			case MyWorkItemKind.PurchaseOrder:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await ProcurementViewModel.OpenOrderAsync(item.EntityId, cancellationToken);
				break;
			case MyWorkItemKind.PurchaseOrderApproval:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await PurchaseOrderApprovalsViewModel.OpenApprovalAsync(item.EntityId, cancellationToken);
				break;
			case MyWorkItemKind.SalesOrder:
			{
				var quickItem = new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty);
				if (route == ShellRoutes.Warehouse.Shipping)
				{
					await this.NavigateToRouteAsync(route, cancellationToken);
					await ShippingViewModel.Workspace.OpenQuickItemAsync(quickItem, cancellationToken);
				}
				else await OpenSalesQuickItemAsync(quickItem, cancellationToken);
				break;
			}
			case MyWorkItemKind.SalesOrderApproval:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await SalesApprovalsViewModel.Workspace.OpenQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case MyWorkItemKind.Shipment:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Shipment, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case MyWorkItemKind.InventoryCount:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await InventoryCountsViewModel.OpenCountAsync(item.EntityId, cancellationToken);
				break;
			case MyWorkItemKind.ReceivableOpenItem:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinanceReceivablesViewModel.OpenOpenItemAsync(item.EntityId, cancellationToken);
				break;
			case MyWorkItemKind.SupplierDocument:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinancePayablesViewModel.OpenDocumentAsync(item.EntityId, cancellationToken);
				break;
			case MyWorkItemKind.BankStatementLine:
				await this.NavigateToRouteAsync(route, cancellationToken);
				FinanceBankingViewModel.SelectedStatementLine = FinanceBankingViewModel.UnreconciledLines.FirstOrDefault(line => line.Id == item.EntityId);
				break;
			case MyWorkItemKind.PaymentRun:
				await this.NavigateToRouteAsync(route, cancellationToken);
				FinanceBankingViewModel.SelectedPaymentRun = FinanceBankingViewModel.PaymentRuns.FirstOrDefault(run => run.Id == item.EntityId);
				break;
			default:
				await this.NavigateToRouteAsync(route, cancellationToken);
				break;
		}
	}

	public async Task OpenCommercialRoleItemAsync(CommercialRoleItem item, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (!ConfirmDiscardChanges(CurrentViewModel)) return;
		var route = new ShellRoute(item.RouteId);
		switch (item.Kind)
		{
			case CommercialRoleItemKind.Customer:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Customer, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case CommercialRoleItemKind.SalesQuote:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await SalesQuotesViewModel.OpenQuoteAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.SalesOrder:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case CommercialRoleItemKind.Shipment:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Shipment, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case CommercialRoleItemKind.CustomerReturn:
				await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.CustomerReturn, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case CommercialRoleItemKind.SalesOrderApproval:
				await this.NavigateToRouteAsync(ShellRoutes.Approvals.Sales, cancellationToken);
				await SalesApprovalsViewModel.Workspace.OpenQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, item.EntityId, item.DisplayNumber, item.Context ?? string.Empty), cancellationToken);
				break;
			case CommercialRoleItemKind.PurchaseOrder:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await ProcurementViewModel.OpenOrderAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.PurchaseOrderApproval:
				await this.NavigateToRouteAsync(ShellRoutes.Approvals.Purchasing, cancellationToken);
				await PurchaseOrderApprovalsViewModel.OpenApprovalAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.SupplierReturn:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await SupplierReturnsViewModel.OpenReturnAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.GoodsReceipt:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await ProcurementViewModel.OpenOrderAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.InventoryCount:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await InventoryCountsViewModel.OpenCountAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.StockTransfer:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await StockTransfersViewModel.OpenTransferAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.MaterialIssue:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await MaterialIssuesViewModel.OpenIssueAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.MaterialReturn:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await MaterialReturnsViewModel.OpenReturnAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.ReceivableOpenItem:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinanceReceivablesViewModel.OpenOpenItemAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.PayableOpenItem:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinancePayablesViewModel.OpenOpenItemAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.SupplierInvoice:
				await this.NavigateToRouteAsync(route, cancellationToken);
				await FinancePayablesViewModel.OpenDocumentAsync(item.EntityId, cancellationToken);
				break;
			case CommercialRoleItemKind.PaymentProposal:
				await this.NavigateToRouteAsync(route, cancellationToken);
				FinanceBankingViewModel.SelectedPaymentRun = FinanceBankingViewModel.PaymentRuns.FirstOrDefault(run => run.Id == item.EntityId);
				break;
			case CommercialRoleItemKind.BankStatement:
				await this.NavigateToRouteAsync(route, cancellationToken);
				FinanceBankingViewModel.SelectedStatement = FinanceBankingViewModel.Statements.FirstOrDefault(statement => statement.Id == item.EntityId);
				break;
			case CommercialRoleItemKind.BankStatementLine:
				await this.NavigateToRouteAsync(route, cancellationToken);
				FinanceBankingViewModel.SelectedStatementLine = FinanceBankingViewModel.UnreconciledLines.FirstOrDefault(line => line.Id == item.EntityId);
				break;
			default:
				await this.NavigateToRouteAsync(route, cancellationToken);
				break;
		}
	}

	public async Task ExecuteCommercialRoleActionAsync(CommercialRoleQuickAction action, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(action);
		switch (action.ActionId)
		{
			case "sales.new-customer":
				await this.NavigateToRouteAsync(ShellRoutes.Sales.Customers, cancellationToken);
				CustomersViewModel.Workspace.NewCustomerCommand.Execute(null);
				break;
			case "sales.new-quote":
				await this.NavigateToRouteAsync(ShellRoutes.Sales.Quotes, cancellationToken);
				SalesQuotesViewModel.NewQuoteCommand.Execute(null);
				break;
			case "sales.new-order":
				await this.NavigateToRouteAsync(ShellRoutes.Sales.Orders, cancellationToken);
				SalesOrdersViewModel.Workspace.NewOrderCommand.Execute(null);
				break;
			case "purchasing.new-order":
				await this.NavigateToRouteAsync(ShellRoutes.Purchasing.PurchaseOrders, cancellationToken);
				ProcurementViewModel.NewOrderCommand.Execute(null);
				break;
			case "purchasing.open-supplier":
				await NavigateToModulePageAsync("Administration", "Suppliers", cancellationToken);
				break;
			case "purchasing.receive":
			case "receiving.receive":
				await this.NavigateToRouteAsync(ShellRoutes.Purchasing.GoodsReceipts, cancellationToken);
				break;
			case "receiving.open-order":
				await this.NavigateToRouteAsync(ShellRoutes.Purchasing.PurchaseOrders, cancellationToken);
				break;
			case "receiving.new-supplier-return":
				await this.NavigateToRouteAsync(ShellRoutes.Purchasing.SupplierReturns, cancellationToken);
				SupplierReturnsViewModel.NewReturnCommand.Execute(null);
				break;
			case "inventory.new-count":
				await this.NavigateToRouteAsync(ShellRoutes.Warehouse.InventoryCounts, cancellationToken);
				InventoryCountsViewModel.NewCommand.Execute(null);
				break;
			case "inventory.new-transfer":
				await this.NavigateToRouteAsync(ShellRoutes.Warehouse.Transfers, cancellationToken);
				StockTransfersViewModel.NewTransferCommand.Execute(null);
				break;
			case "inventory.new-issue":
				await this.NavigateToRouteAsync(ShellRoutes.Warehouse.MaterialIssues, cancellationToken);
				MaterialIssuesViewModel.NewIssueCommand.Execute(null);
				break;
			case "inventory.new-return":
				await this.NavigateToRouteAsync(ShellRoutes.Warehouse.MaterialReturns, cancellationToken);
				MaterialReturnsViewModel.NewReturnCommand.Execute(null);
				break;
			case "ap.new-invoice":
				await this.NavigateToRouteAsync(ShellRoutes.Finance.Payables, cancellationToken);
				FinancePayablesViewModel.NewDraftCommand.Execute(null);
				break;
			case "admin.master-data": await NavigateToAdministrationSectionAsync(AdministrationSection.MasterData, cancellationToken); break;
			case "admin.warehouses": await NavigateToAdministrationSectionAsync(AdministrationSection.Warehouses, cancellationToken); break;
			case "admin.suppliers": await NavigateToAdministrationSectionAsync(AdministrationSection.Suppliers, cancellationToken); break;
			case "admin.users": await NavigateToAdministrationSectionAsync(AdministrationSection.Users, cancellationToken); break;
			case "admin.sessions": await NavigateToAdministrationSectionAsync(AdministrationSection.UserSessions, cancellationToken); break;
			case "admin.security": await NavigateToAdministrationSectionAsync(AdministrationSection.SecurityCenter, cancellationToken); break;
			case "admin.roles": await NavigateToAdministrationSectionAsync(AdministrationSection.Roles, cancellationToken); break;
			case "admin.company": await NavigateToAdministrationSectionAsync(AdministrationSection.Company, cancellationToken); break;
			case "admin.import": await NavigateToAdministrationSectionAsync(AdministrationSection.Import, cancellationToken); break;
			case "admin.audit-log": await NavigateToAdministrationSectionAsync(AdministrationSection.AuditLog, cancellationToken); break;
			case "admin.database": await NavigateToAdministrationSectionAsync(AdministrationSection.Database, cancellationToken); break;
			default:
				await this.NavigateToRouteAsync(new ShellRoute(action.RouteId), cancellationToken);
				break;
		}
	}

	private void BuildNavigation()
	{
		AddDirect(ApplicationPermission.DashboardView, "Dashboard", Icons.Dashboard, () => _dashboard.Value, (viewModel, token) => viewModel.LoadAsync(token), HelpService.FallbackTopicId, "Home");
		var roleCenterPages = new List<SecondaryNavigationItem>();
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.SalesWorkspace)) roleCenterPages.Add(new("Sales Workspace", () => _salesWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.SalesWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.SalesControlCenter)) roleCenterPages.Add(new("Sales Control Center", () => _salesControlRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.SalesControl));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.BuyerWorkbench)) roleCenterPages.Add(new("Buyer Workbench", () => _buyerWorkbenchRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.BuyerWorkbench));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.ApprovalInbox)) roleCenterPages.Add(new("Approval Inbox", () => _approvalInboxRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.ApprovalInbox));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.ReceivingWorkspace)) roleCenterPages.Add(new("Receiving Workspace", () => _receivingWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.ReceivingWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.FulfillmentWorkspace)) roleCenterPages.Add(new("Fulfillment Workspace", () => _fulfillmentWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.FulfillmentWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.InventoryControlWorkspace)) roleCenterPages.Add(new("Inventory Control Workspace", () => _inventoryControlWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.InventoryControlWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.ReceivablesWorkspace)) roleCenterPages.Add(new("Receivables Workspace", () => _receivablesWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.ReceivablesWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.PayablesWorkspace)) roleCenterPages.Add(new("Payables Workspace", () => _payablesWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.PayablesWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.TreasuryWorkspace)) roleCenterPages.Add(new("Treasury Workspace", () => _treasuryWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.TreasuryWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.AccountingControlWorkspace)) roleCenterPages.Add(new("Accounting & Control Workspace", () => _accountingControlWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.AccountingControlWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.ManagementCockpit)) roleCenterPages.Add(new("Management Cockpit", () => _managementCockpitRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.ManagementCockpit));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.AuditComplianceCenter)) roleCenterPages.Add(new("Audit & Compliance Center", () => _auditComplianceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.AuditComplianceCenter));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.MasterDataWorkspace)) roleCenterPages.Add(new("Master Data Workspace", () => _masterDataWorkspaceRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.MasterDataWorkspace));
		if (_commercialRoleCenters.CanAccess(CommercialRoleCenterKind.ApplicationAdministrationCenter)) roleCenterPages.Add(new("Application Administration Center", () => _applicationAdministrationRoleCenter.Value, (viewModel, token) => ((CommercialRoleCenterViewModel)viewModel).LoadAsync(token), HelpService.FallbackTopicId, route: ShellRoutes.RoleCenters.ApplicationAdministrationCenter));
		AddModule("Role Centers", Icons.Work, "Role-oriented starting points for commercial, warehouse, finance, management, compliance, master data and administration work.", roleCenterPages, isPrimaryNavigationVisible: false, showContextNavigation: false);

		var inventoryPages = new List<SecondaryNavigationItem>();
		AddPage(inventoryPages, ApplicationPermission.InventoryView, "Overview", () => _inventory.Value, (viewModel, token) => viewModel.LoadAsync(token), "inventory.overview"); AddPage(inventoryPages, ApplicationPermission.ItemsView, "Items", () => _items.Value, (viewModel, token) => viewModel.LoadItemsAsync(token), "inventory.items"); AddPage(inventoryPages, ApplicationPermission.StockMovementsView, "Movements", () => _movements.Value, (viewModel, token) => viewModel.LoadAsync(token), "inventory.movements"); AddModule("Inventory", Icons.Inventory, "Monitor stock, items, and immutable inventory movements.", inventoryPages);
		var salesPages = new List<SecondaryNavigationItem>();
		AddPage(salesPages, ApplicationPermission.SalesView, "Overview", () => _salesOverview.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.overview"); AddPage(salesPages, ApplicationPermission.SalesQuotesView, "Quotes", () => _salesQuotes.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.quotes"); AddPage(salesPages, ApplicationPermission.SalesPricingView, "Pricing", () => _salesPricing.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.pricing"); AddPage(salesPages, ApplicationPermission.CustomersView, "Customers", () => _salesCustomers.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.customers"); AddPage(salesPages, ApplicationPermission.SalesOrdersView, "Sales Orders", () => _salesOrders.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.orders"); AddPage(salesPages, ApplicationPermission.SalesInvoicesView, "Invoices", () => _salesInvoices.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.invoices"); AddModule("Sales", Icons.Sales, "Manage quotes, pricing, customers, sales orders, and invoicing.", salesPages);
		if (_authorization.HasPermission(ApplicationPermission.PurchasingView)) { var purchasingPages = new List<SecondaryNavigationItem>(); AddPage(purchasingPages, ApplicationPermission.PurchaseOrdersView, "Overview", () => _purchaseOverview.Value, (viewModel, token) => viewModel.LoadAsync(token), "purchasing.overview"); AddPage(purchasingPages, ApplicationPermission.PurchaseOrdersView, "Purchase Orders", () => _purchaseOrdersPage.Value, (viewModel, token) => viewModel.LoadAsync(token), "purchasing.purchase-orders", () => _procurement.Value.Section = ProcurementSection.PurchaseOrders); AddPage(purchasingPages, ApplicationPermission.GoodsReceiptsView, "Goods Receipts", () => _goodsReceiptsPage.Value, (viewModel, token) => viewModel.LoadAsync(token), "purchasing.goods-receipts", () => _procurement.Value.Section = ProcurementSection.GoodsReceipts); AddPage(purchasingPages, ApplicationPermission.SupplierReturnsView, "Supplier Returns", () => _supplierReturns.Value, (viewModel, token) => viewModel.LoadAsync(token), "purchasing.supplier-returns"); AddModule("Purchasing", Icons.Purchasing, "Manage orders, supplier deliveries, and returns.", purchasingPages); }
		var warehousePages = new List<SecondaryNavigationItem>();
		AddPage(warehousePages, ApplicationPermission.StockTransfersView, "Transfers", () => _stockTransfers.Value, (viewModel, token) => viewModel.LoadAsync(token), "warehouse.transfers"); AddPage(warehousePages, ApplicationPermission.InventoryCountsView, "Inventory Counts", () => _inventoryCounts.Value, (viewModel, token) => viewModel.LoadAsync(token), "warehouse.inventory-counts"); AddPage(warehousePages, ApplicationPermission.MaterialIssuesView, "Material Issues", () => _materialIssues.Value, (viewModel, token) => viewModel.LoadAsync(token), "warehouse.material-issues"); AddPage(warehousePages, ApplicationPermission.MaterialReturnsView, "Material Returns", () => _materialReturns.Value, (viewModel, token) => viewModel.LoadAsync(token), "warehouse.material-returns"); AddPage(warehousePages, ApplicationPermission.ShipmentsView, "Shipping", () => _salesShipping.Value, (viewModel, token) => viewModel.LoadAsync(token), "sales.shipping"); AddModule("Warehouse", Icons.Warehouse, "Execute controlled warehouse operations, fulfillment, shipping, and physical stock workflows.", warehousePages);
		var financePages = new List<SecondaryNavigationItem>();
		AddPage(financePages, ApplicationPermission.FinancePostingProfilesView, "Posting Flow Designer", () => _financePostingFlowDesigner.Value, (viewModel, token) => viewModel.LoadAsync(token), "finance.general-ledger");
		AddPage(financePages, ApplicationPermission.FinanceReceivablesView, "Receivables", () => _financeReceivables.Value, (viewModel, token) => viewModel.LoadAsync(token), "finance.receivables"); AddPage(financePages, ApplicationPermission.FinancePayablesView, "Payables", () => _financePayables.Value, (viewModel, token) => viewModel.LoadAsync(token), "finance.payables"); AddPage(financePages, ApplicationPermission.FinanceInventoryAccountingView, "Inventory Accounting", () => _financeInventoryAccounting.Value, (viewModel, token) => viewModel.LoadAsync(token), "finance.inventory-accounting"); AddPage(financePages, ApplicationPermission.FinanceBankingView, "Banking", () => _financeBanking.Value, (viewModel, token) => viewModel.LoadAsync(token), "finance.banking"); AddPage(financePages, ApplicationPermission.FinanceFinancialReportingView, "Financial Reporting", () => _financeFinancialReporting.Value, (viewModel, token) => viewModel.LoadAsync(token), "finance.reporting"); AddModule("Finance", Icons.Finance, "Manage receivables, payables, inventory valuation, banking, financial reporting, settlements, matching, and controlled accounting workflows.", financePages);
		var approvalPages = new List<SecondaryNavigationItem>(); AddPage(approvalPages, ApplicationPermission.PurchaseOrdersApprove, "Purchase Approvals", () => _purchaseOrderApprovals.Value, (viewModel, token) => viewModel.LoadAsync(token), "approvals.purchase"); AddPage(approvalPages, ApplicationPermission.SalesOrdersApprove, "Sales Approvals", () => _salesApprovals.Value, (viewModel, token) => viewModel.LoadAsync(token), "approvals.sales"); AddModule("Approvals", Icons.Approvals, "Review and decide pending purchase and sales approvals.", approvalPages, isPrimaryNavigationVisible: false);
		AddDirect(ApplicationPermission.ReportsView, "Reports", Icons.Reports, () => _reports.Value, (viewModel, token) => viewModel.LoadAsync(token), "reports.overview");
		if (HasAdministrationPages()) { var administrationPages = new List<SecondaryNavigationItem> { new("Administration", () => _administration.Value, (viewModel, token) => ((AdministrationViewModel)viewModel).ActivateAsync(token), HelpService.FallbackTopicId) }; AddModule("Administration", Icons.Administration, "Configure master data, security, connectivity, and application settings.", administrationPages, true); }
	}

	private void AddDirect<TViewModel>(ApplicationPermission permission,string name,string icon,Func<TViewModel> createContent,Func<TViewModel,CancellationToken,Task> loadAsync,string helpTopicId,string? navigationLabel=null) where TViewModel:BaseViewModel { if(!_authorization.HasPermission(permission))return; NavigationItems.Add(new ShellNavigationItem(name,icon,()=>createContent(),(viewModel,token)=>loadAsync((TViewModel)viewModel,token),helpTopicId,navigationLabel:navigationLabel)); }
	private void AddPage<TViewModel>(ICollection<SecondaryNavigationItem> pages,ApplicationPermission permission,string name,Func<TViewModel> createContent,Func<TViewModel,CancellationToken,Task> loadAsync,string helpTopicId,Action? activate=null) where TViewModel:BaseViewModel { if(!_authorization.HasPermission(permission))return; pages.Add(new SecondaryNavigationItem(name,()=>createContent(),(viewModel,token)=>loadAsync((TViewModel)viewModel,token),helpTopicId,activate)); }
	private void AddModule(string name,string icon,string subtitle,IReadOnlyCollection<SecondaryNavigationItem> pages,bool isSeparated=false,bool isPrimaryNavigationVisible=true,bool showContextNavigation=true) { if(pages.Count==0)return; NavigationItems.Add(new ShellNavigationItem(name,icon,()=>CreateModule(name,subtitle,pages),(viewModel,token)=>((ShellModuleViewModel)viewModel).ActivateAsync(token),pages.First().HelpTopicId,isSeparated:isSeparated,ownsLoadState:false,refreshAsync:(viewModel,token)=>((ShellModuleViewModel)viewModel).RefreshAsync(token),isPrimaryNavigationVisible:isPrimaryNavigationVisible,showContextNavigation:showContextNavigation)); }
	private ShellModuleViewModel CreateModule(string name,string subtitle,IEnumerable<SecondaryNavigationItem> pages) { var module=new ShellModuleViewModel(name,subtitle,pages){NavigationGuard=ConfirmDiscardChanges}; module.NavigationRequested+=OnModuleNavigationRequested; return module; }
	private async void OnModuleNavigationRequested(object? sender,EventArgs e){if(sender is not ShellModuleViewModel module)return;var item=NavigationItems.FirstOrDefault(candidate=>candidate.IsContentCreated&&ReferenceEquals(candidate.Content,module));if(item is not null)await NavigateAsync(item);}
	private bool HasAdministrationPages()=>_authorization.HasAnyPermission(ApplicationPermission.MasterDataView,ApplicationPermission.SuppliersView,ApplicationPermission.UsersView,ApplicationPermission.RolesView,ApplicationPermission.ImportManage,ApplicationPermission.AuditLogView,ApplicationPermission.SecurityEventsView,ApplicationPermission.DatabaseView,ApplicationPermission.DocumentTemplatesView,ApplicationPermission.AdministrationView);
	private void MarkInventoryPagesStale(){MarkModulePageStale("Inventory","Overview");MarkModulePageStale("Inventory","Movements");}
	private void MarkPurchasingPagesStale(){MarkModulePageStale("Purchasing","Overview");MarkModulePageStale("Purchasing","Purchase Orders");MarkModulePageStale("Purchasing","Goods Receipts");}
	private void MarkModulePageStale(string moduleName,string pageName){var moduleItem=NavigationItems.FirstOrDefault(item=>item.Name==moduleName);if(moduleItem?.IsContentCreated!=true||moduleItem.Content is not ShellModuleViewModel module)return;module.Pages.FirstOrDefault(page=>page.Name==pageName)?.MarkStale();}
	private CancellationTokenSource BeginNavigation(CancellationToken cancellationToken){CancelNavigationLoad();_navigationCancellation=cancellationToken.CanBeCanceled?CancellationTokenSource.CreateLinkedTokenSource(cancellationToken):new CancellationTokenSource();return _navigationCancellation;}
	private void CancelNavigationLoad(){_navigationCancellation?.Cancel();_navigationCancellation?.Dispose();_navigationCancellation=null;}
	private void SetSelectedNavigationItem(ShellNavigationItem item){if(_selectedNavigationItem==item)return;_selectedNavigationItem=item;OnPropertyChanged(nameof(SelectedNavigationItem));OnPropertyChanged(nameof(ShowSelectedContextNavigation));}
	private HelpViewModel CreateHelpViewModel(IHelpService helpService,HelpMarkdownRenderer helpRenderer){var viewModel=new HelpViewModel(helpService,helpRenderer);viewModel.CloseRequested+=OnHelpCloseRequested;return viewModel;}
	private NotificationCenterViewModel CreateNotificationCenterViewModel(INotificationService notificationService,INotificationNavigationService navigationService){var viewModel=new NotificationCenterViewModel(notificationService,navigationService);viewModel.CloseRequested+=OnNotificationCloseRequested;return viewModel;}
	private void OnHelpCloseRequested(object? sender,EventArgs e){CurrentViewModel=_viewModelBeforeHelp??SelectedNavigationItem?.Content;_viewModelBeforeHelp=null;}
	private void OnNotificationCloseRequested(object? sender,EventArgs e){CurrentViewModel=_viewModelBeforeNotifications??SelectedNavigationItem?.Content;_viewModelBeforeNotifications=null;}

	private async Task NavigateToNotificationAsync(NotificationNavigationTarget target,CancellationToken cancellationToken)
	{
		if(!ConfirmDiscardChanges(CurrentViewModel))return;
		switch(target.SourceType)
		{
			case NotificationSourceTypes.PurchaseOrder: if(target.SourceId is not long orderId)throw new InvalidOperationException("The purchase-order reference is invalid.");await NavigateToModulePageAsync("Purchasing","Purchase Orders",cancellationToken);await ProcurementViewModel.OpenOrderAsync(orderId,cancellationToken);break;
			case NotificationSourceTypes.PurchaseOrderApproval: if(target.SourceId is not long approvalId)throw new InvalidOperationException("The approval reference is invalid.");await NavigateToModulePageAsync("Approvals","Purchase Approvals",cancellationToken);await PurchaseOrderApprovalsViewModel.OpenApprovalAsync(approvalId,cancellationToken);break;
			case NotificationSourceTypes.InventoryCount: if(target.SourceId is not long countId)throw new InvalidOperationException("The inventory-count reference is invalid.");await NavigateToModulePageAsync("Warehouse","Inventory Counts",cancellationToken);await InventoryCountsViewModel.OpenCountAsync(countId,cancellationToken);break;
			case NotificationSourceTypes.DatabaseAdministration: await NavigateToDirectAsync("Administration",cancellationToken);await AdministrationViewModel.NavigateToAsync(AdministrationSection.Database,cancellationToken);break;
			case NotificationSourceTypes.SalesOrder: if(target.SourceId is not long salesOrderId)throw new InvalidOperationException("The sales-order reference is invalid.");await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder,salesOrderId,target.SourceNumber??string.Empty,string.Empty),cancellationToken);break;
			case NotificationSourceTypes.SalesOrderApproval: if(target.SourceId is not long salesApprovalId)throw new InvalidOperationException("The sales-order approval reference is invalid.");await NavigateToModulePageAsync("Approvals","Sales Approvals",cancellationToken);await SalesApprovalsViewModel.Workspace.OpenQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder,salesApprovalId,target.SourceNumber??string.Empty,string.Empty),cancellationToken);break;
			case NotificationSourceTypes.Shipment: if(target.SourceId is not long shipmentId)throw new InvalidOperationException("The shipment reference is invalid.");await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Shipment,shipmentId,target.SourceNumber??string.Empty,string.Empty),cancellationToken);break;
			case NotificationSourceTypes.CustomerReturn: if(target.SourceId is not long returnId)throw new InvalidOperationException("The customer-return reference is invalid.");await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.CustomerReturn,returnId,target.SourceNumber??string.Empty,string.Empty),cancellationToken);break;
			case NotificationSourceTypes.SalesInvoice: if(target.SourceId is not long invoiceId)throw new InvalidOperationException("The sales-invoice reference is invalid.");await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Invoice,invoiceId,target.SourceNumber??string.Empty,string.Empty),cancellationToken);break;
			case NotificationSourceTypes.SalesCreditNote: if(target.SourceId is not long creditId)throw new InvalidOperationException("The credit-note reference is invalid.");await OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.CreditNote,creditId,target.SourceNumber??string.Empty,string.Empty),cancellationToken);break;
		}
	}
	private Task NavigateToDirectAsync(string name,CancellationToken cancellationToken){var item=NavigationItems.FirstOrDefault(candidate=>candidate.Name==name)??throw new UnauthorizedAccessException("The requested page is not available.");return NavigateAsync(item,cancellationToken);}
	private async Task NavigateToAdministrationSectionAsync(AdministrationSection section, CancellationToken cancellationToken)
	{
		await this.NavigateToRouteAsync(ShellRoutes.Administration, cancellationToken);
		await AdministrationViewModel.NavigateToAsync(section, cancellationToken);
	}

	private async Task NavigateToModulePageAsync(string moduleName,string pageName,CancellationToken cancellationToken){var item=NavigationItems.FirstOrDefault(candidate=>candidate.Name==moduleName)??throw new UnauthorizedAccessException("The requested module is not available.");if(item.Content is not ShellModuleViewModel module)throw new InvalidOperationException("The requested navigation target is invalid.");var page=module.Pages.FirstOrDefault(candidate=>candidate.Name==pageName)??throw new UnauthorizedAccessException("The requested page is not available.");if(!module.SetSelectedPage(page))return;await NavigateAsync(item,cancellationToken);}
	private void ToggleNavigation() => IsNavigationExpanded = !IsNavigationExpanded;
	private void Logout(){if(!ConfirmDiscardChanges(CurrentViewModel))return;_session.Logout();LogoutRequested?.Invoke(this,EventArgs.Empty);}

	public void Dispose()
	{
		if(_disposed)return;_disposed=true;_notificationNavigation.SetNavigationHandler(null);CancelNavigationLoad();_notificationLoadState.Dispose();NotificationSummaryViewModel.Dispose();foreach(var item in NavigationItems){if(item.IsContentCreated&&item.Content is ShellModuleViewModel module)module.NavigationRequested-=OnModuleNavigationRequested;item.Dispose();}
		if(_procurement.IsValueCreated)_procurement.Value.Dispose();if(_salesOverview.IsValueCreated)_salesOverview.Value.Dispose();if(_salesQuotes.IsValueCreated)_salesQuotes.Value.Dispose();if(_salesPricing.IsValueCreated)_salesPricing.Value.Dispose();if(_salesCustomers.IsValueCreated)_salesCustomers.Value.Dispose();if(_salesOrders.IsValueCreated)_salesOrders.Value.Dispose();if(_salesApprovals.IsValueCreated)_salesApprovals.Value.Dispose();if(_salesShipping.IsValueCreated)_salesShipping.Value.Dispose();if(_salesInvoices.IsValueCreated)_salesInvoices.Value.Dispose();if(_salesSearch.IsValueCreated)_salesSearch.Value.Dispose();if(_financePostingFlowDesigner.IsValueCreated)_financePostingFlowDesigner.Value.Dispose();if(_financeReceivables.IsValueCreated)_financeReceivables.Value.Dispose();if(_financePayables.IsValueCreated)_financePayables.Value.Dispose();if(_financeInventoryAccounting.IsValueCreated)_financeInventoryAccounting.Value.Dispose();if(_financeBanking.IsValueCreated)_financeBanking.Value.Dispose();if(_financeFinancialReporting.IsValueCreated)_financeFinancialReporting.Value.Dispose();if(_help.IsValueCreated){_help.Value.CloseRequested-=OnHelpCloseRequested;_help.Value.Dispose();}if(_notificationCenter.IsValueCreated){_notificationCenter.Value.CloseRequested-=OnNotificationCloseRequested;_notificationCenter.Value.Dispose();}
	}

	private static class Icons
	{
		public const string Dashboard="M 2,17 L 18,17 M 4,14 L 4,10 M 9,14 L 9,5 M 14,14 L 14,8";
		public const string Inventory="M 2,6 L 10,2 L 18,6 L 10,10 Z M 2,6 L 2,14 L 10,18 L 10,10 M 18,6 L 18,14 L 10,18";
		public const string Warehouse="M 2,8 L 10,3 L 18,8 L 18,18 L 2,18 Z M 6,18 L 6,11 L 14,11 L 14,18";
		public const string Purchasing="M 3,4 L 17,4 L 16,17 L 4,17 Z M 7,4 L 7,2 L 13,2 L 13,4 M 7,8 L 13,8 M 7,12 L 13,12";
		public const string Sales="M 3,4 L 17,4 L 17,16 L 3,16 Z M 6,8 L 14,8 M 6,11 L 12,11 M 6,14 L 10,14";
		public const string Finance="M 3,5 L 17,5 M 4,9 L 16,9 M 5,13 L 15,13 M 7,2 L 13,2 L 15,5 L 5,5 Z M 6,17 L 14,17";
		public const string Work="M 3,6 L 17,6 L 17,17 L 3,17 Z M 7,6 L 7,3 L 13,3 L 13,6 M 3,10 L 17,10 M 8,10 L 8,12 L 12,12 L 12,10";
		public const string Approvals="M 3,10 L 8,15 L 17,5 M 3,3 L 17,3 L 17,18 L 3,18 Z";
		public const string Reports="M 2,17 L 18,17 M 4,14 L 8,10 L 11,12 L 16,5 M 13,5 L 16,5 L 16,8";
		public const string Administration="M 4,5 L 16,5 M 7,2 L 7,8 M 4,15 L 16,15 M 13,12 L 13,18 M 4,10 L 16,10 M 10,7 L 10,13";
	}
}
