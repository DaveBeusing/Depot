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
			_purposeRepository.GetByName(
				"Stock");

		if (purpose is null)
		{
			purpose = new Purpose
			{
				Name = "Stock",
				Description = "Default stock purpose",
				IsActive = true
			};

			purpose.Id =
				_purposeRepository.Create(
					purpose);
		}
		return _inventoryRepository.GetOrCreate(
			itemId,
			purpose.Id);
	}
}