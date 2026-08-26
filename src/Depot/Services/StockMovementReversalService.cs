// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class StockMovementReversalService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _movements;
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly ItemTraceabilityService? _traceability;

	public StockMovementReversalService(IDatabaseTransactionRunner transactions, InventoryRepository inventories, StockMovementRepository movements, ReasonCodeRepository reasonCodes, AuditRepository auditEntries, AuditService audit, ItemTraceabilityService? traceability = null)
	{
		_transactions = transactions;
		_inventories = inventories;
		_movements = movements;
		_reasonCodes = reasonCodes;
		_auditEntries = auditEntries;
		_audit = audit;
		_traceability = traceability;
	}

	public async Task<StockMovement> ReverseWithdrawalAsync(long movementId, long reasonCodeId, string reversalReason, CancellationToken cancellationToken = default)
	{
		if (movementId <= 0) throw new ArgumentOutOfRangeException(nameof(movementId));
		var userId = RequireUser();
		var reversals = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var original = await _movements.GetByIdAsync(transaction, movementId, token) ?? throw new InvalidOperationException("The stock movement was not found.");
			if (original.MovementType != StockMovementType.Withdrawal) throw new InvalidOperationException("Only a material withdrawal can be reversed independently.");
			return await CreateReversalsAsync(transaction, [original], reasonCodeId, reversalReason, userId, token);
		}, cancellationToken);
		return reversals[0];
	}

	public async Task<IReadOnlyList<StockMovement>> CreateReversalsAsync(DatabaseTransactionContext transaction, IReadOnlyList<StockMovement> originals, long reasonCodeId, string reversalReason, long userId, CancellationToken cancellationToken)
	{
		var normalizedReason = NormalizeReason(reasonCodeId, reversalReason);
		if (originals.Select(movement => movement.Id).Distinct().Count() != originals.Count) throw new InvalidOperationException("A stock movement can occur only once in a reversal.");
		if (originals.Any(movement => movement.ReversalOfMovementId is not null || movement.MovementType == StockMovementType.Reversal)) throw new InvalidOperationException("A reversal movement cannot be reversed again.");
		var reasonCode = await _reasonCodes.GetByIdAsync(transaction, reasonCodeId, cancellationToken) ?? throw new InvalidOperationException("The reversal reason code was not found.");
		if (!reasonCode.IsActive) throw new InvalidOperationException("The reversal reason code is inactive.");
		var inventoryIds = originals.Select(movement => movement.InventoryId).Distinct().OrderBy(id => id).ToArray();
		var inventories = await _inventories.GetByIdsForUpdateAsync(transaction, inventoryIds, cancellationToken);
		if (inventories.Count != inventoryIds.Length) throw new InvalidOperationException("A stock-movement inventory was not found.");
		var alreadyReversed = await _movements.ListReversedOriginalIdsAsync(transaction, originals.Select(movement => movement.Id), cancellationToken);
		if (alreadyReversed.Count > 0) throw new InvalidOperationException("A stock movement has already been reversed.");
		var currentQuantities = (await _movements.GetCurrentQuantitiesAsync(transaction, inventoryIds, cancellationToken)).ToDictionary(quantity => quantity.InventoryId, quantity => quantity.Quantity);
		var reversalByInventory = originals.GroupBy(movement => movement.InventoryId).ToDictionary(group => group.Key, group => group.Sum(movement => -(long)movement.Quantity));
		if (reversalByInventory.Any(reversal => currentQuantities.GetValueOrDefault(reversal.Key) + reversal.Value < 0)) throw new InvalidOperationException("The reversal would create a negative stock quantity.");
		var reversedAtUtc = DateTime.UtcNow;
		var result = new List<StockMovement>(originals.Count);
		foreach (var original in originals.OrderBy(movement => movement.InventoryId).ThenBy(movement => movement.Id))
		{
			var reversal = new StockMovement { InventoryId = original.InventoryId, ReasonCodeId = reasonCode.Id, MovementType = StockMovementType.Reversal, TimestampUtc = reversedAtUtc, Quantity = checked(-original.Quantity), UnitPrice = original.UnitPrice, Reference = original.Reference, Notes = $"Reversal of movement {original.Id}", ReversalOfMovementId = original.Id, ReversalReason = normalizedReason, ReversedAtUtc = reversedAtUtc, ReversedByUserId = userId };
			reversal.Id = await _movements.CreateAsync(transaction, reversal, cancellationToken);
			if (_traceability is not null) await _traceability.AttachReversalAsync(transaction, original, reversal, cancellationToken);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(reversal.Id, reversal), cancellationToken);
			result.Add(reversal);
		}
		return result;
	}

	public long RequireUser() => _audit.CurrentUserId ?? throw new InvalidOperationException("A signed-in user is required to reverse stock movements.");

	private static string NormalizeReason(long reasonCodeId, string reversalReason)
	{
		if (reasonCodeId <= 0) throw new ArgumentOutOfRangeException(nameof(reasonCodeId));
		var value = reversalReason?.Trim();
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A reversal reason is required.", nameof(reversalReason));
		if (value.Length > 1000) throw new ArgumentException("The reversal reason must not exceed 1000 characters.", nameof(reversalReason));
		return value;
	}
}
