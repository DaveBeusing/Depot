// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

namespace Depot.Data;

public sealed class DatabaseSeeder
{
	private readonly InventoryService _inventoryService;
	private readonly StockService _stockService;

	public DatabaseSeeder(
		InventoryService inventoryService,
		StockService stockService)
	{
		_inventoryService = inventoryService;
		_stockService = stockService;
	}

	public void Seed()
	{
		if (_inventoryService.GetItems().Count > 0)
		{
			return;
		}

		var ssd1 =
			_inventoryService.CreateItem(
				"SSD-001",
				"Samsung SSD 1TB",
				"Samsung",
				"Storage");

		_inventoryService.CreateItem(
			"SSD-002",
			"Crucial SSD 2TB",
			"Crucial",
			"Storage");

		_inventoryService.CreateItem(
			"CABLE-001",
			"HDMI Cable 3m",
			"Generic",
			"Cables");

		_stockService.AddPurchase(
			ssd1.Id,
			100,
			1.00m,
			"INV-1000",
			"Initial demo purchase");

		_stockService.AddPurchase(
			ssd1.Id,
			50,
			0.50m,
			"INV-1001",
			"Second demo purchase");
	}
}