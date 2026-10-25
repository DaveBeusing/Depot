// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
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
		long storageLocationId)
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

		if (storageLocationId <= 0)
		{
			throw new ArgumentException(
				"Storage location id is required.",
				nameof(storageLocationId));
		}

		var inventory =
			_inventoryRepository.GetByItemPurposeLocation(
				itemId,
				purposeId,
				storageLocationId);

		if (inventory is not null)
		{
			return inventory;
		}

		inventory =
			new Inventory
			{
				ItemId = itemId,
				PurposeId = purposeId,
				StorageLocationId = storageLocationId,
				IsActive = true
			};

		inventory.Id =
			_inventoryRepository.Create(
				inventory);

		_auditService.RecordCreated(inventory.Id, inventory);

		return inventory;
	}

	internal async Task<Inventory> GetOrCreateForImportAsync(
		DatabaseTransactionContext transaction,
		long itemId,
		long purposeId,
		long storageLocationId,
		CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ImportManage);
		if (itemId <= 0) throw new ArgumentException("Item id is required.", nameof(itemId));
		if (purposeId <= 0) throw new ArgumentException("Purpose id is required.", nameof(purposeId));
		if (storageLocationId <= 0) throw new ArgumentException("Storage location id is required.", nameof(storageLocationId));
		if (!await _inventoryRepository.IsImportContextActiveAsync(transaction, itemId, purposeId, storageLocationId, cancellationToken))
			throw new InvalidOperationException("Import requires an active item, purpose, storage location, and warehouse.");
		var existing = await _inventoryRepository.GetByContextAsync(transaction, itemId, purposeId, storageLocationId, cancellationToken);
		if (existing is not null)
		{
			if (!existing.IsActive) throw new InvalidOperationException("The inventory is inactive and cannot be imported.");
			return existing;
		}

		var inventory = new Inventory
		{
			ItemId = itemId,
			PurposeId = purposeId,
			StorageLocationId = storageLocationId,
			IsActive = true
		};
		inventory.Id = await _inventoryRepository.CreateAsync(transaction, inventory, cancellationToken);
		await _auditService.RecordCreatedAsync(transaction, inventory.Id, inventory, cancellationToken);
		return inventory;
	}

	public async Task<Inventory> GetOrCreateInventoryAsync(
		long itemId,
		long purposeId,
		long storageLocationId,
		CancellationToken cancellationToken = default)
	{
		if (itemId <= 0) throw new ArgumentException("Item id is required.", nameof(itemId));
		if (purposeId <= 0) throw new ArgumentException("Purpose id is required.", nameof(purposeId));
		if (storageLocationId <= 0) throw new ArgumentException("Storage location id is required.", nameof(storageLocationId));

		var inventory = await _inventoryRepository.GetByContextAsync(
			itemId,
			purposeId,
			storageLocationId,
			cancellationToken);

		if (inventory is not null) return inventory;

		inventory = new Inventory
		{
			ItemId = itemId,
			PurposeId = purposeId,
			StorageLocationId = storageLocationId,
			IsActive = true
		};
		inventory.Id = await _inventoryRepository.CreateAsync(inventory, cancellationToken);
		await _auditService.RecordCreatedAsync(inventory.Id, inventory, cancellationToken);
		return inventory;
	}
}

