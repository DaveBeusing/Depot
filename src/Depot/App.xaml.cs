// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using Depot.Data;
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

	public static StockMovementRepository StockMovementRepository { get; private set; } = null!;

	public static InventoryService InventoryService { get; private set; } = null!;

	public static StockService StockService { get; private set; } = null!;

	public static ImportService ImportService { get; private set; } = null!;

	public static DatabaseSeeder DatabaseSeeder { get; private set; } = null!;

	public static MainViewModel MainViewModel { get; private set; } = null!;

	protected override void OnStartup(
		StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;

		base.OnStartup(e);

		ConnectionFactory =
			new SqliteConnectionFactory(
				"depot.db");

		Database =
			new DepotDatabase(
				ConnectionFactory);

		Database.Initialize();

		ItemRepository =
			new ItemRepository(
				ConnectionFactory);

		StockMovementRepository =
			new StockMovementRepository(
				ConnectionFactory);

		InventoryService =
			new InventoryService(
				ItemRepository);

		StockService =
			new StockService(
				ItemRepository,
				StockMovementRepository);

		ImportService =
			new ImportService(
				ItemRepository,
				InventoryService,
				StockService);

		DatabaseSeeder =
			new DatabaseSeeder(
				InventoryService,
				StockService);

		DatabaseSeeder.Seed();

		MainViewModel =
			new MainViewModel(
				InventoryService,
				StockService,
				ItemRepository,
				StockMovementRepository,
				ImportService);
	}

	private static void OnDispatcherUnhandledException(
		object sender,
		System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		MessageBox.Show(
			e.Exception.ToString(),
			"Unhandled Error",
			MessageBoxButton.OK,
			MessageBoxImage.Error);

		e.Handled = true;
	}
}