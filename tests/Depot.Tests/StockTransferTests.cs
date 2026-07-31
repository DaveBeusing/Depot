// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Depot.ViewModels;

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
	public async Task TransferSearchUsesServerSideTextStatusAndPaging()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var first = NewTransfer(fixture);
		first.Notes = "Project Alpine";
		var saved = await fixture.Service.SaveDraftAsync(first);
		var cancelledDraft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		await fixture.Service.CancelAsync(cancelledDraft.Id, cancelledDraft.Version);

		var searchPage = await fixture.Service.SearchAsync("Alpine", StockTransferStatus.Draft, 1, 1);
		var cancelledPage = await fixture.Service.SearchAsync(null, StockTransferStatus.Cancelled, 1, 50);

		Assert.Equal(1, searchPage.TotalCount);
		Assert.Single(searchPage.Items);
		Assert.Equal(saved.Id, searchPage.Items[0].Id);
		Assert.Equal(1, searchPage.Items[0].LineCount);
		Assert.Single(cancelledPage.Items);
		Assert.Equal(cancelledDraft.Id, cancelledPage.Items[0].Id);
	}

	[Fact]
	public async Task TransferInventoryOptionsAreWarehouseAndItemFilteredAndShowCurrentStock()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 12);

		var sourceOptions = await fixture.Service.GetInventoryOptionsAsync(fixture.SourceWarehouseId);
		var matchingDestinationOptions = await fixture.Service.GetInventoryOptionsAsync(
			fixture.DestinationWarehouseId,
			context.ItemId);

		var source = Assert.Single(sourceOptions, option => option.InventoryId == fixture.SourceInventoryId);
		Assert.Equal(12, source.CurrentStock);
		Assert.Equal(2, matchingDestinationOptions.Count);
		Assert.All(matchingDestinationOptions, option => Assert.Equal(context.ItemId, option.ItemId));
		Assert.DoesNotContain(matchingDestinationOptions, option => option.InventoryId == fixture.OtherItemDestinationInventoryId);
	}

	[Fact]
	public async Task PostedTransferExposesItsGeneratedMovementPair()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 10);
		var draft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		var posted = await fixture.Service.PostAsync(draft.Id, draft.Version);

		var movements = await fixture.Service.GetMovementsAsync(posted.Id);

		Assert.Equal(2, movements.Count);
		Assert.Contains(movements, movement => movement.MovementType == StockMovementType.TransferOut && movement.Quantity == -3);
		Assert.Contains(movements, movement => movement.MovementType == StockMovementType.TransferIn && movement.Quantity == 3);
		Assert.All(movements, movement => Assert.Equal(Reference(posted), movement.Reference));
	}

	[Fact]
	public async Task TransfersViewModelLoadsThePagedTransferList()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		var saved = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		var audit = new AuditService(new AuditRepository(context.Data), context.Authorization);
		var warehouses = new WarehouseService(
			new WarehouseRepository(context.Data),
			new StorageLocationRepository(context.Data),
			audit);
		using var viewModel = new StockTransfersViewModel(fixture.Service, warehouses, new ConfirmingFileDialogs());

		await viewModel.LoadAsync();

		Assert.Equal(1, viewModel.TotalCount);
		Assert.Single(viewModel.Transfers);
		Assert.Equal(saved.Id, viewModel.Transfers[0].Id);
		Assert.False(viewModel.IsBusy);
		Assert.False(viewModel.HasOperationError);
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

	[Fact]
	public async Task PostingTransfersStockAndUpdatesStatusAndAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 10);
		var draft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));

		var posted = await fixture.Service.PostAsync(draft.Id, draft.Version);

		Assert.Equal(StockTransferStatus.Posted, posted.Status);
		Assert.Equal(context.Authorization.CurrentUser?.Id, posted.PostedByUserId);
		Assert.Equal(2, posted.Version);
		Assert.Equal(7, await StockAsync(context, fixture.SourceInventoryId));
		Assert.Equal(3, await StockAsync(context, fixture.DestinationInventoryId));
		Assert.Equal(1, await MovementCountAsync(context, posted, StockMovementType.TransferOut));
		Assert.Equal(1, await MovementCountAsync(context, posted, StockMovementType.TransferIn));
		Assert.Equal(2, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements sm INNER JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId WHERE sm.Reference = $Reference AND rc.Code = $Code;",
			new DatabaseParameter("$Reference", Reference(posted)),
			new DatabaseParameter("$Code", ReasonCodeSystemCodes.Transfer)));
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockTransfer' AND EntityId = $Id AND Action = 'Updated' AND AfterJson LIKE '%\"status\":2%';",
			new DatabaseParameter("$Id", posted.Id)));
	}

	[Fact]
	public async Task PostingMultipleLinesCreatesCompleteMovementPairs()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 8);
		await AddStockAsync(context, fixture.OtherItemSourceInventoryId, 6);
		var transfer = NewTransfer(fixture);
		transfer.Lines =
		[
			transfer.Lines[0],
			new StockTransferLine
			{
				SourceInventoryId = fixture.OtherItemSourceInventoryId,
				DestinationInventoryId = fixture.OtherItemDestinationInventoryId,
				Quantity = 4
			}
		];
		var draft = await fixture.Service.SaveDraftAsync(transfer);

		await fixture.Service.PostAsync(draft.Id, draft.Version);

		Assert.Equal(4, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;",
			new DatabaseParameter("$Reference", Reference(draft))));
		Assert.Equal(5, await StockAsync(context, fixture.SourceInventoryId));
		Assert.Equal(2, await StockAsync(context, fixture.OtherItemSourceInventoryId));
		Assert.Equal(3, await StockAsync(context, fixture.DestinationInventoryId));
		Assert.Equal(4, await StockAsync(context, fixture.OtherItemDestinationInventoryId));
	}

	[Fact]
	public async Task InsufficientStockLeavesDraftWithoutTransferMovements()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 2);
		var draft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));

		await Assert.ThrowsAsync<InsufficientStockException>(() =>
			fixture.Service.PostAsync(draft.Id, draft.Version));

		Assert.Equal(2, await StockAsync(context, fixture.SourceInventoryId));
		Assert.Equal(0, await StockAsync(context, fixture.DestinationInventoryId));
		Assert.Equal(0, await TransferMovementCountAsync(context, draft));
		Assert.Equal((long)StockTransferStatus.Draft, await TransferStatusAsync(context, draft.Id));
	}

	[Fact]
	public async Task PostingRevalidatesItemAndWarehouseAssignments()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 10);
		var wrongItem = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		await context.Data.ExecuteAsync(
			"UPDATE StockTransferLines SET DestinationInventoryId = $InventoryId WHERE StockTransferId = $TransferId;",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", fixture.OtherItemDestinationInventoryId),
			new DatabaseParameter("$TransferId", wrongItem.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			fixture.Service.PostAsync(wrongItem.Id, wrongItem.Version));

		var wrongWarehouse = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		await context.Data.ExecuteAsync(
			"UPDATE StockTransferLines SET SourceInventoryId = $InventoryId WHERE StockTransferId = $TransferId;",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", fixture.AlternateDestinationInventoryId),
			new DatabaseParameter("$TransferId", wrongWarehouse.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			fixture.Service.PostAsync(wrongWarehouse.Id, wrongWarehouse.Version));

		Assert.Equal(0, await TransferMovementCountAsync(context, wrongItem));
		Assert.Equal(0, await TransferMovementCountAsync(context, wrongWarehouse));
	}

	[Fact]
	public async Task ParallelTransfersCannotOverdrawSharedSourceInventory()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 5);
		var first = NewTransfer(fixture);
		first.Lines[0].Quantity = 4;
		var second = NewTransfer(fixture);
		second.Lines[0].Quantity = 4;
		first = await fixture.Service.SaveDraftAsync(first);
		second = await fixture.Service.SaveDraftAsync(second);

		var results = await Task.WhenAll(PostAsync(first), PostAsync(second));

		Assert.Single(results, result => result);
		Assert.Equal(1, await StockAsync(context, fixture.SourceInventoryId));
		Assert.Equal(4, await StockAsync(context, fixture.DestinationInventoryId));
		Assert.Equal(2, await context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE MovementType IN ($Out, $In);",
			new DatabaseParameter("$Out", (int)StockMovementType.TransferOut),
			new DatabaseParameter("$In", (int)StockMovementType.TransferIn)));

		async Task<bool> PostAsync(StockTransfer transfer)
		{
			try
			{
				await fixture.Service.PostAsync(transfer.Id, transfer.Version);
				return true;
			}
			catch (InsufficientStockException)
			{
				return false;
			}
		}
	}

	[Fact]
	public async Task FailureAfterTransferOutRollsBackAllMovementsStatusAndAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 10);
		var draft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		var auditCount = await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries;");
		await context.Data.ExecuteAsync(
			$"""
			CREATE TRIGGER FailTransferIn
			BEFORE INSERT ON StockMovements
			WHEN NEW.MovementType = {(int)StockMovementType.TransferIn}
			BEGIN
				SELECT RAISE(ABORT, 'forced TransferIn failure');
			END;
			""",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() =>
			fixture.Service.PostAsync(draft.Id, draft.Version));

		Assert.Equal(10, await StockAsync(context, fixture.SourceInventoryId));
		Assert.Equal(0, await StockAsync(context, fixture.DestinationInventoryId));
		Assert.Equal(0, await TransferMovementCountAsync(context, draft));
		Assert.Equal((long)StockTransferStatus.Draft, await TransferStatusAsync(context, draft.Id));
		Assert.Equal(auditCount, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries;"));
	}

	[Fact]
	public async Task PostingAuditFailureRollsBackMovementsAndStatus()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 10);
		var draft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));
		await context.Data.ExecuteAsync(
			"""
			CREATE TRIGGER FailStockTransferPostingAudit
			BEFORE INSERT ON AuditEntries
			WHEN NEW.EntityType = 'StockTransfer' AND NEW.Action = 'Updated'
			BEGIN
				SELECT RAISE(ABORT, 'forced posting audit failure');
			END;
			""",
			CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() =>
			fixture.Service.PostAsync(draft.Id, draft.Version));

		Assert.Equal(10, await StockAsync(context, fixture.SourceInventoryId));
		Assert.Equal(0, await StockAsync(context, fixture.DestinationInventoryId));
		Assert.Equal(0, await TransferMovementCountAsync(context, draft));
		Assert.Equal((long)StockTransferStatus.Draft, await TransferStatusAsync(context, draft.Id));
	}

	[Fact]
	public async Task PostingDetectsConcurrencyBeforeCreatingMovements()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var fixture = await CreateFixtureAsync(context);
		await AddStockAsync(context, fixture.SourceInventoryId, 10);
		var draft = await fixture.Service.SaveDraftAsync(NewTransfer(fixture));

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			fixture.Service.PostAsync(draft.Id, draft.Version + 1));

		Assert.Equal(0, await TransferMovementCountAsync(context, draft));
		Assert.Equal((long)StockTransferStatus.Draft, await TransferStatusAsync(context, draft.Id));
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
		var alternateDestinationLocationId = await context.Data.InsertAsync(
			"INSERT INTO StorageLocations (WarehouseId, Name, Description, IsActive) VALUES ($WarehouseId, 'Alternate', 'Alternate transfer destination', 1);",
			CancellationToken.None,
			new DatabaseParameter("$WarehouseId", destinationWarehouseId));
		var alternateDestinationInventoryId = await CreateInventoryAsync(
			context,
			context.ItemId,
			purposeId,
			alternateDestinationLocationId);
		var auditRepository = new AuditRepository(context.Data);
		var audit = new AuditService(auditRepository, context.Authorization);
		var service = new StockTransferService(
			new DatabaseTransactionRunner(context.Data),
			new StockTransferRepository(context.Data),
			new InventoryRepository(context.Data),
			new StockMovementRepository(context.Data),
			new ReasonCodeRepository(context.Data),
			auditRepository,
			audit);
		return new TransferFixture(
			service,
			sourceWarehouseId,
			destinationWarehouseId,
			context.InventoryId,
			destinationInventoryId,
			context.SecondInventoryId,
			otherItemDestinationInventoryId,
			alternateDestinationInventoryId);
	}

	private static Task<long> AddStockAsync(
		ProcurementTestContext context,
		long inventoryId,
		int quantity) =>
		context.Data.InsertAsync(
			"INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, Reference) VALUES ($InventoryId, $MovementType, $TimestampUtc, $Quantity, $Reference);",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", inventoryId),
			new DatabaseParameter("$MovementType", (int)StockMovementType.OpeningBalance),
			new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O")),
			new DatabaseParameter("$Quantity", quantity),
			new DatabaseParameter("$Reference", "Transfer test stock"));

	private static async Task<long> StockAsync(ProcurementTestContext context, long inventoryId) =>
		await context.ScalarAsync(
			"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;",
			new DatabaseParameter("$InventoryId", inventoryId));

	private static Task<long> MovementCountAsync(
		ProcurementTestContext context,
		StockTransfer transfer,
		StockMovementType movementType) =>
		context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference AND MovementType = $MovementType;",
			new DatabaseParameter("$Reference", Reference(transfer)),
			new DatabaseParameter("$MovementType", (int)movementType));

	private static Task<long> TransferMovementCountAsync(
		ProcurementTestContext context,
		StockTransfer transfer) =>
		context.ScalarAsync(
			"SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;",
			new DatabaseParameter("$Reference", Reference(transfer)));

	private static Task<long> TransferStatusAsync(ProcurementTestContext context, long transferId) =>
		context.ScalarAsync(
			"SELECT Status FROM StockTransfers WHERE Id = $Id;",
			new DatabaseParameter("$Id", transferId));

	private static string Reference(StockTransfer transfer) =>
		$"Stock Transfer {transfer.TransferNumber}";

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
		long OtherItemSourceInventoryId,
		long OtherItemDestinationInventoryId,
		long AlternateDestinationInventoryId);

	private sealed class ConfirmingFileDialogs : IFileDialogService
	{
		public string? ShowOpenFile(OpenFileDialogRequest request) => null;
		public string? ShowSaveFile(SaveFileDialogRequest request) => null;
		public bool Confirm(ConfirmationDialogRequest request) => true;
	}
}
