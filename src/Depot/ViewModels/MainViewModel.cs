// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels.Administration;

namespace Depot.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
	private readonly IAuthorizationService _authorization;
	private readonly SessionService _session;
	private CancellationTokenSource? _navigationCancellation;
	private ShellNavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

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
		ApplicationInformationService applicationInformationService)
	{
		_authorization = authorizationService;
		_session = sessionService;
		ConnectionStatus = connectionStatusService;
		LogoutCommand = new RelayCommand(Logout);

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
		PurchaseOrderApprovalsViewModel = new PurchaseOrderApprovalsViewModel(purchaseOrderApprovalService);
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
	public PurchaseOrderApprovalsViewModel PurchaseOrderApprovalsViewModel { get; }
	public ReportsViewModel ReportsViewModel { get; }
	public ImportViewModel ImportViewModel { get; }
	public AdministrationViewModel AdministrationViewModel { get; }

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
		}
	}

	public string CurrentUserDisplayName => _authorization.CurrentUser?.DisplayName ?? string.Empty;
	public string CurrentUserRole => _authorization.CurrentUser is { Roles.Count: > 0 } user
		? string.Join(", ", user.Roles.Select(role => role.Name))
		: "No active role";

	private void BuildNavigation()
	{
		AddDirect(ApplicationPermission.DashboardView, "Dashboard", Icons.Dashboard, DashboardViewModel, DashboardViewModel.LoadAsync);

		var inventoryPages = new List<SecondaryNavigationItem>();
		AddPage(inventoryPages, ApplicationPermission.InventoryView, "Overview", InventoryViewModel, InventoryViewModel.LoadAsync);
		AddPage(inventoryPages, ApplicationPermission.ItemsView, "Items", ItemsViewModel, ItemsViewModel.LoadItemsAsync);
		AddPage(inventoryPages, ApplicationPermission.StockMovementsView, "Movements", MovementsViewModel, MovementsViewModel.LoadAsync);
		AddModule("Inventory", Icons.Inventory, "Monitor stock, items, and immutable inventory movements.", inventoryPages);

		var warehousePages = new List<SecondaryNavigationItem>();
		AddPage(warehousePages, ApplicationPermission.StockTransfersView, "Transfers", StockTransfersViewModel, StockTransfersViewModel.LoadAsync);
		AddPage(warehousePages, ApplicationPermission.InventoryCountsView, "Inventory Counts", InventoryCountsViewModel, InventoryCountsViewModel.LoadAsync);
		AddPage(warehousePages, ApplicationPermission.MaterialIssuesView, "Material Issues", MaterialIssuesViewModel, MaterialIssuesViewModel.LoadAsync);
		AddPage(warehousePages, ApplicationPermission.MaterialReturnsView, "Material Returns", MaterialReturnsViewModel, MaterialReturnsViewModel.LoadAsync);
		AddModule("Warehouse", Icons.Warehouse, "Execute controlled warehouse operations and physical stock workflows.", warehousePages);

		if (_authorization.HasPermission(ApplicationPermission.PurchasingView))
		{
			var purchasingPages = new List<SecondaryNavigationItem>();
			AddPage(purchasingPages, ApplicationPermission.PurchaseOrdersView, "Purchase Orders", ProcurementViewModel, ProcurementViewModel.LoadAsync,
				() => ProcurementViewModel.Section = ProcurementSection.PurchaseOrders);
			AddPage(purchasingPages, ApplicationPermission.GoodsReceiptsView, "Goods Receipts", ProcurementViewModel, ProcurementViewModel.LoadAsync,
				() => ProcurementViewModel.Section = ProcurementSection.GoodsReceipts);
			AddPage(purchasingPages, ApplicationPermission.SupplierReturnsView, "Supplier Returns", SupplierReturnsViewModel, SupplierReturnsViewModel.LoadAsync);
			AddModule("Purchasing", Icons.Purchasing, "Manage orders, supplier deliveries, and returns.", purchasingPages);
		}

		AddDirect(ApplicationPermission.PurchaseOrdersApprove, "Approvals", Icons.Approvals, PurchaseOrderApprovalsViewModel, PurchaseOrderApprovalsViewModel.LoadAsync);
		AddDirect(ApplicationPermission.ReportsView, "Reports", Icons.Reports, ReportsViewModel, ReportsViewModel.LoadAsync);

		if (AdministrationViewModel.NavigationItems.Count > 0)
		{
			var administrationModule = new ShellModuleViewModel(
				"Administration",
				"Configure master data, security, connectivity, and application settings.",
				[new SecondaryNavigationItem { Name = "Administration", Content = AdministrationViewModel, LoadAsync = AdministrationViewModel.LoadAsync }]);
			NavigationItems.Add(new ShellNavigationItem
			{
				Name = "Administration",
				IconData = Icons.Administration,
				Content = administrationModule,
				LoadAsync = administrationModule.LoadAsync,
				IsSeparated = true
			});
		}
	}

	private void AddDirect(
		ApplicationPermission permission,
		string name,
		string icon,
		BaseViewModel content,
		Func<CancellationToken, Task> loadAsync)
	{
		if (!_authorization.HasPermission(permission)) return;
		NavigationItems.Add(new ShellNavigationItem { Name = name, IconData = icon, Content = content, LoadAsync = loadAsync });
	}

	private void AddModule(string name, string icon, string subtitle, IReadOnlyCollection<SecondaryNavigationItem> pages)
	{
		if (pages.Count == 0) return;
		var module = new ShellModuleViewModel(name, subtitle, pages);
		NavigationItems.Add(new ShellNavigationItem { Name = name, IconData = icon, Content = module, LoadAsync = module.LoadAsync });
	}

	private void AddPage(
		ICollection<SecondaryNavigationItem> pages,
		ApplicationPermission permission,
		string name,
		BaseViewModel content,
		Func<CancellationToken, Task> loadAsync,
		Action? activate = null)
	{
		if (_authorization.HasPermission(permission))
			pages.Add(new SecondaryNavigationItem { Name = name, Content = content, LoadAsync = loadAsync, Activate = activate });
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
