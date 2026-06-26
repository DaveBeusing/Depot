// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

namespace Depot.Data;

public sealed class DatabaseSeeder
{
	private readonly ItemService _itemService;
	private readonly MovementService _movementService;

	public DatabaseSeeder(
		ItemService itemService,
		MovementService movementService)
	{
		_itemService = itemService;
		_movementService = movementService;
	}

	public void Seed()
	{
		if (_itemService.GetItems().Count > 0)
		{
			return;
		}

		var ssd1 =
			_itemService.CreateItem(
				"SSD-001",
				"Samsung SSD 1TB",
				"Samsung",
				"Storage");

		_itemService.CreateItem(
			"SSD-002",
			"Crucial SSD 2TB",
			"Crucial",
			"Storage");

		_itemService.CreateItem(
			"CABLE-001",
			"HDMI Cable 3m",
			"Generic",
			"Cables");

		_movementService.AddPurchase(
			ssd1.Id,
			100,
			1.00m,
			"INV-1000",
			"Initial demo purchase");

		_movementService.AddPurchase(
			ssd1.Id,
			50,
			0.50m,
			"INV-1001",
			"Second demo purchase");

		_movementService.AddWithdrawal(
			ssd1.Id,
			10,
			"LAB-001",
			"Demo withdrawal");

		_movementService.AddCorrection(
			ssd1.Id,
			-2,
			"INV-COUNT",
			"Inventory adjustment");
	}
}