// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class StockTransferService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly StockTransferRepository _transfers;
	private readonly InventoryRepository _inventories;
	private readonly StockMovementRepository _stockMovements;
	private readonly ReasonCodeRepository _reasonCodes;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly StockMovementReversalService _reversals;

	public StockTransferService(
		IDatabaseTransactionRunner transactions,
		StockTransferRepository transfers,
		InventoryRepository inventories,
		StockMovementRepository stockMovements,
		ReasonCodeRepository reasonCodes,
		AuditRepository auditEntries,
		AuditService audit,
		StockMovementReversalService reversals)
	{
		_transactions = transactions;
		_transfers = transfers;
		_inventories = inventories;
		_stockMovements = stockMovements;
		_reasonCodes = reasonCodes;
		_auditEntries = auditEntries;
		_audit = audit;
		_reversals = reversals;
	}

	public async Task<StockTransfer> ReverseAsync(long id, long version, long reasonCodeId, string reversalReason, CancellationToken cancellationToken = default)
	{
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		var userId = _reversals.RequireUser();
		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await _transfers.GetByIdAsync(transaction, id, token)
					?? throw new InvalidOperationException("The stock transfer was not found.");
				if (before.Version != version) throw new ConcurrencyConflictException("stock transfer");
				if (before.Status != StockTransferStatus.Posted || before.IsReversed)
				{
					throw new InvalidOperationException("Only a posted, unreversed stock transfer can be reversed.");
				}
				var reference = $"Stock Transfer {before.TransferNumber}";
				var originals = await _stockMovements.ListOriginalsByReferenceAsync(transaction, reference, token);
				if (originals.Count != before.Lines.Count * 2 || originals.Any(movement => movement.MovementType is not (StockMovementType.TransferOut or StockMovementType.TransferIn)))
				{
					throw new InvalidOperationException("The stock-transfer movements are incomplete or inconsistent.");
				}
				await _reversals.CreateReversalsAsync(transaction, originals, reasonCodeId, reversalReason, userId, token);
				var reversedAtUtc = DateTime.UtcNow;
				var normalizedReason = reversalReason.Trim();
				if (!await _transfers.MarkReversedAsync(transaction, id, version, reversedAtUtc, userId, normalizedReason, token))
				{
					throw new ConcurrencyConflictException("stock transfer");
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

	public async Task<StockTransfer> PostAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		if (id <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(id));
		}
		var userId = _audit.CurrentUserId
			?? throw new InvalidOperationException("A signed-in user is required to post a stock transfer.");

		return await _transactions.ExecuteAsync(
			(transaction, token) => PostAsync(transaction, id, version, userId, token),
			cancellationToken);
	}

	public Task<StockTransfer?> GetByIdAsync(
		long id,
		CancellationToken cancellationToken = default) =>
		_transfers.GetByIdAsync(id, cancellationToken);

	public Task<PageResult<StockTransferOverviewItem>> SearchAsync(
		string? searchText,
		StockTransferStatus? status,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default) =>
		_transfers.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);

	public Task<StockTransferOverviewItem?> GetOverviewByIdAsync(
		long id,
		CancellationToken cancellationToken = default) =>
		_transfers.GetOverviewByIdAsync(id, cancellationToken);

	public Task<IReadOnlyList<StockTransferInventoryOption>> GetInventoryOptionsAsync(
		long warehouseId,
		long? itemId = null,
		CancellationToken cancellationToken = default)
	{
		if (warehouseId <= 0) throw new ArgumentOutOfRangeException(nameof(warehouseId));
		if (itemId is <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
		return _inventories.ListTransferOptionsAsync(warehouseId, itemId, cancellationToken);
	}

	public async Task<IReadOnlyList<MovementOverviewItem>> GetMovementsAsync(
		long stockTransferId,
		CancellationToken cancellationToken = default)
	{
		var transfer = await _transfers.GetByIdAsync(stockTransferId, cancellationToken)
			?? throw new InvalidOperationException("The stock transfer was not found.");
		return await _stockMovements.ListByReferenceAsync(
			$"Stock Transfer {transfer.TransferNumber}",
			cancellationToken);
	}

	public async Task<StockTransfer> SaveDraftAsync(
		StockTransfer transfer,
		CancellationToken cancellationToken = default)
	{
		NormalizeAndValidate(transfer);
		var userId = _audit.CurrentUserId
			?? throw new InvalidOperationException("A signed-in user is required to save a stock transfer.");
		return await _transactions.ExecuteAsync(
			(transaction, token) => SaveDraftAsync(transaction, transfer, userId, token),
			cancellationToken);
	}

	public async Task<StockTransfer> CancelAsync(
		long id,
		long version,
		CancellationToken cancellationToken = default)
	{
		if (id <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(id));
		}
		if (_audit.CurrentUserId is null)
		{
			throw new InvalidOperationException("A signed-in user is required to cancel a stock transfer.");
		}

		return await _transactions.ExecuteAsync(
			async (transaction, token) =>
			{
				var before = await _transfers.GetByIdAsync(transaction, id, token)
					?? throw new InvalidOperationException("The stock transfer was not found.");
				if (before.Status != StockTransferStatus.Draft)
				{
					throw new InvalidOperationException("Only an unposted draft stock transfer can be cancelled.");
				}

				var after = Copy(before);
				after.Status = StockTransferStatus.Cancelled;
				after.Version = version + 1;
				if (!await _transfers.SetStatusAsync(
					transaction,
					id,
					version,
					StockTransferStatus.Draft,
					StockTransferStatus.Cancelled,
					null,
					token))
				{
					throw new ConcurrencyConflictException("stock transfer");
				}

				await _auditEntries.CreateAsync(
					transaction,
					_audit.CreateUpdatedEntry(id, before, after),
					token);
				return after;
			},
			cancellationToken);
	}

	private async Task<StockTransfer> SaveDraftAsync(
		DatabaseTransactionContext transaction,
		StockTransfer transfer,
		long userId,
		CancellationToken cancellationToken)
	{
		StockTransfer? before = null;
		if (transfer.Id == 0)
		{
			transfer.CreatedByUserId = userId;
			transfer.PostedByUserId = null;
		}
		else
		{
			before = await _transfers.GetByIdAsync(transaction, transfer.Id, cancellationToken)
				?? throw new InvalidOperationException("The stock transfer was not found.");
			if (before.Status != StockTransferStatus.Draft)
			{
				throw new InvalidOperationException("Only draft stock transfers can be edited.");
			}

			transfer.TransferNumber = before.TransferNumber;
			transfer.CreatedByUserId = before.CreatedByUserId;
			transfer.PostedByUserId = null;
		}

		var inventoryContexts = (await _inventories.GetTransferContextsByIdsForUpdateAsync(
			transaction,
			transfer.Lines.SelectMany(line => new[] { line.SourceInventoryId, line.DestinationInventoryId }),
			cancellationToken)).ToDictionary(context => context.InventoryId);
		ValidateInventoryAssignments(transfer, inventoryContexts);

		if (transfer.Id == 0)
		{
			transfer.TransferNumber = $"PENDING-{Guid.NewGuid():N}";
			transfer.Id = await _transfers.CreateAsync(transaction, transfer, cancellationToken);
			transfer.TransferNumber = $"ST-{transfer.Id:000000}";
			if (await _transfers.UpdateTransferNumberAsync(
				transaction,
				transfer.Id,
				transfer.TransferNumber,
				cancellationToken) != 1)
			{
				throw new ConcurrencyConflictException("stock transfer number");
			}
		}
		else
		{
			if (!await _transfers.UpdateDraftAsync(transaction, transfer, cancellationToken))
			{
				throw new ConcurrencyConflictException("stock transfer");
			}
			transfer.Version++;
		}

		var existingLineIds = before?.Lines.Select(line => line.Id).ToHashSet() ?? [];
		var suppliedLineIds = transfer.Lines.Where(line => line.Id != 0).Select(line => line.Id).ToArray();
		if (suppliedLineIds.Distinct().Count() != suppliedLineIds.Length ||
			suppliedLineIds.Any(id => !existingLineIds.Contains(id)))
		{
			throw new InvalidOperationException("A stock transfer line does not belong to this transfer.");
		}

		var removedLineIds = existingLineIds.Except(suppliedLineIds).OrderBy(id => id).ToArray();
		await _transfers.DeleteLinesAsync(transaction, transfer.Id, removedLineIds, cancellationToken);
		var lineNumber = 1;
		foreach (var line in transfer.Lines)
		{
			line.StockTransferId = transfer.Id;
			line.LineNumber = lineNumber++;
			if (line.Id == 0)
			{
				line.Id = await _transfers.CreateLineAsync(transaction, line, cancellationToken);
			}
			else
			{
				if (!await _transfers.UpdateLineAsync(transaction, line, cancellationToken))
				{
					throw new ConcurrencyConflictException("stock transfer line");
				}
				line.Version++;
			}
		}

		var auditEntry = before is null
			? _audit.CreateCreatedEntry(transfer.Id, transfer)
			: _audit.CreateUpdatedEntry(transfer.Id, before, transfer);
		await _auditEntries.CreateAsync(transaction, auditEntry, cancellationToken);
		return transfer;
	}

	private async Task<StockTransfer> PostAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		long userId,
		CancellationToken cancellationToken)
	{
		var before = await _transfers.GetByIdAsync(transaction, id, cancellationToken)
			?? throw new InvalidOperationException("The stock transfer was not found.");
		if (before.Version != version)
		{
			throw new ConcurrencyConflictException("stock transfer");
		}
		if (before.Status != StockTransferStatus.Draft)
		{
			throw new InvalidOperationException("Only draft stock transfers can be posted.");
		}
		if (before.Lines.Count == 0)
		{
			throw new InvalidOperationException("A stock transfer requires at least one line.");
		}

		var inventories = (await _inventories.GetTransferContextsByIdsForUpdateAsync(
			transaction,
			before.Lines.SelectMany(line => new[] { line.SourceInventoryId, line.DestinationInventoryId }),
			cancellationToken)).ToDictionary(inventory => inventory.InventoryId);
		ValidateInventoryAssignments(before, inventories);

		var requiredBySource = before.Lines
			.GroupBy(line => line.SourceInventoryId)
			.ToDictionary(group => group.Key, group => group.Sum(line => (long)line.Quantity));
		var currentBySource = (await _stockMovements.GetCurrentQuantitiesAsync(
			transaction,
			requiredBySource.Keys,
			cancellationToken)).ToDictionary(stock => stock.InventoryId, stock => stock.Quantity);
		if (requiredBySource.Any(required =>
			currentBySource.GetValueOrDefault(required.Key) < required.Value))
		{
			throw new InsufficientStockException();
		}

		var reasonCode = await _reasonCodes.GetByCodeAsync(
			transaction,
			ReasonCodeSystemCodes.Transfer,
			cancellationToken);
		if (reasonCode is null || !reasonCode.IsActive)
		{
			throw new InvalidOperationException(
				$"Required system reason code '{ReasonCodeSystemCodes.Transfer}' is unavailable.");
		}

		var timestampUtc = DateTime.UtcNow;
		var reference = $"Stock Transfer {before.TransferNumber}";
		foreach (var line in before.Lines.OrderBy(line => line.LineNumber))
		{
			var transferOut = new StockMovement
			{
				InventoryId = line.SourceInventoryId,
				ReasonCodeId = reasonCode.Id,
				MovementType = StockMovementType.TransferOut,
				TimestampUtc = timestampUtc,
				Quantity = -line.Quantity,
				Reference = reference,
				Notes = $"TransferOut to inventory {line.DestinationInventoryId}"
			};
			transferOut.Id = await _stockMovements.CreateAsync(
				transaction,
				transferOut,
				cancellationToken);

			var transferIn = new StockMovement
			{
				InventoryId = line.DestinationInventoryId,
				ReasonCodeId = reasonCode.Id,
				MovementType = StockMovementType.TransferIn,
				TimestampUtc = timestampUtc,
				Quantity = line.Quantity,
				Reference = reference,
				Notes = $"TransferIn from inventory {line.SourceInventoryId}"
			};
			transferIn.Id = await _stockMovements.CreateAsync(
				transaction,
				transferIn,
				cancellationToken);
		}

		var after = Copy(before);
		after.Status = StockTransferStatus.Posted;
		after.PostedByUserId = userId;
		after.Version = version + 1;
		if (!await _transfers.SetStatusAsync(
			transaction,
			id,
			version,
			StockTransferStatus.Draft,
			StockTransferStatus.Posted,
			userId,
			cancellationToken))
		{
			throw new ConcurrencyConflictException("stock transfer");
		}

		await _auditEntries.CreateAsync(
			transaction,
			_audit.CreateUpdatedEntry(id, before, after),
			cancellationToken);
		return after;
	}

	private static void NormalizeAndValidate(StockTransfer transfer)
	{
		transfer.Notes = string.IsNullOrWhiteSpace(transfer.Notes) ? null : transfer.Notes.Trim();
		if (transfer.Status != StockTransferStatus.Draft)
		{
			throw new InvalidOperationException("Only draft stock transfers can be saved.");
		}
		if (transfer.SourceWarehouseId <= 0 || transfer.DestinationWarehouseId <= 0)
		{
			throw new ArgumentException("Source and destination warehouses are required.");
		}
		if (transfer.SourceWarehouseId == transfer.DestinationWarehouseId)
		{
			throw new InvalidOperationException("Source and destination warehouses must be different.");
		}
		if (transfer.Notes?.Length > 4000)
		{
			throw new ArgumentException("Notes must not exceed 4000 characters.");
		}
		if (transfer.Lines.Count == 0)
		{
			throw new InvalidOperationException("A stock transfer requires at least one line.");
		}
		if (transfer.Lines.Any(line => line.Quantity <= 0))
		{
			throw new ArgumentOutOfRangeException(nameof(transfer), "Every stock transfer quantity must be greater than zero.");
		}
		if (transfer.Lines.Any(line => line.SourceInventoryId <= 0 || line.DestinationInventoryId <= 0))
		{
			throw new InvalidOperationException("Every stock transfer line requires source and destination inventory.");
		}
		if (transfer.Lines
			.Select(line => (line.SourceInventoryId, line.DestinationInventoryId))
			.Distinct()
			.Count() != transfer.Lines.Count)
		{
			throw new InvalidOperationException("An inventory pair can only occur once per stock transfer.");
		}
	}

	private static void ValidateInventoryAssignments(
		StockTransfer transfer,
		IReadOnlyDictionary<long, InventoryTransferContext> inventories)
	{
		foreach (var line in transfer.Lines)
		{
			if (!inventories.TryGetValue(line.SourceInventoryId, out var source) ||
				!inventories.TryGetValue(line.DestinationInventoryId, out var destination))
			{
				throw new InvalidOperationException("A source or destination inventory was not found.");
			}
			if (source.ItemId != destination.ItemId)
			{
				throw new InvalidOperationException("Source and destination inventory must belong to the same item.");
			}
			if (source.WarehouseId != transfer.SourceWarehouseId)
			{
				throw new InvalidOperationException("Source inventory does not belong to the source warehouse.");
			}
			if (destination.WarehouseId != transfer.DestinationWarehouseId)
			{
				throw new InvalidOperationException("Destination inventory does not belong to the destination warehouse.");
			}
		}
	}

	private static StockTransfer Copy(StockTransfer source) => new()
	{
		Id = source.Id,
		TransferNumber = source.TransferNumber,
		SourceWarehouseId = source.SourceWarehouseId,
		DestinationWarehouseId = source.DestinationWarehouseId,
		TransferDate = source.TransferDate,
		Status = source.Status,
		CreatedByUserId = source.CreatedByUserId,
		PostedByUserId = source.PostedByUserId,
		Notes = source.Notes,
		ReversedAtUtc = source.ReversedAtUtc,
		ReversedByUserId = source.ReversedByUserId,
		ReversalReason = source.ReversalReason,
		Version = source.Version,
		Lines = source.Lines
	};
}
