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
		SupplierService supplierService,
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
				itemService, manufacturerService, categoryService, unitOfMeasureService, packagingService, supplierService);

		MovementsViewModel =
			new MovementsViewModel(
				movementService,
				reasonCodeService);

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
				purposeService,
				reasonCodeService,
				manufacturerService,
				categoryService,
				unitOfMeasureService,
				packagingService,
				supplierService,
				warehouseService,
				storageLocationService,
				userService,
				settingsService,
				connectionStatusService,
				databaseConnectionTester,
				databaseManagementService,
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
		else if (CurrentViewModel == ReportsViewModel)
		{
			await ReportsViewModel.LoadAsync(cancellationToken);
		}
	}

	public string CurrentUserDisplayName => _authorizationService.CurrentUser?.DisplayName ?? string.Empty;

	public string CurrentUserRole => _authorizationService.CurrentUser?.IsAdministrator == true ? "Administrator" : "User";

	private void Logout()
	{
		_sessionService.Logout();
		LogoutRequested?.Invoke(this, EventArgs.Empty);
	}
}
