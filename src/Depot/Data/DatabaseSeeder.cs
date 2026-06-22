// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

namespace Depot.Data;

public sealed class DatabaseSeeder
{
	private readonly InventoryService _inventoryService;

	public DatabaseSeeder(
		InventoryService inventoryService)
	{
		_inventoryService = inventoryService;
	}

	public void Seed()
	{
		if (_inventoryService.GetItems().Count > 0)
		{
			return;
		}

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
	}
}