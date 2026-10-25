// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class InventoryImportAtomicityTests
{
	[Fact]
	public async Task LaterTrackedRowFailureRollsBackAllImportEvidenceAndSamePreviewCanRetry()
	{
		await using var fixture = await InventoryImportFixture.CreateSqliteAsync();
		var service = fixture.CreateService();
		var preview = await fixture.CreateSerialFailurePreviewAsync();
		var before = await fixture.SnapshotAsync();

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteImportAsync(preview));
		Assert.Contains("tracking data", error.Message, StringComparison.Ordinal);
		Assert.Equal(before, await fixture.SnapshotAsync());

		await fixture.MakeSerialItemUntrackedAsync();
		var result = await service.ExecuteImportAsync(preview);
		Assert.False(result.AlreadyCompleted);
		Assert.Equal(1, result.ImportedItems);
		Assert.Equal(2, result.ImportedMovements);
		Assert.Equal(0, result.SkippedItems);
		await fixture.AssertCommittedAsync(preview);
		var committed = await fixture.SnapshotAsync();
		Assert.Equal(before.Items + 1, committed.Items);
		Assert.Equal(before.Manufacturers + 1, committed.Manufacturers);
		Assert.Equal(before.Categories + 1, committed.Categories);
		Assert.Equal(before.Purposes + 1, committed.Purposes);
		Assert.Equal(before.Warehouses + 1, committed.Warehouses);
		Assert.Equal(before.Locations + 1, committed.Locations);
		Assert.Equal(before.Inventories + 2, committed.Inventories);
		Assert.Equal(before.Movements + 2, committed.Movements);
		Assert.Equal(before.Audits + 10, committed.Audits);
		Assert.Equal(before.Operations + 1, committed.Operations);
		InventoryImportFixture.AssertReplay(await fixture.CreateService().ExecuteImportAsync(preview), preview);
		Assert.Equal(committed, await fixture.SnapshotAsync());
	}

	[Fact]
	public async Task CancellationAtCommitRollsBackAllRowsAndLeavesOperationRetryable()
	{
		await using var fixture = await InventoryImportFixture.CreateSqliteAsync();
		var service = fixture.CreateService();
		var preview = fixture.CreatePreview();
		var before = await fixture.SnapshotAsync();
		using var cancellation = new CancellationTokenSource();
		fixture.Transactions.Arm();
		var importing = service.ExecuteImportAsync(preview, cancellation.Token);
		try
		{
			var boundary = await Task.WhenAny(fixture.Transactions.BeforeCommit, importing).WaitAsync(TimeSpan.FromSeconds(30));
			if (boundary == importing) await importing;
			Assert.Same(fixture.Transactions.BeforeCommit, boundary);
			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importing);
		}
		finally
		{
			cancellation.Cancel();
			fixture.Transactions.Release();
			await Record.ExceptionAsync(() => importing);
		}
		Assert.Equal(before, await fixture.SnapshotAsync());

		var retried = await fixture.CreateService().ExecuteImportAsync(preview);
		Assert.Equal(1, retried.ImportedItems);
		Assert.Equal(1, retried.ImportedMovements);
		await fixture.AssertCommittedAsync(preview);
	}

	[Fact]
	public async Task LostCommittedResponseReplaysAfterServiceRestartAndRejectsChangedContent()
	{
		await using var fixture = await InventoryImportFixture.CreateSqliteAsync();
		var preview = fixture.CreatePreview();
		var service = fixture.CreateService(new ImportCommitResponseLossRunner(fixture.Transactions));
		await Assert.ThrowsAsync<IOException>(() => service.ExecuteImportAsync(preview));
		await fixture.AssertCommittedAsync(preview);
		var committed = await fixture.SnapshotAsync();

		var restarted = fixture.CreateService();
		InventoryImportFixture.AssertReplay(await restarted.ExecuteImportAsync(preview), preview);
		var equivalentPrice = InventoryImportFixture.WithEquivalentPriceScale(preview);
		Assert.Equal(preview.Items[0].UnitPrice, equivalentPrice.Items[0].UnitPrice);
		InventoryImportFixture.AssertReplay(await restarted.ExecuteImportAsync(equivalentPrice), equivalentPrice);
		await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ExecuteImportAsync(InventoryImportFixture.WithChangedQuantity(preview)));
		Assert.Equal(committed, await fixture.SnapshotAsync());
	}

	[Fact]
	public async Task RegeneratedWorkbookPreviewKeepsIdentityAndSynchronousExecutionRecognizesReplay()
	{
		await using var fixture = await InventoryImportFixture.CreateSqliteAsync();
		var path = fixture.WriteWorkbook(fixture.CreatePreview());
		try
		{
			var service = fixture.CreateService();
			var preview = service.CreatePreview(path);
			Assert.Empty(preview.Warnings);
			Assert.False(Assert.Single(preview.Items).ItemAlreadyExists);
			await service.ExecuteImportAsync(preview);
			var committed = await fixture.SnapshotAsync();
			var restarted = fixture.CreateService();
			var regenerated = restarted.CreatePreview(path);
			Assert.True(Assert.Single(regenerated.Items).ItemAlreadyExists);
			Assert.Equal(preview.OperationId, regenerated.OperationId);
			InventoryImportFixture.AssertReplay(restarted.ExecuteImport(regenerated), regenerated);
			InventoryImportFixture.AssertReplay(await restarted.ExecuteImportAsync(regenerated), regenerated);
			await fixture.AssertCommittedAsync(regenerated);
			Assert.Equal(committed, await fixture.SnapshotAsync());
		}
		finally
		{
			if (File.Exists(path)) File.Delete(path);
		}
	}
}
