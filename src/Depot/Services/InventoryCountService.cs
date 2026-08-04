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
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly WarehouseRepository _warehouses;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly StockMovementReversalService _reversals;
	private readonly NotificationService _notifications;

	public InventoryCountService(
		IDatabaseTransactionRunner transactions,
		InventoryCountRepository counts,
		InventoryRepository inventories,
		StockMovementRepository stockMovements,
		ReasonCodeRepository reasonCodes,
		WarehouseRepository warehouses,
		AuditRepository auditEntries,
		AuditService audit,
		StockMovementReversalService reversals,
		NotificationService notifications)
	{
		_transactions = transactions;
		_counts = counts;
		_inventories = inventories;
		_stockMovements = stockMovements;
		_reasonCodes = reasonCodes;
		_warehouses = warehouses;
		_auditEntries = auditEntries;
		_audit = audit;
		_reversals = reversals;
		_notifications = notifications;
	}

	public async Task<InventoryCount> ReverseAsync(long id, long version, long reasonCodeId, string reversalReason, CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsReverse);
		ValidateId(id);
		var userId = _reversals.RequireUser();
		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await _counts.GetHeaderByIdForUpdateAsync(transaction, id, token)
					?? throw new InvalidOperationException("The inventory count was not found.");
				if (before.Version != version) throw new ConcurrencyConflictException("inventory count");
				if (before.Status != InventoryCountStatus.Posted || before.IsReversed)
				{
					throw new InvalidOperationException("Only a posted, unreversed inventory count can be reversed.");
				}
				before.Lines = await _counts.ListLinesAsync(transaction, id, token);
				var originals = await _stockMovements.ListOriginalsByReferenceAsync(transaction, before.CountNumber, token);
				if (originals.Any(movement => movement.MovementType != StockMovementType.Correction))
				{
					throw new InvalidOperationException("The inventory-count correction movements are inconsistent.");
				}
				await _reversals.CreateReversalsAsync(transaction, originals, reasonCodeId, reversalReason, userId, token);
				var reversedAtUtc = DateTime.UtcNow;
				var normalizedReason = reversalReason.Trim();
				if (!await _counts.MarkReversedAsync(transaction, id, version, reversedAtUtc, userId, normalizedReason, token))
				{
					throw new ConcurrencyConflictException("inventory count");
				}
				var after = Copy(before);
				after.ReversedAtUtc = reversedAtUtc;
				after.ReversedByUserId = userId;
				after.ReversalReason = normalizedReason;
				after.Version++;
				await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
				return after;
			},
			cancellationToken);
	}

	public Task<InventoryCount?> GetByIdAsync(
		long id,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsView);
		return _counts.GetByIdAsync(id, cancellationToken);
	}

	public Task<InventoryCount?> GetHeaderByIdAsync(
		long id,
		CancellationToken cancellationToken = default) =>
		_counts.GetHeaderByIdAsync(id, cancellationToken);

	public Task<PageResult<InventoryCountOverviewItem>> SearchAsync(
		string? searchText,
		InventoryCountStatus? status,
		long? warehouseId,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsView);
		return _counts.SearchAsync(searchText, status, warehouseId, pageNumber, pageSize, cancellationToken);
	}

	public Task<InventoryCountOverviewItem?> GetOverviewByIdAsync(
		long id,
		CancellationToken cancellationToken = default) =>
		_counts.GetOverviewByIdAsync(id, cancellationToken);

	public Task<PageResult<InventoryCountLineDetails>> SearchLinesAsync(
		long inventoryCountId,
		string? searchText,
		bool uncountedOnly,
		bool differencesOnly,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsEdit);
		ValidateId(inventoryCountId);
		return _counts.SearchLineDetailsAsync(
			inventoryCountId,
			searchText,
			uncountedOnly,
			differencesOnly,
			pageNumber,
			pageSize,
			cancellationToken);
	}

	public Task<InventoryCountLineDetails?> GetLineDetailsByIdAsync(
		long lineId,
		CancellationToken cancellationToken = default) =>
		_counts.GetLineDetailsByIdAsync(lineId, cancellationToken);

	public async Task<InventoryCount> SaveDraftAsync(
		InventoryCount count,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(count.Id == 0 ? ApplicationPermission.InventoryCountsCreate : ApplicationPermission.InventoryCountsEdit);
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
		_audit.RequirePermission(ApplicationPermission.InventoryCountsEdit);
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
		long? countedQuantity,
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
				var count = await _counts.GetHeaderByIdForUpdateAsync(transaction, inventoryCountId, token)
					?? throw new InvalidOperationException("The inventory count was not found.");
				if (count.Status != InventoryCountStatus.Counting)
				{
					throw new InvalidOperationException("Quantities can only be recorded while an inventory count is in Counting status.");
				}

				var before = await _counts.GetLineByIdAsync(transaction, inventoryCountId, lineId, token)
					?? throw new InvalidOperationException("The inventory-count line was not found.");
				if (before.Version != lineVersion)
				{
					throw new ConcurrencyConflictException("inventory-count line");
				}

				var after = Copy(before);
				after.CountedQuantity = countedQuantity;
				after.CountedByUserId = countedQuantity is null ? null : userId;
				after.CountedAtUtc = countedQuantity is null ? null : DateTime.UtcNow;
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

	public async Task<InventoryCount> MoveToReviewAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsEdit);
		ValidateId(id);
		EnsureSignedInForStatusChange();
		var result = await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await GetForStatusChangeAsync(
					transaction,
					id,
					version,
					InventoryCountStatus.Counting,
					token);
				if (await _counts.HasUncountedLinesAsync(transaction, id, token))
				{
					throw new InvalidOperationException("Every inventory-count line must be counted before review.");
				}
				return await SetStatusAsync(
					transaction,
					before,
					InventoryCountStatus.Review,
					token);
			},
			cancellationToken);
		_notifications.RaiseChanged();
		return result;
	}

	public async Task<InventoryCount> PostInventoryCountAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsPost);
		ValidateId(id);
		var userId = _audit.CurrentUserId
			?? throw new InvalidOperationException("A signed-in user is required to post an inventory count.");

		return await _transactions.ExecuteAsync(
			(transaction, token) => PostInventoryCountAsync(transaction, id, version, userId, token),
			cancellationToken);
	}

	public Task<InventoryCount> ReturnToCountingAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsEdit);
		return ChangeStatusAsync(
			id,
			version,
			InventoryCountStatus.Review,
			InventoryCountStatus.Counting,
			cancellationToken);
	}

	public async Task<InventoryCount> CancelAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		_audit.RequirePermission(ApplicationPermission.InventoryCountsEdit);
		ValidateId(id);
		EnsureSignedInForStatusChange();
		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await _counts.GetHeaderByIdForUpdateAsync(transaction, id, token)
					?? throw new InvalidOperationException("The inventory count was not found.");
				if (before.Version != version)
				{
					throw new ConcurrencyConflictException("inventory count");
				}
				if (before.Status is InventoryCountStatus.Posted or InventoryCountStatus.Cancelled)
				{
					throw new InvalidOperationException("A posted or cancelled inventory count cannot be cancelled.");
				}
				return await SetStatusAsync(
					transaction,
					before,
					InventoryCountStatus.Cancelled,
					token);
			},
			cancellationToken);
	}

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

	private async Task<InventoryCount> PostInventoryCountAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		long userId,
		CancellationToken cancellationToken)
	{
		var before = await GetForStatusChangeAsync(
			transaction,
			id,
			version,
			InventoryCountStatus.Review,
			cancellationToken);
		var lines = await _counts.ListLinesAsync(transaction, id, cancellationToken);
		if (lines.Count == 0)
		{
			throw new InvalidOperationException("An inventory count without positions cannot be posted.");
		}
		if (lines.Any(line => line.CountedQuantity is null))
		{
			throw new InvalidOperationException("Every inventory-count line must be counted before posting.");
		}
		before.Lines = lines;

		var inventoryIds = lines.Select(line => line.InventoryId).Distinct().OrderBy(inventoryId => inventoryId).ToArray();
		var inventories = await _inventories.GetByIdsForUpdateAsync(
			transaction,
			inventoryIds,
			cancellationToken);
		if (inventories.Count != inventoryIds.Length)
		{
			throw new InvalidOperationException("An inventory-count inventory was not found.");
		}

		var currentQuantities = (await _stockMovements.GetCurrentQuantitiesAsync(
			transaction,
			inventoryIds,
			cancellationToken)).ToDictionary(quantity => quantity.InventoryId, quantity => quantity.Quantity);
		var reasonCode = await _reasonCodes.GetByCodeAsync(
			transaction,
			ReasonCodeSystemCodes.InventoryCorrection,
			cancellationToken);
		if (reasonCode is null || !reasonCode.IsActive)
		{
			throw new InvalidOperationException(
				$"Required system reason code '{ReasonCodeSystemCodes.InventoryCorrection}' is unavailable.");
		}

		var completedAtUtc = DateTime.UtcNow;
		foreach (var line in lines.OrderBy(line => line.InventoryId))
		{
			var currentQuantity = currentQuantities.GetValueOrDefault(line.InventoryId);
			var countedQuantity = line.CountedQuantity.GetValueOrDefault();
			var correction = countedQuantity - currentQuantity;
			if (correction == 0) continue;

			var movement = new StockMovement
			{
				InventoryId = line.InventoryId,
				ReasonCodeId = reasonCode.Id,
				MovementType = StockMovementType.Correction,
				TimestampUtc = completedAtUtc,
				Quantity = checked((int)correction),
				Reference = before.CountNumber,
				Notes = $"Inventory count correction; snapshot {line.ExpectedQuantity}; current {currentQuantity}; counted {countedQuantity}"
			};
			movement.Id = await _stockMovements.CreateAsync(transaction, movement, cancellationToken);
		}

		if (!await _counts.PostAsync(
			transaction,
			id,
			version,
			userId,
			completedAtUtc,
			cancellationToken))
		{
			throw new ConcurrencyConflictException("inventory count");
		}

		var after = Copy(before);
		after.Status = InventoryCountStatus.Posted;
		after.PostedByUserId = userId;
		after.CompletedAtUtc = completedAtUtc;
		after.Version++;
		await _auditEntries.CreateAsync(
			transaction,
			_audit.CreateUpdatedEntry(id, before, after),
			cancellationToken);
		return after;
	}

	private async Task<InventoryCount> ChangeStatusAsync(
		long id,
		long version,
		InventoryCountStatus expectedStatus,
		InventoryCountStatus status,
		CancellationToken cancellationToken)
	{
		ValidateId(id);
		EnsureSignedInForStatusChange();

		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await GetForStatusChangeAsync(transaction, id, version, expectedStatus, token);
				return await SetStatusAsync(transaction, before, status, token);
			},
			cancellationToken);
	}

	private async Task<InventoryCount> GetDraftForUpdateAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		CancellationToken cancellationToken)
	{
		var count = await _counts.GetHeaderByIdForUpdateAsync(transaction, id, cancellationToken)
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

	private async Task<InventoryCount> GetForStatusChangeAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		InventoryCountStatus expectedStatus,
		CancellationToken cancellationToken)
	{
		var count = await _counts.GetHeaderByIdForUpdateAsync(transaction, id, cancellationToken)
			?? throw new InvalidOperationException("The inventory count was not found.");
		if (count.Version != version)
		{
			throw new ConcurrencyConflictException("inventory count");
		}
		if (count.Status != expectedStatus)
		{
			throw new InvalidOperationException(
				$"Only an inventory count in {expectedStatus} status can be changed.");
		}
		return count;
	}

	private async Task<InventoryCount> SetStatusAsync(
		DatabaseTransactionContext transaction,
		InventoryCount before,
		InventoryCountStatus status,
		CancellationToken cancellationToken)
	{
		if (!await _counts.SetStatusAsync(
			transaction,
			before.Id,
			before.Version,
			before.Status,
			status,
			cancellationToken))
		{
			throw new ConcurrencyConflictException("inventory count");
		}

		var after = Copy(before);
		after.Status = status;
		after.Version++;
		await _auditEntries.CreateAsync(
			transaction,
			_audit.CreateUpdatedEntry(before.Id, before, after),
			cancellationToken);
		if (status == InventoryCountStatus.Review)
		{
			var recipients = await _notifications.ResolvePermissionHoldersAsync(
				transaction,
				ApplicationPermission.InventoryCountsPost,
				cancellationToken);
			await _notifications.CreateAsync(
				transaction,
				new NotificationRequest(
					NotificationType.Workflow,
					NotificationSeverity.Information,
					$"Inventory count {after.CountNumber} is ready for review",
					$"Inventory count {after.CountNumber} has been completed and is ready to be reviewed and posted.",
					NotificationSourceTypes.InventoryCount,
					after.Id,
					after.CountNumber,
					_audit.CurrentUserId),
				recipients,
				cancellationToken);
		}
		return after;
	}

	private void EnsureSignedInForStatusChange()
	{
		if (_audit.CurrentUserId is null)
		{
			throw new InvalidOperationException("A signed-in user is required to change an inventory-count status.");
		}
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
		ReversedAtUtc = source.ReversedAtUtc,
		ReversedByUserId = source.ReversedByUserId,
		ReversalReason = source.ReversalReason,
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
