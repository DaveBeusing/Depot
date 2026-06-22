// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using Depot.Data;
using Depot.Repositories;
using Depot.Services;
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

	public static DatabaseSeeder DatabaseSeeder { get; private set; } = null!;

	public static MainViewModel MainViewModel { get; private set; } = null!;

	protected override void OnStartup(
		StartupEventArgs e)
	{
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
				StockMovementRepository);
	}
}