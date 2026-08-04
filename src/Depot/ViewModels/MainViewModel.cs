// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels.Administration;
using Depot.ViewModels.Help;
using Depot.Services.Help;

namespace Depot.ViewModels;

public sealed class MainViewModel : BaseViewModel, IDisposable
{
	private readonly IAuthorizationService _authorization;
	private readonly SessionService _session;
	private CancellationTokenSource? _navigationCancellation;
	private ShellNavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;
	private BaseViewModel? _viewModelBeforeHelp;
	private BaseViewModel? _viewModelBeforeNotifications;

	public MainViewModel(
		ItemService itemService,
		StockService stockService,
		MovementService movementService,
		ReportService reportService,
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
		GoodsReceiptService goodsReceiptService,
		StockTransferService stockTransferService,
		InventoryCountService inventoryCountService,
		MaterialIssueService materialIssueService,
		MaterialReturnService materialReturnService,
		SupplierReturnService supplierReturnService,
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
		ApplicationInformationService applicationInformationService,
		IHelpService helpService,
		HelpMarkdownRenderer helpRenderer,
		INotificationService notificationService,
		INotificationNavigationService notificationNavigationService)
	{
		_authorization = authorizationService;
		_session = sessionService;
		ConnectionStatus = connectionStatusService;
		LogoutCommand = new RelayCommand(Logout);
		HelpCommand = new RelayCommand(() => _ = OpenHelpAsync());
		NotificationCommand = new RelayCommand(() => _ = OpenNotificationsAsync());
		HelpViewModel = new HelpViewModel(helpService, helpRenderer);
		HelpViewModel.CloseRequested += OnHelpCloseRequested;
		NotificationCenterViewModel = new NotificationCenterViewModel(notificationService, notificationNavigationService);
		NotificationCenterViewModel.CloseRequested += OnNotificationCloseRequested;
		notificationNavigationService.SetNavigationHandler(NavigateToNotificationAsync);
		_notificationNavigation = notificationNavigationService;

		DashboardViewModel = new DashboardViewModel(stockService);
		InventoryViewModel = new InventoryViewModel(stockService);
		ItemsViewModel = new ItemsViewModel(itemService, manufacturerService, categoryService, unitOfMeasureService, packagingService);
		MovementsViewModel = new MovementsViewModel(movementService, reasonCodeService, fileDialogService);
		StockTransfersViewModel = new StockTransfersViewModel(stockTransferService, warehouseService, fileDialogService, reasonCodeService);
		InventoryCountsViewModel = new InventoryCountsViewModel(inventoryCountService, warehouseService, fileDialogService, reasonCodeService);
		MaterialIssuesViewModel = new MaterialIssuesViewModel(materialIssueService, reasonCodeService, fileDialogService);
		MaterialReturnsViewModel = new MaterialReturnsViewModel(materialReturnService, reasonCodeService, fileDialogService);
		SupplierReturnsViewModel = new SupplierReturnsViewModel(supplierReturnService, supplierService, reasonCodeService, fileDialogService);
		ProcurementViewModel = new ProcurementViewModel(purchaseOrderService, goodsReceiptService, supplierService, itemService, fileDialogService, reasonCodeService);
		PurchaseOrdersPageViewModel = new PurchaseOrdersPageViewModel(ProcurementViewModel);
		GoodsReceiptsPageViewModel = new GoodsReceiptsPageViewModel(ProcurementViewModel);
		PurchaseOrderApprovalsViewModel = new PurchaseOrderApprovalsViewModel(purchaseOrderApprovalService, fileDialogService);
		ReportsViewModel = new ReportsViewModel(reportService, fileDialogService);
		ImportViewModel = new ImportViewModel(importService, fileDialogService);
		AdministrationViewModel = new AdministrationViewModel(
			ImportViewModel, itemService, purposeService, reasonCodeService, manufacturerService, categoryService,
			unitOfMeasureService, packagingService, supplierCategoryService, supplierService, supplierItemService,
			warehouseService, storageLocationService, userService, roleService, authorizationService, settingsService,
			connectionStatusService, databaseConnectionTester, databaseManagementService, auditLogService,
			fileDialogService, applicationInformationService);

		BuildNavigation();
		SelectedNavigationItem = NavigationItems.FirstOrDefault();
	}

	public ObservableCollection<ShellNavigationItem> NavigationItems { get; } = new();
	public RelayCommand LogoutCommand { get; }
	public RelayCommand HelpCommand { get; }
	public RelayCommand NotificationCommand { get; }
	public ConnectionStatusService ConnectionStatus { get; }
	public event EventHandler? LogoutRequested;

