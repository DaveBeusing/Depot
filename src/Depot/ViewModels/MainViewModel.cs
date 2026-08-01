// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels.Administration;

namespace Depot.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
	private CancellationTokenSource? _navigationLoadCancellation;
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;
	private readonly AuthorizationService _authorizationService;
	private readonly SessionService _sessionService;
	
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
		AuthorizationService authorizationService,
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
		
		_authorizationService =	authorizationService;
		_sessionService = sessionService;
		ConnectionStatus = connectionStatusService;
		LogoutCommand = new RelayCommand(Logout);
	
		DashboardViewModel =
			new DashboardViewModel(
				stockService);

		InventoryViewModel =
			new InventoryViewModel(
				stockService);

		ItemsViewModel =
			new ItemsViewModel(
				itemService, manufacturerService, categoryService, unitOfMeasureService, packagingService);

		MovementsViewModel =
			new MovementsViewModel(
				movementService,
				reasonCodeService,
				fileDialogService);

		StockTransfersViewModel = new StockTransfersViewModel(stockTransferService, warehouseService, fileDialogService, reasonCodeService);
		InventoryCountsViewModel = new InventoryCountsViewModel(inventoryCountService, warehouseService, fileDialogService, reasonCodeService);
		MaterialIssuesViewModel = new MaterialIssuesViewModel(materialIssueService, reasonCodeService, fileDialogService);
		MaterialReturnsViewModel = new MaterialReturnsViewModel(materialReturnService, reasonCodeService, fileDialogService);
		SupplierReturnsViewModel = new SupplierReturnsViewModel(supplierReturnService, supplierService, reasonCodeService, fileDialogService);

		ProcurementViewModel = new ProcurementViewModel(purchaseOrderService, goodsReceiptService, supplierService, itemService, fileDialogService, reasonCodeService);
		PurchaseOrderApprovalsViewModel = new PurchaseOrderApprovalsViewModel(purchaseOrderApprovalService);

		ReportsViewModel =
			new ReportsViewModel(
				reportService,
				fileDialogService);

		ImportViewModel =
			new ImportViewModel(
				importService,
				fileDialogService);

		AdministrationViewModel =
			new AdministrationViewModel(
				ImportViewModel,
				itemService,
				purposeService,
				reasonCodeService,
				manufacturerService,
				categoryService,
				unitOfMeasureService,
				packagingService,
				supplierCategoryService,
				supplierService,
				supplierItemService,
				warehouseService,
				storageLocationService,
				userService,
				settingsService,
				connectionStatusService,
				databaseConnectionTester,
				databaseManagementService,
				auditLogService,
				fileDialogService,
				applicationInformationService);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Dashboard",
				IconData = "M 2,17 L 18,17 M 4,14 L 4,10 M 9,14 L 9,5 M 14,14 L 14,8",
				Section = ShellSection.Dashboard
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Inventory",
				IconData = "M 2,6 L 10,2 L 18,6 L 10,10 Z M 2,6 L 2,14 L 10,18 L 10,10 M 18,6 L 18,14 L 10,18",
				Section = ShellSection.Inventory
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Items",
				IconData = "M 5,3 L 15,3 L 17,5 L 17,18 L 3,18 L 3,5 Z M 7,3 L 7,1 L 13,1 L 13,3 M 7,8 L 13,8 M 7,12 L 13,12",
				Section = ShellSection.Items
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Movements",
				IconData = "M 3,6 L 15,6 M 12,3 L 15,6 L 12,9 M 17,14 L 5,14 M 8,11 L 5,14 L 8,17",
				Section = ShellSection.Movements
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Transfers",
				IconData = "M 3,6 L 15,6 M 12,3 L 15,6 L 12,9 M 17,14 L 5,14 M 8,11 L 5,14 L 8,17 M 3,10 L 17,10",
				Section = ShellSection.Transfers
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Inventory Counts",
				IconData = "M 4,3 L 16,3 L 16,18 L 4,18 Z M 7,7 L 9,9 L 13,5 M 7,13 L 9,15 L 13,11",
				Section = ShellSection.InventoryCounts
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Material Issues",
				IconData = "M 3,4 L 17,4 L 17,16 L 3,16 Z M 6,8 L 14,8 M 6,12 L 11,12 M 14,10 L 18,10 M 16,8 L 18,10 L 16,12",
				Section = ShellSection.MaterialIssues
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Material Returns",
				IconData = "M 17,4 L 5,4 L 5,16 L 17,16 M 8,8 L 3,11 L 8,14 M 3,11 L 13,11",
				Section = ShellSection.MaterialReturns
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Procurement",
				IconData = "M 3,4 L 17,4 L 16,17 L 4,17 Z M 7,4 L 7,2 L 13,2 L 13,4 M 7,8 L 13,8 M 7,12 L 13,12",
				Section = ShellSection.Procurement
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Supplier Returns",
				IconData = "M 17,4 L 5,4 L 5,16 L 17,16 M 8,8 L 3,11 L 8,14 M 3,11 L 13,11 M 13,7 L 17,11 L 13,15",
				Section = ShellSection.SupplierReturns
			});

		if (_authorizationService.CanApprovePurchaseOrders())
		{
			NavigationItems.Add(
				new NavigationItem
				{
					Name = "Approvals",
					IconData = "M 3,10 L 8,15 L 17,5 M 3,3 L 17,3 L 17,18 L 3,18 Z",
					Section = ShellSection.Approvals
				});
		}

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Reports",
				IconData = "M 2,17 L 18,17 M 4,14 L 8,10 L 11,12 L 16,5 M 13,5 L 16,5 L 16,8",
				Section = ShellSection.Reports
			});

		// Only show the Administration section if the user has permission to manage users
		// CanManageUsers() represents the administrator role in version 1.0.
		if (_authorizationService.CanManageUsers())
		{
			NavigationItems.Add(
				new NavigationItem
				{
					Name = "Administration",
					IconData = "M 4,5 L 16,5 M 7,2 L 7,8 M 4,15 L 16,15 M 13,12 L 13,18 M 4,10 L 16,10 M 10,7 L 10,13",
					Section = ShellSection.Administration,
					IsSeparated = true
				});
		}

		SelectedNavigationItem = NavigationItems[0];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

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

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;
		set
		{
			_selectedNavigationItem = value;
			OnPropertyChanged();
			UpdateCurrentViewModel();
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;
		private set
		{
			_currentViewModel = value;
			OnPropertyChanged();
		}
	}

	private void UpdateCurrentViewModel()
	{
		if (SelectedNavigationItem is null)
		{
			CurrentViewModel = null;
			return;
		}

		CurrentViewModel = (ShellSection)SelectedNavigationItem.Section switch
		{
			ShellSection.Dashboard => DashboardViewModel,
			ShellSection.Inventory => InventoryViewModel,
			ShellSection.Items => ItemsViewModel,
			ShellSection.Movements => MovementsViewModel,
			ShellSection.Transfers => StockTransfersViewModel,
			ShellSection.InventoryCounts => InventoryCountsViewModel,
			ShellSection.MaterialIssues => MaterialIssuesViewModel,
			ShellSection.MaterialReturns => MaterialReturnsViewModel,
			ShellSection.Procurement => ProcurementViewModel,
			ShellSection.SupplierReturns => SupplierReturnsViewModel,
			ShellSection.Approvals => PurchaseOrderApprovalsViewModel,
			ShellSection.Reports => ReportsViewModel,
			ShellSection.Administration => AdministrationViewModel,
			_ => DashboardViewModel
		};
		_navigationLoadCancellation?.Cancel();
		_navigationLoadCancellation?.Dispose();
		_navigationLoadCancellation = new CancellationTokenSource();
		_ = LoadCurrentViewModelAsync(_navigationLoadCancellation.Token);
	}

	private async Task LoadCurrentViewModelAsync(CancellationToken cancellationToken)
	{
		if (CurrentViewModel == DashboardViewModel)
		{
			await DashboardViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == InventoryViewModel)
		{
			await InventoryViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == ItemsViewModel)
		{
			await ItemsViewModel.LoadItemsAsync(cancellationToken);
		}
		else if (CurrentViewModel == MovementsViewModel)
		{
			await MovementsViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == StockTransfersViewModel)
		{
			await StockTransfersViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == InventoryCountsViewModel)
		{
			await InventoryCountsViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == MaterialIssuesViewModel)
		{
			await MaterialIssuesViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == MaterialReturnsViewModel)
		{
			await MaterialReturnsViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == ProcurementViewModel)
		{
			await ProcurementViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == SupplierReturnsViewModel)
		{
			await SupplierReturnsViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == PurchaseOrderApprovalsViewModel)
		{
			await PurchaseOrderApprovalsViewModel.LoadAsync(cancellationToken);
		}
		else if (CurrentViewModel == ReportsViewModel)
		{
			await ReportsViewModel.LoadAsync(cancellationToken);
		}
	}

	public string CurrentUserDisplayName => _authorizationService.CurrentUser?.DisplayName ?? string.Empty;

	public string CurrentUserRole => _authorizationService.CurrentUser switch
	{
		{ IsAdministrator: true } => "Administrator",
		{ CanApprovePurchaseOrders: true } => "Purchase Approver",
		_ => "User"
	};

	private void Logout()
	{
		_sessionService.Logout();
		LogoutRequested?.Invoke(this, EventArgs.Empty);
	}
}
