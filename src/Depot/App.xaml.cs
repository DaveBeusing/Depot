// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Markup;

using Depot.Data;
using Depot.Diagnostics;
using Depot.Repositories;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels;
using Depot.ViewModels.Login;
using Depot.Views.Login;

namespace Depot;

public partial class App : Application
{
	static App()
	{
		FrameworkElement.LanguageProperty.OverrideMetadata(
			typeof(FrameworkElement),
			new FrameworkPropertyMetadata(
				XmlLanguage.GetLanguage("de-DE")));
	}

	public static IDatabaseConnectionFactory ConnectionFactory { get; private set; } = null!;
	public static DatabaseAccess DataAccess { get; private set; } = null!;
	public static IDatabaseInitializer Database { get; private set; } = null!;
	public static ItemRepository ItemRepository { get; private set; } = null!;
	public static PurposeRepository PurposeRepository { get; private set; } = null!;
	public static ReasonCodeRepository ReasonCodeRepository { get; private set; } = null!;
	public static ManufacturerRepository ManufacturerRepository { get; private set; } = null!;
	public static CategoryRepository CategoryRepository { get; private set; } = null!;
	public static UnitOfMeasureRepository UnitOfMeasureRepository { get; private set; } = null!;
	public static PackagingRepository PackagingRepository { get; private set; } = null!;
	public static SupplierCategoryRepository SupplierCategoryRepository { get; private set; } = null!;
	public static SupplierRepository SupplierRepository { get; private set; } = null!;
	public static SupplierItemRepository SupplierItemRepository { get; private set; } = null!;
	public static PurchaseOrderRepository PurchaseOrderRepository { get; private set; } = null!;
	public static GoodsReceiptRepository GoodsReceiptRepository { get; private set; } = null!;
	public static InventoryRepository InventoryRepository { get; private set; } = null!;
	public static WarehouseRepository WarehouseRepository { get; private set; } = null!;
	public static StorageLocationRepository StorageLocationRepository { get; private set; } = null!;
	public static StockMovementRepository StockMovementRepository { get; private set; } = null!;
	public static UserRepository UserRepository { get; private set; } = null!;
	public static AuditRepository AuditRepository { get; private set; } = null!;
	public static SettingsRepository SettingsRepository { get; private set; } = null!;
	public static AuthorizationService AuthorizationService { get; private set; } = null!;
	public static PasswordHasher PasswordHasher { get; private set; } = null!;
	public static AuthenticationService AuthenticationService { get; private set; } = null!;
	public static SessionService SessionService { get; private set; } = null!;
	public static SettingsService SettingsService { get; private set; } = null!;
	public static ConnectionStatusService ConnectionStatusService { get; private set; } = null!;
	public static DatabaseConnectionTester DatabaseConnectionTester { get; private set; } = null!;
	public static DatabaseManagementService DatabaseManagementService { get; private set; } = null!;
	public static DatabaseBackupScheduler DatabaseBackupScheduler { get; private set; } = null!;
	public static AuditService AuditService { get; private set; } = null!;
	public static ItemService ItemService { get; private set; } = null!;
	public static PurposeService PurposeService { get; private set; } = null!;
	public static ReasonCodeService ReasonCodeService { get; private set; } = null!;
	public static ManufacturerService ManufacturerService { get; private set; } = null!;
	public static CategoryService CategoryService { get; private set; } = null!;
	public static UnitOfMeasureService UnitOfMeasureService { get; private set; } = null!;
	public static PackagingService PackagingService { get; private set; } = null!;
	public static SupplierCategoryService SupplierCategoryService { get; private set; } = null!;
	public static SupplierService SupplierService { get; private set; } = null!;
	public static SupplierItemService SupplierItemService { get; private set; } = null!;
	public static PurchaseOrderService PurchaseOrderService { get; private set; } = null!;
	public static GoodsReceiptService GoodsReceiptService { get; private set; } = null!;
	public static WarehouseService WarehouseService { get; private set; } = null!;
	public static StorageLocationService StorageLocationService { get; private set; } = null!;
	public static UserService UserService { get; private set; } = null!;
	public static MovementService MovementService { get; private set; } = null!;
	public static StockService StockService { get; private set; } = null!;
	public static ReportService ReportService { get; private set; } = null!;
	public static InventoryManagementService InventoryManagementService { get; private set; } = null!;
	public static ImportService ImportService { get; private set; } = null!;
	public static ApplicationInformationService ApplicationInformationService { get; } =
		new(typeof(App).Assembly);
	public static IFileDialogService FileDialogService { get; } = new FileDialogService();
	public static MainViewModel MainViewModel { get; private set; } = null!;