	public DashboardViewModel DashboardViewModel { get; }
	public InventoryViewModel InventoryViewModel { get; }
	public ItemsViewModel ItemsViewModel { get; }
	public MovementsViewModel MovementsViewModel { get; }
	public StockTransfersViewModel StockTransfersViewModel { get; }
	public InventoryCountsViewModel InventoryCountsViewModel { get; }
	public MaterialIssuesViewModel MaterialIssuesViewModel { get; }
	public MaterialReturnsViewModel MaterialReturnsViewModel { get; }
	public SupplierReturnsViewModel SupplierReturnsViewModel { get; }
	public ProcurementViewModel ProcurementViewModel { get; }
	public PurchaseOrdersPageViewModel PurchaseOrdersPageViewModel { get; }
	public GoodsReceiptsPageViewModel GoodsReceiptsPageViewModel { get; }
	public PurchaseOrderApprovalsViewModel PurchaseOrderApprovalsViewModel { get; }
	public ReportsViewModel ReportsViewModel { get; }
	public ImportViewModel ImportViewModel { get; }
	public AdministrationViewModel AdministrationViewModel { get; }
	public HelpViewModel HelpViewModel { get; }
	public NotificationCenterViewModel NotificationCenterViewModel { get; }
	private readonly INotificationNavigationService _notificationNavigation;

	public string CurrentHelpTopicId => SelectedNavigationItem?.Content switch
	{
		ShellModuleViewModel { SelectedPage.Content: AdministrationViewModel administration } => administration.HelpTopicId,
		ShellModuleViewModel module => module.SelectedPage?.HelpTopicId ?? HelpService.FallbackTopicId,
		_ => SelectedNavigationItem?.HelpTopicId ?? HelpService.FallbackTopicId
	};

	public async Task OpenHelpAsync(string? topicId = null, CancellationToken cancellationToken = default)
	{
		if (CurrentViewModel != HelpViewModel) _viewModelBeforeHelp = CurrentViewModel;
		CurrentViewModel = HelpViewModel;
		await HelpViewModel.OpenAsync(topicId ?? CurrentHelpTopicId, cancellationToken);
	}

	public async Task OpenNotificationsAsync(CancellationToken cancellationToken = default)
	{
		if (CurrentViewModel != NotificationCenterViewModel) _viewModelBeforeNotifications = CurrentViewModel;
		CurrentViewModel = NotificationCenterViewModel;
		await NotificationCenterViewModel.LoadAsync(cancellationToken);
	}

	public void SetApplicationActive(bool isActive) => NotificationCenterViewModel.SetApplicationActive(isActive);

