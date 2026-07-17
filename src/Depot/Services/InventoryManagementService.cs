// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

/// <summary>
/// Provides business logic for inventory management.
/// </summary>
public sealed class InventoryManagementService
{
	private readonly InventoryRepository _inventoryRepository;
	private readonly AuditService _auditService;

	public InventoryManagementService(
		InventoryRepository inventoryRepository,
		AuditService auditService)
	{
		_inventoryRepository = inventoryRepository;
		_auditService = auditService;
	}

	public Inventory GetOrCreateInventory(
		long itemId,
		long purposeId,
		long locationId)
	{
		if (itemId <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(itemId));
		}

		if (purposeId <= 0)
		{
			throw new ArgumentException(
				"Purpose id is required.",
				nameof(purposeId));
		}

		if (locationId <= 0)
		{
			throw new ArgumentException(
				"Location id is required.",
				nameof(locationId));
		}

		var inventory =
			_inventoryRepository.GetByItemPurposeLocation(
				itemId,
				purposeId,
				locationId);

		if (inventory is not null)
		{
			return inventory;
		}

		inventory =
			new Inventory
			{
				ItemId = itemId,
				PurposeId = purposeId,
				LocationId = locationId,
				IsActive = true
			};

		inventory.Id =
			_inventoryRepository.Create(
				inventory);

		_auditService.RecordCreated(inventory.Id, inventory);

		return inventory;
	}

	public async Task<Inventory> GetOrCreateInventoryAsync(
		long itemId,
		long purposeId,
		long locationId,
		CancellationToken cancellationToken = default)
	{
		if (itemId <= 0) throw new ArgumentException("Item id is required.", nameof(itemId));
		if (purposeId <= 0) throw new ArgumentException("Purpose id is required.", nameof(purposeId));
		if (locationId <= 0) throw new ArgumentException("Location id is required.", nameof(locationId));

		var inventory = await _inventoryRepository.GetByContextAsync(
			itemId,
			purposeId,
			locationId,
			cancellationToken);

		if (inventory is not null) return inventory;

		inventory = new Inventory
		{
			ItemId = itemId,
			PurposeId = purposeId,
			LocationId = locationId,
			IsActive = true
		};
		inventory.Id = await _inventoryRepository.CreateAsync(inventory, cancellationToken);
		await _auditService.RecordCreatedAsync(inventory.Id, inventory, cancellationToken);
		return inventory;
	}
}
