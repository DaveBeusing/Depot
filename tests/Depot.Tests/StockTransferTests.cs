// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class StockTransferTests
{
	[Fact]
	public async Task DraftTransferIsSavedWithUniqueNumberLinesAndAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var transfer = NewTransfer(fixture);

		var saved = await fixture.Service.SaveDraftAsync(transfer);
		var second = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));

		Assert.Equal(StockTransferStatus.Draft, saved.Status);
		Assert.Matches("^ST-[0-9]{6}$", saved.TransferNumber);
		Assert.NotEqual(saved.TransferNumber, second.TransferNumber);
		Assert.Equal(1, saved.Version);
		Assert.Single(saved.Lines);
		Assert.NotEqual(0, saved.Lines[0].Id);
		Assert.Equal(1, saved.Lines[0].LineNumber);
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockTransfer' AND EntityId = $Id AND Action = 'Created';",
			new DatabaseParameter("$Id", saved.Id)));
	}

	[Fact]
	public async Task TransferRequiresDifferentWarehousesAndAtLeastOnePositiveLine()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var sameWarehouse = NewTransfer(fixture);
		sameWarehouse.DestinationWarehouseId = sameWarehouse.SourceWarehouseId;
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(sameWarehouse));

		var withoutLines = NewTransfer(fixture);
		withoutLines.Lines = [];
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(withoutLines));

		var zeroQuantity = NewTransfer(fixture);
		zeroQuantity.Lines[0].Quantity = 0;
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.SaveDraftAsync(zeroQuantity));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockTransfers;"));
	}

	[Fact]
	public async Task TransferRejectsDuplicateInventoryPair()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var transfer = NewTransfer(fixture);
		transfer.Lines = [transfer.Lines[0], new StockTransferLine
		{
			SourceInventoryId = fixture.SourceInventoryId,
			DestinationInventoryId = fixture.DestinationInventoryId,
			Quantity = 2
		}];

		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(transfer));
	}

	[Fact]
	public async Task TransferInventoriesMustMatchItemAndConfiguredWarehouses()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var wrongItem = NewTransfer(fixture);
		wrongItem.Lines[0].DestinationInventoryId = fixture.OtherItemDestinationInventoryId;
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(wrongItem));

		var wrongSourceWarehouse = NewTransfer(fixture);
		wrongSourceWarehouse.Lines[0].SourceInventoryId = fixture.DestinationInventoryId;
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(wrongSourceWarehouse));

		var wrongDestinationWarehouse = NewTransfer(fixture);
		wrongDestinationWarehouse.Lines[0].DestinationInventoryId = fixture.SourceInventoryId;
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(wrongDestinationWarehouse));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockTransfers;"));
	}

	[Fact]
	public async Task DraftCanBeEditedAndUsesOptimisticConcurrency()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var saved = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		var current = await fixture.Service.GetByIdAsync(saved.Id) ?? throw new InvalidOperationException();
		var stale = await fixture.Service.GetByIdAsync(saved.Id) ?? throw new InvalidOperationException();
		current.Notes = "Current edit";

		var updated = await fixture.Service.SaveDraftAsync(current);

		Assert.Equal(2, updated.Version);
		stale.Notes = "Stale edit";
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => fixture.Service.SaveDraftAsync(stale));
		var persisted = await fixture.Service.GetByIdAsync(saved.Id) ?? throw new InvalidOperationException();
		Assert.Equal("Current edit", persisted.Notes);
	}

	[Fact]
	public async Task StaleTransferLineRollsBackHeaderEdit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var saved = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		await context.Data.ExecuteAsync(
			"UPDATE StockTransferLines SET Version = Version + 1 WHERE StockTransferId = $Id;",
			CancellationToken.None,
			new DatabaseParameter("$Id", saved.Id));
		saved.Notes = "Must roll back";

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => fixture.Service.SaveDraftAsync(saved));

		var persisted = await fixture.Service.GetByIdAsync(saved.Id) ?? throw new InvalidOperationException();
		Assert.Equal("Transfer test", persisted.Notes);
		Assert.Equal(1, persisted.Version);
	}

	[Fact]
	public async Task OnlyUnpostedDraftCanBeCancelledAndCancelledTransferCannotBeEdited()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var saved = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));

		var cancelled = await fixture.Service.CancelAsync(saved.Id, saved.Version);

		Assert.Equal(StockTransferStatus.Cancelled, cancelled.Status);
		Assert.Equal(2, cancelled.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CancelAsync(cancelled.Id, cancelled.Version));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(cancelled));
		var posted = NewTransfer(fixture);
		posted.Status = StockTransferStatus.Posted;
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(posted));

		var storedPosted = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		await context.Data.ExecuteAsync(
			"UPDATE StockTransfers SET Status = $Status WHERE Id = $Id;",
			CancellationToken.None,
			new DatabaseParameter("$Status", (int)StockTransferStatus.Posted),
			new DatabaseParameter("$Id", storedPosted.Id));
		storedPosted = await fixture.Service.GetByIdAsync(storedPosted.Id) ?? throw new InvalidOperationException();
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(storedPosted));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CancelAsync(storedPosted.Id, storedPosted.Version));
	}

	[Fact]
	public async Task AuditFailureRollsBackEntireTransfer()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await context.Data.ExecuteAsync(
			"""
			CREATE TRIGGER FailStockTransferAudit
			BEFORE INSERT ON AuditEntries
			WHEN NEW.EntityType = 'StockTransfer'
			BEGIN
				SELECT RAISE(ABORT, 'forced audit failure');
			END;
			""",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => fixture.Service.SaveDraftAsync(NewTransfer(fixture)));

		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockTransfers;"));
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockTransferLines;"));
	}

	private static StockTransfer NewTransfer(TransferFixture fixture) => new()
	{
		SourceWarehouseId = fixture.SourceWarehouseId,
		DestinationWarehouseId = fixture.DestinationWarehouseId,
		TransferDate = DateTime.Today,
		Notes = "Transfer test",
		Lines =
		[
			new StockTransferLine
			{
				SourceInventoryId = fixture.SourceInventoryId,
				DestinationInventoryId = fixture.DestinationInventoryId,
				Quantity = 3
			}
		]
	};

	private static async Task<TransferFixture> CreateFixtureAsync(ProcurementTestContext context)
	{
		var sourceWarehouseId = await context.ScalarAsync(
			"SELECT sl.WarehouseId FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId WHERE inv.Id = $Id;",
			new DatabaseParameter("$Id", context.InventoryId));
		var purposeId = await context.ScalarAsync(
			"SELECT PurposeId FROM Inventories WHERE Id = $Id;",
			new DatabaseParameter("$Id", context.InventoryId));
		var destinationWarehouseId = await context.Data.InsertAsync(
			"INSERT INTO Warehouses (Name, Description, IsActive) VALUES ($Name, $Description, 1);",
			CancellationToken.None,
			new DatabaseParameter("$Name", $"TRANSFER-{Guid.NewGuid():N}"),
			new DatabaseParameter("$Description", "Transfer destination"));
		var destinationLocationId = await context.Data.InsertAsync(
			"INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive) VALUES ($WarehouseId, 'Default', 'Transfer destination', 1);",
			CancellationToken.None,
			new DatabaseParameter("$WarehouseId", destinationWarehouseId));
		var destinationInventoryId = await CreateInventoryAsync(context, context.ItemId, purposeId, destinationLocationId);
		var otherItemDestinationInventoryId = await CreateInventoryAsync(context, context.SecondItemId, purposeId, destinationLocationId);
		var auditRepository = new AuditRepository(context.Data);
		var audit = new AuditService(auditRepository, context.Authorization);
		var service = new StockTransferService(
			new DatabaseTransactionRunner(context.Data),
			new StockTransferRepository(context.Data),
			new InventoryRepository(context.Data),
			auditRepository,
			audit);
		return new TransferFixture(
			service,
			sourceWarehouseId,
			destinationWarehouseId,
			context.InventoryId,
			destinationInventoryId,
			otherItemDestinationInventoryId);
	}

	private static Task<long> CreateInventoryAsync(
		ProcurementTestContext context,
		long itemId,
		long purposeId,
		long storageLocationId) =>
		context.Data.InsertAsync(
			"INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive) VALUES ($ItemId, $PurposeId, $StorageLocationId, 1);",
			CancellationToken.None,
			new DatabaseParameter("$ItemId", itemId),
			new DatabaseParameter("$PurposeId", purposeId),
			new DatabaseParameter("$StorageLocationId", storageLocationId));

	private sealed record TransferFixture(
		StockTransferService Service,
		long SourceWarehouseId,
		long DestinationWarehouseId,
		long SourceInventoryId,
		long DestinationInventoryId,
		long OtherItemDestinationInventoryId);
}