	public ShellNavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;
		set
		{
			if (_selectedNavigationItem == value) return;
			_selectedNavigationItem = value;
			CurrentViewModel = value?.Content;
			OnPropertyChanged();
			_ = LoadSelectedAsync();
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;
		private set
		{
			if (_currentViewModel == value) return;
			_currentViewModel = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsHelpOpen));
		}
	}

	public bool IsHelpOpen => CurrentViewModel == HelpViewModel;

	public string CurrentUserDisplayName => _authorization.CurrentUser?.DisplayName ?? string.Empty;
	public string CurrentUserRole => _authorization.CurrentUser is { Roles.Count: > 0 } user
		? string.Join(", ", user.Roles.Select(role => role.Name))
		: "No active role";

	private void BuildNavigation()
	{
		AddDirect(ApplicationPermission.DashboardView, "Dashboard", Icons.Dashboard, DashboardViewModel, DashboardViewModel.LoadAsync, HelpService.FallbackTopicId);

		var inventoryPages = new List<SecondaryNavigationItem>();
		AddPage(inventoryPages, ApplicationPermission.InventoryView, "Overview", InventoryViewModel, InventoryViewModel.LoadAsync, "inventory.overview");
		AddPage(inventoryPages, ApplicationPermission.ItemsView, "Items", ItemsViewModel, ItemsViewModel.LoadItemsAsync, "inventory.items");
		AddPage(inventoryPages, ApplicationPermission.StockMovementsView, "Movements", MovementsViewModel, MovementsViewModel.LoadAsync, "inventory.movements");
		AddModule("Inventory", Icons.Inventory, "Monitor stock, items, and immutable inventory movements.", inventoryPages);

		var warehousePages = new List<SecondaryNavigationItem>();
		AddPage(warehousePages, ApplicationPermission.StockTransfersView, "Transfers", StockTransfersViewModel, StockTransfersViewModel.LoadAsync, "warehouse.transfers");
		AddPage(warehousePages, ApplicationPermission.InventoryCountsView, "Inventory Counts", InventoryCountsViewModel, InventoryCountsViewModel.LoadAsync, "warehouse.inventory-counts");
		AddPage(warehousePages, ApplicationPermission.MaterialIssuesView, "Material Issues", MaterialIssuesViewModel, MaterialIssuesViewModel.LoadAsync, "warehouse.material-issues");
		AddPage(warehousePages, ApplicationPermission.MaterialReturnsView, "Material Returns", MaterialReturnsViewModel, MaterialReturnsViewModel.LoadAsync, "warehouse.material-returns");
		AddModule("Warehouse", Icons.Warehouse, "Execute controlled warehouse operations and physical stock workflows.", warehousePages);

		if (_authorization.HasPermission(ApplicationPermission.PurchasingView))
		{
			var purchasingPages = new List<SecondaryNavigationItem>();
			AddPage(purchasingPages, ApplicationPermission.PurchaseOrdersView, "Purchase Orders", PurchaseOrdersPageViewModel, PurchaseOrdersPageViewModel.LoadAsync, "purchasing.purchase-orders");
			AddPage(purchasingPages, ApplicationPermission.GoodsReceiptsView, "Goods Receipts", GoodsReceiptsPageViewModel, GoodsReceiptsPageViewModel.LoadAsync, "purchasing.goods-receipts");
			AddPage(purchasingPages, ApplicationPermission.SupplierReturnsView, "Supplier Returns", SupplierReturnsViewModel, SupplierReturnsViewModel.LoadAsync, "purchasing.supplier-returns");
			AddModule("Purchasing", Icons.Purchasing, "Manage orders, supplier deliveries, and returns.", purchasingPages);
		}

		AddDirect(ApplicationPermission.PurchaseOrdersApprove, "Approvals", Icons.Approvals, PurchaseOrderApprovalsViewModel, PurchaseOrderApprovalsViewModel.LoadAsync, "approvals.queue");
		AddDirect(ApplicationPermission.ReportsView, "Reports", Icons.Reports, ReportsViewModel, ReportsViewModel.LoadAsync, "reports.overview");

		if (AdministrationViewModel.NavigationItems.Count > 0)
		{
			var administrationModule = new ShellModuleViewModel(
				"Administration",
				"Configure master data, security, connectivity, and application settings.",
				[new SecondaryNavigationItem { Name = "Administration", Content = AdministrationViewModel, LoadAsync = AdministrationViewModel.LoadAsync, HelpTopicId = HelpService.FallbackTopicId }]);
			NavigationItems.Add(new ShellNavigationItem
			{
				Name = "Administration",
				IconData = Icons.Administration,
				Content = administrationModule,
				LoadAsync = administrationModule.LoadAsync,
				HelpTopicId = HelpService.FallbackTopicId,
				IsSeparated = true
			});
		}
	}

	private void AddDirect(
		ApplicationPermission permission,
		string name,
		string icon,
		BaseViewModel content,
		Func<CancellationToken, Task> loadAsync,
		string helpTopicId)
	{
		if (!_authorization.HasPermission(permission)) return;
		NavigationItems.Add(new ShellNavigationItem { Name = name, IconData = icon, Content = content, LoadAsync = loadAsync, HelpTopicId = helpTopicId });
	}

	private void AddModule(string name, string icon, string subtitle, IReadOnlyCollection<SecondaryNavigationItem> pages)
	{
		if (pages.Count == 0) return;
		var module = new ShellModuleViewModel(name, subtitle, pages);
		NavigationItems.Add(new ShellNavigationItem { Name = name, IconData = icon, Content = module, LoadAsync = module.LoadAsync, HelpTopicId = pages.First().HelpTopicId });
	}

	private void AddPage(
		ICollection<SecondaryNavigationItem> pages,
		ApplicationPermission permission,
		string name,
		BaseViewModel content,
		Func<CancellationToken, Task> loadAsync,
		string helpTopicId,
		Action? activate = null)
	{
		if (_authorization.HasPermission(permission))
			pages.Add(new SecondaryNavigationItem { Name = name, Content = content, LoadAsync = loadAsync, HelpTopicId = helpTopicId, Activate = activate });
	}

	private async Task LoadSelectedAsync()
	{
		_navigationCancellation?.Cancel();
		_navigationCancellation?.Dispose();
		_navigationCancellation = new CancellationTokenSource();
		var selected = SelectedNavigationItem;
		if (selected is null) return;
		try
		{
			await selected.LoadAsync(_navigationCancellation.Token);
		}
		catch (OperationCanceledException) when (_navigationCancellation.IsCancellationRequested)
		{
		}
	}

	private void Logout()
	{
		_session.Logout();
		LogoutRequested?.Invoke(this, EventArgs.Empty);
	}

	private void OnHelpCloseRequested(object? sender, EventArgs e)
	{
		CurrentViewModel = _viewModelBeforeHelp ?? SelectedNavigationItem?.Content;
		_viewModelBeforeHelp = null;
	}

	private void OnNotificationCloseRequested(object? sender, EventArgs e)
	{
		CurrentViewModel = _viewModelBeforeNotifications ?? SelectedNavigationItem?.Content;
		_viewModelBeforeNotifications = null;
	}

	private async Task NavigateToNotificationAsync(NotificationNavigationTarget target, CancellationToken cancellationToken)
	{
		switch (target.SourceType)
		{
			case NotificationSourceTypes.PurchaseOrder:
				if (target.SourceId is not long orderId) throw new InvalidOperationException("The purchase-order reference is invalid.");
				await ProcurementViewModel.OpenOrderAsync(orderId, cancellationToken);
				await NavigateToModulePageAsync("Purchasing", "Purchase Orders", cancellationToken);
				break;
			case NotificationSourceTypes.PurchaseOrderApproval:
				if (target.SourceId is not long approvalId) throw new InvalidOperationException("The approval reference is invalid.");
				await PurchaseOrderApprovalsViewModel.OpenApprovalAsync(approvalId, cancellationToken);
				await NavigateToDirectAsync("Approvals", cancellationToken);
				break;
			case NotificationSourceTypes.InventoryCount:
				if (target.SourceId is not long countId) throw new InvalidOperationException("The inventory-count reference is invalid.");
				await InventoryCountsViewModel.OpenCountAsync(countId, cancellationToken);
				await NavigateToModulePageAsync("Warehouse", "Inventory Counts", cancellationToken);
				break;
			case NotificationSourceTypes.DatabaseAdministration:
				AdministrationViewModel.NavigateTo(AdministrationSection.Database);
				await NavigateToDirectAsync("Administration", cancellationToken);
				break;
		}
	}

	private async Task NavigateToDirectAsync(string name, CancellationToken cancellationToken)
	{
		var item = NavigationItems.FirstOrDefault(candidate => candidate.Name == name)
			?? throw new UnauthorizedAccessException("The requested page is not available.");
		SelectedNavigationItem = item;
		CurrentViewModel = item.Content;
		await item.LoadAsync(cancellationToken);
	}

	private async Task NavigateToModulePageAsync(string moduleName, string pageName, CancellationToken cancellationToken)
	{
		var item = NavigationItems.FirstOrDefault(candidate => candidate.Name == moduleName)
			?? throw new UnauthorizedAccessException("The requested module is not available.");
		if (item.Content is not ShellModuleViewModel module)
			throw new InvalidOperationException("The requested navigation target is invalid.");
		module.SelectedPage = module.Pages.FirstOrDefault(page => page.Name == pageName)
			?? throw new UnauthorizedAccessException("The requested page is not available.");
		SelectedNavigationItem = item;
		CurrentViewModel = module;
		await module.LoadAsync(cancellationToken);
	}

	public void Dispose()
	{
		_notificationNavigation.SetNavigationHandler(null);
		NotificationCenterViewModel.CloseRequested -= OnNotificationCloseRequested;
		NotificationCenterViewModel.Dispose();
		_navigationCancellation?.Cancel();
		_navigationCancellation?.Dispose();
	}

	private static class Icons
	{
		public const string Dashboard = "M 2,17 L 18,17 M 4,14 L 4,10 M 9,14 L 9,5 M 14,14 L 14,8";
		public const string Inventory = "M 2,6 L 10,2 L 18,6 L 10,10 Z M 2,6 L 2,14 L 10,18 L 10,10 M 18,6 L 18,14 L 10,18";
		public const string Warehouse = "M 2,8 L 10,3 L 18,8 L 18,18 L 2,18 Z M 6,18 L 6,11 L 14,11 L 14,18";
		public const string Purchasing = "M 3,4 L 17,4 L 16,17 L 4,17 Z M 7,4 L 7,2 L 13,2 L 13,4 M 7,8 L 13,8 M 7,12 L 13,12";
		public const string Approvals = "M 3,10 L 8,15 L 17,5 M 3,3 L 17,3 L 17,18 L 3,18 Z";
		public const string Reports = "M 2,17 L 18,17 M 4,14 L 8,10 L 11,12 L 16,5 M 13,5 L 16,5 L 16,8";
		public const string Administration = "M 4,5 L 16,5 M 7,2 L 7,8 M 4,15 L 16,15 M 13,12 L 13,18 M 4,10 L 16,10 M 10,7 L 10,13";
	}
}