	protected override void OnStartup(StartupEventArgs e)
	{
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		try
		{
			base.OnStartup(e);
			StartupDiagnostics.Log(
				$"Application startup. Version {ApplicationInformationService.GetVersionInfo().InformationalVersion}.");
			InitializeInfrastructure();
			RunApplication();
		}
		catch (Exception ex)
		{
			StartupDiagnostics.LogException(ex);
			StartupDiagnostics.ShowStartupError(ex);
			Shutdown();
		}
	}

	private static void InitializeInfrastructure()
	{
		SettingsRepository = new SettingsRepository("depot.settings");
		SettingsService = new SettingsService(SettingsRepository);
		ConnectionStatusService = new ConnectionStatusService();
		var connectionSettings = SettingsService.LoadOrCreate();

		ConnectionFactory = DatabaseProviderFactory.CreateConnectionFactory(connectionSettings);
		DataAccess = new DatabaseAccess(ConnectionFactory);
		Database = DatabaseProviderFactory.CreateInitializer(ConnectionFactory);
		Database.Initialize();
		ConnectionStatusService.SetConnected(connectionSettings);
		DatabaseConnectionTester = new DatabaseConnectionTester();
		DatabaseManagementService = new DatabaseManagementService(ConnectionFactory, SettingsService);
		DatabaseBackupScheduler = new DatabaseBackupScheduler(DatabaseManagementService, SettingsService);
		DatabaseBackupScheduler.Start();

		StartupDiagnostics.Log("Database initialized.");

		ItemRepository =
			new ItemRepository(
				DataAccess);

		PurposeRepository =
			new PurposeRepository(
				DataAccess);

		ReasonCodeRepository =
			new ReasonCodeRepository(
				DataAccess);
		ManufacturerRepository = new ManufacturerRepository(DataAccess);
		CategoryRepository = new CategoryRepository(DataAccess);
		UnitOfMeasureRepository = new UnitOfMeasureRepository(DataAccess);
		PackagingRepository = new PackagingRepository(DataAccess);
		SupplierCategoryRepository = new SupplierCategoryRepository(DataAccess);
		SupplierRepository = new SupplierRepository(DataAccess);
		SupplierItemRepository = new SupplierItemRepository(DataAccess);
		PurchaseOrderRepository = new PurchaseOrderRepository(DataAccess);
		GoodsReceiptRepository = new GoodsReceiptRepository(DataAccess);

		InventoryRepository =
			new InventoryRepository(
				DataAccess);

		WarehouseRepository =
			new WarehouseRepository(
				DataAccess);

		StorageLocationRepository =
			new StorageLocationRepository(
				DataAccess);

		StockMovementRepository =
			new StockMovementRepository(
				DataAccess);

		UserRepository =
			new UserRepository(
				DataAccess);

		AuditRepository =
			new AuditRepository(
				DataAccess);

		StartupDiagnostics.Log(
			"Repositories created.");

		AuthorizationService =
			new AuthorizationService();

		AuditService =
			new AuditService(
				AuditRepository,
				AuthorizationService);

		PasswordHasher =
			new PasswordHasher();

		AuthenticationService =
			new AuthenticationService(
				UserRepository,
				PasswordHasher,
				AuthorizationService);

		SessionService =
			new SessionService(
				AuthorizationService);

		ManufacturerService = new ManufacturerService(ManufacturerRepository, AuditService);
		CategoryService = new CategoryService(CategoryRepository, AuditService);
		UnitOfMeasureService = new UnitOfMeasureService(UnitOfMeasureRepository, AuditService);
		PackagingService = new PackagingService(PackagingRepository, AuditService);
		SupplierCategoryService = new SupplierCategoryService(SupplierCategoryRepository, AuditService);
		SupplierService = new SupplierService(SupplierRepository, SupplierItemRepository, SupplierCategoryRepository, AuditService);
		SupplierItemService = new SupplierItemService(SupplierItemRepository, SupplierRepository, ItemRepository, AuditService);
		PurchaseOrderService = new PurchaseOrderService(PurchaseOrderRepository, SupplierRepository, ItemRepository, AuditService);
		GoodsReceiptService = new GoodsReceiptService(GoodsReceiptRepository, AuditService);

		ItemService = new ItemService(ItemRepository, AuditService, ManufacturerService, CategoryService, UnitOfMeasureService, PackagingService, SupplierItemRepository);

		PurposeService =
			new PurposeService(
				PurposeRepository,
				AuditService);

		ReasonCodeService =
			new ReasonCodeService(
				ReasonCodeRepository,
				AuditService);

		WarehouseService =
			new WarehouseService(
				WarehouseRepository,
				StorageLocationRepository,
				AuditService);

		StorageLocationService =
			new StorageLocationService(
				StorageLocationRepository,
				WarehouseRepository,
				AuditService);

		UserService =
			new UserService(
				UserRepository,
				PasswordHasher,
				AuthorizationService,
				AuditService);

		MovementService =
			new MovementService(
				ItemRepository,
				InventoryRepository,
				PurposeRepository,
				StorageLocationRepository,
				WarehouseRepository,
				ReasonCodeRepository,
				StockMovementRepository,
				AuditService);

		StockService =
			new StockService(
				ItemRepository,
				InventoryRepository,
				PurposeRepository,
				StorageLocationRepository,
				WarehouseRepository,
				StockMovementRepository);

		ReportService =
			new ReportService(
				StockService);

		InventoryManagementService =
			new InventoryManagementService(
				InventoryRepository,
				AuditService);

		ImportService =
			new ImportService(
				ItemRepository,
				ItemService,
				PurposeService,
				WarehouseService,
				StorageLocationService,
				InventoryManagementService,
				MovementService);

		StartupDiagnostics.Log(
			"Services created.");

	}

