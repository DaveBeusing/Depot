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

	public static SqliteConnectionFactory ConnectionFactory { get; private set; } = null!;
	public static DepotDatabase Database { get; private set; } = null!;
	public static ItemRepository ItemRepository { get; private set; } = null!;
	public static PurposeRepository PurposeRepository { get; private set; } = null!;
	public static InventoryRepository InventoryRepository { get; private set; } = null!;
	public static LocationRepository LocationRepository { get; private set; } = null!;
	public static StockMovementRepository StockMovementRepository { get; private set; } = null!;
	public static UserRepository UserRepository { get; private set; } = null!;
	public static AuthorizationService AuthorizationService { get; private set; } = null!;
	public static PasswordHasher PasswordHasher { get; private set; } = null!;
	public static AuthenticationService AuthenticationService { get; private set; } = null!;
	public static SessionService SessionService { get; private set; } = null!;
	public static ItemService ItemService { get; private set; } = null!;
	public static PurposeService PurposeService { get; private set; } = null!;
	public static LocationService LocationService { get; private set; } = null!;
	public static UserService UserService { get; private set; } = null!;
	public static MovementService MovementService { get; private set; } = null!;
	public static StockService StockService { get; private set; } = null!;
	public static ReportService ReportService { get; private set; } = null!;
	public static InventoryManagementService InventoryManagementService { get; private set; } = null!;
	public static ImportService ImportService { get; private set; } = null!;
	public static IFileDialogService FileDialogService { get; } = new FileDialogService();
	public static DatabaseSeeder DatabaseSeeder { get; private set; } = null!;
	public static MainViewModel MainViewModel { get; private set; } = null!;


	protected override void OnStartup(StartupEventArgs e)
	{
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		try
		{
			base.OnStartup(e);
			StartupDiagnostics.Log("Application startup.");
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
		ConnectionFactory = new SqliteConnectionFactory("depot.db");
		Database = new DepotDatabase(ConnectionFactory);
		Database.Initialize();

		StartupDiagnostics.Log("Database initialized.");

		ItemRepository =
			new ItemRepository(
				ConnectionFactory);

		PurposeRepository =
			new PurposeRepository(
				ConnectionFactory);

		InventoryRepository =
			new InventoryRepository(
				ConnectionFactory);

		LocationRepository =
			new LocationRepository(
				ConnectionFactory);

		StockMovementRepository =
			new StockMovementRepository(
				ConnectionFactory);

		UserRepository =
			new UserRepository(
				ConnectionFactory);

		StartupDiagnostics.Log(
			"Repositories created.");

		AuthorizationService =
			new AuthorizationService();

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

		ItemService =
			new ItemService(
				ItemRepository);

		PurposeService =
			new PurposeService(
				PurposeRepository);

		LocationService =
			new LocationService(
				LocationRepository);

		UserService =
			new UserService(
				UserRepository,
				PasswordHasher,
				AuthorizationService);

		MovementService =
			new MovementService(
				ItemRepository,
				InventoryRepository,
				PurposeRepository,
				LocationRepository,
				StockMovementRepository);

		StockService =
			new StockService(
				ItemRepository,
				InventoryRepository,
				PurposeRepository,
				LocationRepository,
				StockMovementRepository);

		ReportService =
			new ReportService(
				StockService);

		InventoryManagementService =
			new InventoryManagementService(
				InventoryRepository);

		ImportService =
			new ImportService(
				ItemRepository,
				ItemService,
				PurposeService,
				LocationService,
				InventoryManagementService,
				MovementService);

		StartupDiagnostics.Log(
			"Services created.");

		// DatabaseSeeder =
		//	new DatabaseSeeder(
		//		ItemService,
		//		PurposeService,
		//		LocationService,
		//		InventoryManagementService,
		//		MovementService);
		//
		// DatabaseSeeder.Seed();
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
		var loginViewModel = new LoginViewModel(AuthenticationService);
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
				LocationService,
				UserService,
				AuthorizationService,
				SessionService,
				ImportService,
				FileDialogService);

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
}
