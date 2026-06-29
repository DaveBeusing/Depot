// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;

using Depot.Data;
using Depot.Diagnostics;
using Depot.Repositories;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels;

namespace Depot;

public partial class App
	: Application
{
	public static SqliteConnectionFactory ConnectionFactory { get; private set; } = null!;

	public static DepotDatabase Database { get; private set; } = null!;

	public static ItemRepository ItemRepository { get; private set; } = null!;

	public static PurposeRepository PurposeRepository { get; private set; } = null!;

	public static InventoryRepository InventoryRepository { get; private set; } = null!;

	public static LocationRepository LocationRepository { get; private set; } = null!;

	public static StockMovementRepository StockMovementRepository { get; private set; } = null!;

	public static ItemService ItemService { get; private set; } = null!;

	public static StockService StockService { get; private set; } = null!;

	public static MovementService MovementService { get; private set; } = null!;

	public static InventoryManagementService InventoryManagementService { get; private set; } = null!;

	public static ImportService ImportService { get; private set; } = null!;

	public static PurposeService PurposeService { get; private set; } = null!;

	public static LocationService LocationService { get; private set; } = null!;

	public static DatabaseSeeder DatabaseSeeder { get; private set; } = null!;

	public static MainViewModel MainViewModel { get; private set; } = null!;

	protected override void OnStartup(StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		try
		{
			base.OnStartup(e);

			StartupDiagnostics.Log("Application startup.");

			ConnectionFactory =
				new SqliteConnectionFactory(
					"depot.db");

			Database =
				new DepotDatabase(
					ConnectionFactory);

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

			StartupDiagnostics.Log("Repositories created.");

			ItemService =
				new ItemService(
					ItemRepository);

			MovementService =
				new MovementService(
					ItemRepository,
					StockMovementRepository);

			StockService =
				new StockService(
					ItemRepository,
					StockMovementRepository);

			InventoryManagementService =
				new InventoryManagementService(
					InventoryRepository,
					PurposeRepository);

			PurposeService =
				new PurposeService(
					PurposeRepository);

			ImportService =
				new ImportService(
					ItemRepository,
					ItemService,
					MovementService);

			LocationService =
				new LocationService(
					LocationRepository);

			StartupDiagnostics.Log("Services created.");

			// DatabaseSeeder =
			//	new DatabaseSeeder(
			//		ItemService,
			//		MovementService);
			//
			// DatabaseSeeder.Seed();

			MainViewModel =
				new MainViewModel(
					ItemService,
					StockService,
					MovementService,
					PurposeService,
					ItemRepository,
					StockMovementRepository,
					LocationService,
					ImportService);

			StartupDiagnostics.Log("MainViewModel created.");

			var mainWindow =
				new MainWindow
				{
					DataContext =
						MainViewModel
				};

			StartupDiagnostics.Log("MainWindow created.");

			mainWindow.Show();

			StartupDiagnostics.Log("Application started.");
		}
		catch (Exception ex)
		{
			StartupDiagnostics.LogException(ex);
			StartupDiagnostics.ShowStartupError(ex);
			Shutdown();
		}
	}

	private static void OnDispatcherUnhandledException(
		object sender,
		System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		StartupDiagnostics.LogException(
			e.Exception);

		StartupDiagnostics.ShowRuntimeError(
			e.Exception);

		e.Handled = true;
	}
}