	private void RunApplication()
	{
		while (true)
		{
			SessionService.Reset();
			if (!ShowLogin())
			{
				Shutdown();
				return;
			}
			ShowMainWindow();
			if (!SessionService.LogoutRequestedByUser)
			{
				Shutdown();
				return;
			}
			StartupDiagnostics.Log("Restarting session.");
		}
	}

	private static bool ShowLogin()
	{
		var loginViewModel = new LoginViewModel(
			AuthenticationService,
			ConnectionStatusService);
		var loginWindow = new LoginWindow(loginViewModel);
		StartupDiagnostics.Log("Showing login dialog.");
		var result = loginWindow.ShowDialog();
		StartupDiagnostics.Log($"Login dialog returned: {result}");
		return result == true;
	}

	private void ShowMainWindow()
	{
		MainViewModel =
			new MainViewModel(
				ItemService,
				StockService,
				MovementService,
				ReportService,
				PurposeService,
				ReasonCodeService,
				ManufacturerService,
				CategoryService,
				UnitOfMeasureService,
				PackagingService,
				SupplierCategoryService,
				SupplierService,
				SupplierItemService,
				PurchaseOrderService,
				GoodsReceiptService,
				WarehouseService,
				StorageLocationService,
				UserService,
				AuthorizationService,
				SessionService,
				ImportService,
				FileDialogService,
				SettingsService,
				ConnectionStatusService,
				DatabaseConnectionTester,
				DatabaseManagementService,
				ApplicationInformationService);

		StartupDiagnostics.Log("MainViewModel created.");

		var mainWindow =
			new MainWindow
			{
				DataContext = MainViewModel
			};

		MainWindow = mainWindow;
		StartupDiagnostics.Log("MainWindow created.");
		MainViewModel.LogoutRequested += OnLogoutRequested;
		try
		{
			mainWindow.ShowDialog();
		}
		finally
		{
			MainViewModel.LogoutRequested -= OnLogoutRequested;
		}
		StartupDiagnostics.Log("MainWindow closed.");

		void OnLogoutRequested(object? sender, EventArgs e)
		{
			mainWindow.Close();
		}
	}

	private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		StartupDiagnostics.LogException(e.Exception);
		StartupDiagnostics.ShowRuntimeError(e.Exception);
		e.Handled = true;
	}

	protected override void OnExit(ExitEventArgs e)
	{
		DatabaseBackupScheduler?.Dispose();
		base.OnExit(e);
	}
}
