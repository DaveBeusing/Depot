// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class InventoryManagementService
{
	private readonly InventoryRepository _inventoryRepository;
	private readonly PurposeRepository _purposeRepository;

	public InventoryManagementService(
		InventoryRepository inventoryRepository,
		PurposeRepository purposeRepository)
	{
		_inventoryRepository = inventoryRepository;
		_purposeRepository = purposeRepository;
	}

	public Inventory GetOrCreateDefaultInventory(
		long itemId)
	{
		var purpose =
			_purposeRepository.GetOrCreate(
				"Stock",
				"Default stock purpose");

		return _inventoryRepository.GetOrCreate(
			itemId,
			purpose.Id);
	}
}