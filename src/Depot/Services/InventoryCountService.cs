// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class InventoryCountService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly InventoryCountRepository _counts;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _stockMovements;
	private readonly WarehouseRepository _warehouses;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;

	public InventoryCountService(
		IDatabaseTransactionRunner transactions,
		InventoryCountRepository counts,
		InventoryRepository inventories,
		StockMovementRepository stockMovements,
		WarehouseRepository warehouses,
		AuditRepository auditEntries,
		AuditService audit)
	{
		_transactions = transactions;
		_counts = counts;
		_inventories = inventories;
		_stockMovements = stockMovements;
		_warehouses = warehouses;
		_auditEntries = auditEntries;
		_audit = audit;
	}

	public Task<InventoryCount?> GetByIdAsync(
		long id,
		CancellationToken cancellationToken = default) =>
		_counts.GetByIdAsync(id, cancellationToken);

	public async Task<InventoryCount> SaveDraftAsync(
		InventoryCount count,
		CancellationToken cancellationToken = default)
	{
		NormalizeAndValidateDraft(count);
		var userId = _audit.CurrentUserId
			?? throw new InvalidOperationException("A signed-in user is required to save an inventory count.");
		return await _transactions.ExecuteAsync(
			(transaction, token) => SaveDraftAsync(transaction, count, userId, token),
			cancellationToken);
	}

	public async Task<InventoryCount> StartAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		ValidateId(id);
		if (_audit.CurrentUserId is null)
		{
			throw new InvalidOperationException("A signed-in user is required to start an inventory count.");
		}

		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await GetDraftForUpdateAsync(transaction, id, version, token);
				var warehouse = await _warehouses.GetByIdAsync(transaction, before.WarehouseId, token)
					?? throw new InvalidOperationException("The inventory-count warehouse was not found.");
				if (!warehouse.IsActive)
				{
					throw new InvalidOperationException("An inventory count cannot be started for an inactive warehouse.");
				}

				var inventoryIds = await _inventories.GetActiveIdsByWarehouseForUpdateAsync(
					transaction,
					before.WarehouseId,
					token);
				if (inventoryIds.Count == 0)
				{
					throw new InvalidOperationException("The warehouse has no active inventories to count.");
				}

				var quantities = (await _stockMovements.GetCurrentQuantitiesAsync(
					transaction,
					inventoryIds,
					token)).ToDictionary(stock => stock.InventoryId, stock => stock.Quantity);
				var lines = new List<InventoryCountLine>(inventoryIds.Count);
				foreach (var inventoryId in inventoryIds)
				{
					var line = new InventoryCountLine
					{
						InventoryCountId = before.Id,
						InventoryId = inventoryId,
						ExpectedQuantity = quantities.GetValueOrDefault(inventoryId)
					};
					line.Id = await _counts.CreateLineAsync(transaction, line, token);
					lines.Add(line);
				}

				var startedAtUtc = DateTime.UtcNow;
				if (!await _counts.StartAsync(transaction, id, version, startedAtUtc, token))
				{
					throw new ConcurrencyConflictException("inventory count");
				}

				var after = Copy(before);
				after.Status = InventoryCountStatus.Counting;
				after.StartedAtUtc = startedAtUtc;
				after.Version++;
				after.Lines = lines;
				await _auditEntries.CreateAsync(
					transaction,
					_audit.CreateUpdatedEntry(id, before, after),
					token);
				return after;
			},
			cancellationToken);
	}

	public async Task<InventoryCountLine> RecordCountAsync(
		long inventoryCountId,
		long lineId,
		long lineVersion,
		long countedQuantity,
		CancellationToken cancellationToken = default)
	{
		ValidateId(inventoryCountId);
		ValidateId(lineId);
		if (countedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(countedQuantity));
		var userId = _audit.CurrentUserId
			?? throw new InvalidOperationException("A signed-in user is required to record an inventory count.");

		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var count = await _counts.GetByIdForUpdateAsync(transaction, inventoryCountId, token)
					?? throw new InvalidOperationException("The inventory count was not found.");
				if (count.Status != InventoryCountStatus.Counting)
				{
					throw new InvalidOperationException("Quantities can only be recorded while an inventory count is in Counting status.");
				}

				var before = count.Lines.FirstOrDefault(line => line.Id == lineId)
					?? throw new InvalidOperationException("The inventory-count line was not found.");
				if (before.Version != lineVersion)
				{
					throw new ConcurrencyConflictException("inventory-count line");
				}

				var after = Copy(before);
				after.CountedQuantity = countedQuantity;
				after.CountedByUserId = userId;
				after.CountedAtUtc = DateTime.UtcNow;
				if (!await _counts.UpdateCountedQuantityAsync(transaction, after, token))
				{
					throw new ConcurrencyConflictException("inventory-count line");
				}
				after.Version++;
				await _auditEntries.CreateAsync(
					transaction,
					_audit.CreateUpdatedEntry(lineId, before, after),
					token);
				return after;
			},
			cancellationToken);
	}

	public Task<InventoryCount> MoveToReviewAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(
			id,
			version,
			InventoryCountStatus.Counting,
			InventoryCountStatus.Review,
			count =>
			{
				if (count.Lines.Any(line => line.CountedQuantity is null))
				{
					throw new InvalidOperationException("Every inventory-count line must be counted before review.");
				}
			},
			cancellationToken);

	public Task<InventoryCount> CancelAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(
			id,
			version,
			InventoryCountStatus.Draft,
			InventoryCountStatus.Cancelled,
			_ => { },
			cancellationToken);

	private async Task<InventoryCount> SaveDraftAsync(
		DatabaseTransactionContext transaction,
		InventoryCount count,
		long userId,
		CancellationToken cancellationToken)
	{
		var warehouse = await _warehouses.GetByIdAsync(transaction, count.WarehouseId, cancellationToken)
			?? throw new InvalidOperationException("The inventory-count warehouse was not found.");
		if (!warehouse.IsActive)
		{
			throw new InvalidOperationException("An inventory count requires an active warehouse.");
		}

		InventoryCount? before = null;
		if (count.Id == 0)
		{
			count.CountNumber = $"PENDING-{Guid.NewGuid():N}";
			count.CreatedAtUtc = DateTime.UtcNow;
			count.CreatedByUserId = userId;
			count.StartedAtUtc = null;
			count.CompletedAtUtc = null;
			count.PostedByUserId = null;
			count.Lines = [];
			count.Id = await _counts.CreateAsync(transaction, count, cancellationToken);
			count.CountNumber = $"IC-{count.Id:000000}";
			if (await _counts.UpdateCountNumberAsync(
				transaction,
				count.Id,
				count.CountNumber,
				cancellationToken) != 1)
			{
				throw new ConcurrencyConflictException("inventory count number");
			}
		}
		else
		{
			before = await GetDraftForUpdateAsync(transaction, count.Id, count.Version, cancellationToken);
			count.CountNumber = before.CountNumber;
			count.CreatedAtUtc = before.CreatedAtUtc;
			count.CreatedByUserId = before.CreatedByUserId;
			count.StartedAtUtc = null;
			count.CompletedAtUtc = null;
			count.PostedByUserId = null;
			count.Lines = [];
			if (!await _counts.UpdateDraftAsync(transaction, count, cancellationToken))
			{
				throw new ConcurrencyConflictException("inventory count");
			}
			count.Version++;
		}

		await _auditEntries.CreateAsync(
			transaction,
			before is null
				? _audit.CreateCreatedEntry(count.Id, count)
				: _audit.CreateUpdatedEntry(count.Id, before, count),
			cancellationToken);
		return count;
	}

	private async Task<InventoryCount> ChangeStatusAsync(
		long id,
		long version,
		InventoryCountStatus expectedStatus,
		InventoryCountStatus status,
		Action<InventoryCount> validate,
		CancellationToken cancellationToken)
	{
		ValidateId(id);
		if (_audit.CurrentUserId is null)
		{
			throw new InvalidOperationException("A signed-in user is required to change an inventory-count status.");
		}

		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await _counts.GetByIdForUpdateAsync(transaction, id, token)
					?? throw new InvalidOperationException("The inventory count was not found.");
				if (before.Version != version)
				{
					throw new ConcurrencyConflictException("inventory count");
				}
				if (before.Status != expectedStatus)
				{
					throw new InvalidOperationException(
						$"Only an inventory count in {expectedStatus} status can be changed to {status}.");
				}
				validate(before);
				if (!await _counts.SetStatusAsync(transaction, id, version, expectedStatus, status, token))
				{
					throw new ConcurrencyConflictException("inventory count");
				}

				var after = Copy(before);
				after.Status = status;
				after.Version++;
				await _auditEntries.CreateAsync(
					transaction,
					_audit.CreateUpdatedEntry(id, before, after),
					token);
				return after;
			},
			cancellationToken);
	}

	private async Task<InventoryCount> GetDraftForUpdateAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		CancellationToken cancellationToken)
	{
		var count = await _counts.GetByIdForUpdateAsync(transaction, id, cancellationToken)
			?? throw new InvalidOperationException("The inventory count was not found.");
		if (count.Version != version)
		{
			throw new ConcurrencyConflictException("inventory count");
		}
		if (count.Status != InventoryCountStatus.Draft)
		{
			throw new InvalidOperationException("Only draft inventory counts can be edited, started, or cancelled.");
		}
		return count;
	}

	private static void NormalizeAndValidateDraft(InventoryCount count)
	{
		if (count.Status != InventoryCountStatus.Draft)
		{
			throw new InvalidOperationException("Only draft inventory counts can be saved.");
		}
		if (count.WarehouseId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(count), "A warehouse is required.");
		}
		count.Notes = string.IsNullOrWhiteSpace(count.Notes) ? null : count.Notes.Trim();
		if (count.Notes?.Length > 4000)
		{
			throw new ArgumentException("Notes must not exceed 4000 characters.", nameof(count));
		}
	}

	private static void ValidateId(long id)
	{
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
	}

	private static InventoryCount Copy(InventoryCount source) => new()
	{
		Id = source.Id,
		CountNumber = source.CountNumber,
		WarehouseId = source.WarehouseId,
		Status = source.Status,
		CreatedAtUtc = source.CreatedAtUtc,
		StartedAtUtc = source.StartedAtUtc,
		CompletedAtUtc = source.CompletedAtUtc,
		CreatedByUserId = source.CreatedByUserId,
		PostedByUserId = source.PostedByUserId,
		Notes = source.Notes,
		Version = source.Version,
		Lines = source.Lines.Select(Copy).ToArray()
	};

	private static InventoryCountLine Copy(InventoryCountLine source) => new()
	{
		Id = source.Id,
		InventoryCountId = source.InventoryCountId,
		InventoryId = source.InventoryId,
		ExpectedQuantity = source.ExpectedQuantity,
		CountedQuantity = source.CountedQuantity,
		CountedByUserId = source.CountedByUserId,
		CountedAtUtc = source.CountedAtUtc,
		Version = source.Version
	};
}
