// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class InventoryCountTests
{
	[Fact]
	public async Task DraftCanBeCreatedEditedAndCancelledWithAuditAndConcurrency()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		var warehouseId = await WarehouseIdAsync(context);
		var draft = await service.SaveDraftAsync(new InventoryCount
		{
			WarehouseId = warehouseId,
			Notes = "Annual count"
		});

		Assert.Matches("^IC-[0-9]{6}$", draft.CountNumber);
		Assert.Equal(InventoryCountStatus.Draft, draft.Status);
		Assert.Equal(1, draft.Version);
		Assert.Empty(draft.Lines);
		Assert.Equal(1, await AuditCountAsync(context, draft.Id, "Created"));

		var stale = Copy(draft);
		draft.Notes = "Updated annual count";
		var updated = await service.SaveDraftAsync(draft);
		Assert.Equal(2, updated.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.SaveDraftAsync(stale));

		var cancelled = await service.CancelAsync(updated.Id, updated.Version);
		Assert.Equal(InventoryCountStatus.Cancelled, cancelled.Status);
		Assert.Equal(3, cancelled.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(cancelled));
		Assert.Equal(2, await AuditCountAsync(context, draft.Id, "Updated"));
	}

	[Fact]
	public async Task DraftRequiresAnActiveWarehouse()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		var inactiveWarehouseId = await context.Data.InsertAsync(
			"INSERT INTO Warehouses (Name, IsActive) VALUES ($Name, 0);",
			CancellationToken.None,
			new DatabaseParameter("$Name", $"INACTIVE-{Guid.NewGuid():N}"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(new InventoryCount
		{
			WarehouseId = inactiveWarehouseId
		}));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(new InventoryCount
		{
			WarehouseId = long.MaxValue
		}));
	}

	[Fact]
	public async Task StartCreatesAtomicWarehouseSnapshotAndDoesNotCreateMovements()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		await AddStockAsync(context, context.InventoryId, 7);
		var movementCount = await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements;");
		var draft = await NewDraftAsync(context, service);

		var started = await service.StartAsync(draft.Id, draft.Version);

		Assert.Equal(InventoryCountStatus.Counting, started.Status);
		Assert.NotNull(started.StartedAtUtc);
		Assert.Equal(2, started.Version);
		Assert.Equal(2, started.Lines.Count);
		Assert.Equal(7, Assert.Single(started.Lines, line => line.InventoryId == context.InventoryId).ExpectedQuantity);
		Assert.Equal(0, Assert.Single(started.Lines, line => line.InventoryId == context.SecondInventoryId).ExpectedQuantity);
		Assert.DoesNotContain(started.Lines, line => line.InventoryId == context.InactiveInventoryId);
		Assert.All(started.Lines, line => Assert.Null(line.CountedQuantity));
		Assert.Equal(movementCount, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements;"));
		Assert.Equal(1, await AuditCountAsync(context, draft.Id, "Updated"));
	}

	[Fact]
	public async Task ExpectedQuantityRemainsAnImmutableStartSnapshot()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		await AddStockAsync(context, context.InventoryId, 5);
		var draft = await NewDraftAsync(context, service);
		var started = await service.StartAsync(draft.Id, draft.Version);
		await AddStockAsync(context, context.InventoryId, 4);

		var reloaded = await service.GetByIdAsync(started.Id) ?? throw new InvalidOperationException();

		Assert.Equal(5, Assert.Single(reloaded.Lines, line => line.InventoryId == context.InventoryId).ExpectedQuantity);
		Assert.Equal(9, await CurrentStockAsync(context, context.InventoryId));
	}

	[Fact]
	public async Task AuditFailureRollsBackStartStatusAndEverySnapshotLine()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		var draft = await NewDraftAsync(context, service);
		await context.Data.ExecuteAsync(
			"""
			CREATE TRIGGER FailInventoryCountStartAudit
			BEFORE INSERT ON AuditEntries
			WHEN NEW.EntityType = 'InventoryCount' AND NEW.Action = 'Updated'
			BEGIN
				SELECT RAISE(ABORT, 'forced inventory-count audit failure');
			END;
			""",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => service.StartAsync(draft.Id, draft.Version));

		var stored = await service.GetByIdAsync(draft.Id) ?? throw new InvalidOperationException();
		Assert.Equal(InventoryCountStatus.Draft, stored.Status);
		Assert.Null(stored.StartedAtUtc);
		Assert.Equal(1, stored.Version);
		Assert.Empty(stored.Lines);
	}

	[Fact]
	public async Task CountingUsesLineConcurrencyAndReviewRequiresEveryLine()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		var draft = await NewDraftAsync(context, service);
		var started = await service.StartAsync(draft.Id, draft.Version);
		var first = started.Lines[0];

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveToReviewAsync(started.Id, started.Version));
		var countedFirst = await service.RecordCountAsync(started.Id, first.Id, first.Version, 3);
		Assert.Equal(3, countedFirst.CountedQuantity);
		Assert.NotNull(countedFirst.CountedByUserId);
		Assert.NotNull(countedFirst.CountedAtUtc);
		Assert.Equal(2, countedFirst.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			service.RecordCountAsync(started.Id, first.Id, first.Version, 4));

		foreach (var line in started.Lines.Skip(1))
		{
			await service.RecordCountAsync(started.Id, line.Id, line.Version, line.ExpectedQuantity);
		}
		var review = await service.MoveToReviewAsync(started.Id, started.Version);
		Assert.Equal(InventoryCountStatus.Review, review.Status);
		Assert.Equal(3, review.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.RecordCountAsync(review.Id, countedFirst.Id, countedFirst.Version, 4));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(review.Id, review.Version));
	}

	[Fact]
	public async Task ParallelStartCreatesOnlyOneSnapshot()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		var draft = await NewDraftAsync(context, service);

		var results = await Task.WhenAll(TryStartAsync(), TryStartAsync());

		Assert.Single(results, result => result);
		var stored = await service.GetByIdAsync(draft.Id) ?? throw new InvalidOperationException();
		Assert.Equal(InventoryCountStatus.Counting, stored.Status);
		Assert.Equal(2, stored.Lines.Count);
		Assert.Equal(2, stored.Lines.Select(line => line.InventoryId).Distinct().Count());

		async Task<bool> TryStartAsync()
		{
			try
			{
				await service.StartAsync(draft.Id, draft.Version);
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}
	}

	private static InventoryCountService CreateService(ProcurementTestContext context)
	{
		var auditRepository = new AuditRepository(context.Data);
		var audit = new AuditService(auditRepository, context.Authorization);
		return new InventoryCountService(
			new DatabaseTransactionRunner(context.Data),
			new InventoryCountRepository(context.Data),
			new InventoryRepository(context.Data),
			new StockMovementRepository(context.Data),
			new WarehouseRepository(context.Data),
			auditRepository,
			audit);
	}

	private static async Task<InventoryCount> NewDraftAsync(
		ProcurementTestContext context,
		InventoryCountService service) =>
		await service.SaveDraftAsync(new InventoryCount
		{
			WarehouseId = await WarehouseIdAsync(context),
			Notes = "Inventory count integration test"
		});

	private static async Task<long> WarehouseIdAsync(ProcurementTestContext context) =>
		await context.ScalarAsync(
			"SELECT WarehouseId FROM StorageLocations WHERE Id = $Id;",
			new DatabaseParameter("$Id", context.TestStorageLocationId));

	private static Task<long> AddStockAsync(ProcurementTestContext context, long inventoryId, int quantity) =>
		context.Data.InsertAsync(
			"INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, Reference) VALUES ($InventoryId, $MovementType, $TimestampUtc, $Quantity, $Reference);",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", inventoryId),
			new DatabaseParameter("$MovementType", (int)StockMovementType.OpeningBalance),
			new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O")),
			new DatabaseParameter("$Quantity", quantity),
			new DatabaseParameter("$Reference", "Inventory count test stock"));

	private static async Task<long> CurrentStockAsync(ProcurementTestContext context, long inventoryId) =>
		await context.ScalarAsync(
			"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", inventoryId));

	private static async Task<long> AuditCountAsync(
		ProcurementTestContext context,
		long id,
		string action) =>
		await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'InventoryCount' AND EntityId = $Id AND Action = $Action;",
			new DatabaseParameter("$Id", id),
			new DatabaseParameter("$Action", action));

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
		Lines = source.Lines
	};
}
