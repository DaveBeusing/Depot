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
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;

	public StockTransferService(
		IDatabaseTransactionRunner transactions,
		StockTransferRepository transfers,
		InventoryRepository inventories,
		AuditRepository auditEntries,
		AuditService audit)
	{
		_transactions = transactions;
		_transfers = transfers;
		_inventories = inventories;
		_auditEntries = auditEntries;
		_audit = audit;
	}

	public Task<StockTransfer?> GetByIdAsync(
		long id,
		CancellationToken cancellationToken = default) =>
		_transfers.GetByIdAsync(id, cancellationToken);

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
		Version = source.Version,
		Lines = source.Lines
	};
}
