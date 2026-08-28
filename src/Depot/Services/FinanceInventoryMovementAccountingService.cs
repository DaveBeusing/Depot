// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceInventoryMovementAccountingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceInventoryCostingRepository _repository;
	private readonly InventoryRepository _inventories;
	private readonly FinanceInventoryCostingService _costing;
	private readonly IAuthorizationService _authorization;

	public FinanceInventoryMovementAccountingService(IDatabaseTransactionRunner transactions, FinanceInventoryCostingRepository repository, InventoryRepository inventories, FinanceInventoryCostingService costing, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_repository = repository;
		_inventories = inventories;
		_costing = costing;
		_authorization = authorization;
	}

	public async Task<int> ProcessInventoryCountAsync(string countNumber, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		var user = _authorization.CurrentUser is { IsActive: true } active ? active : throw new UnauthorizedAccessException("An active signed-in user is required for Inventory Accounting.");
		var reference = string.IsNullOrWhiteSpace(countNumber) ? throw new ArgumentException("Inventory count number is required.", nameof(countNumber)) : countNumber.Trim();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var movements = await _repository.GetInventoryCountMovementsAsync(transaction, reference, token);
			var originals = movements.Where(value => value.MovementType == StockMovementType.Correction && value.ReversalOfMovementId is null).OrderBy(value => value.Id).ToArray();
			if (originals.Length == 0) throw new InvalidOperationException("No inventory-count correction movements were found for the supplied count number.");
			var inventories = (await _inventories.GetByIdsForUpdateAsync(transaction, originals.Select(value => value.InventoryId), token)).ToDictionary(value => value.Id);
			if (inventories.Count != originals.Select(value => value.InventoryId).Distinct().Count()) throw new InvalidOperationException("An inventory referenced by the inventory count was not found.");
			var processed = 0;
			foreach (var original in originals)
			{
				var inventory = inventories[original.InventoryId];
				if (await _costing.RecordInventoryAdjustmentAsync(transaction, original, inventory.ItemId, DateOnly.FromDateTime(original.TimestampUtc), reference, user.Id, token) is not null) processed++;
				var reversals = movements.Where(value => value.ReversalOfMovementId == original.Id).OrderBy(value => value.Id).ToArray();
				foreach (var reversal in reversals)
				{
					if (await _costing.ReverseInventoryAdjustmentAsync(transaction, original, reversal, DateOnly.FromDateTime(reversal.TimestampUtc), reversal.ReversalReason ?? "Inventory-count reversal", user.Id, token) is not null) processed++;
				}
			}
			return processed;
		}, cancellationToken);
	}
}
