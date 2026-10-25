// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class InventoryImportProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SQLiteImportAtomicityAndReplayContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-import-provider-{Guid.NewGuid():N}.db");
		try { await VerifyAsync(new SqliteConnectionFactory(path)); }
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerImportAtomicityAndReplayContract() =>
		VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbImportAtomicityAndReplayContract() =>
		VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlImportAtomicityAndReplayContract() =>
		VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		await using var fixture = await InventoryImportFixture.CreateAsync(factory);
		var firstService = fixture.CreateService();
		var preview = await fixture.CreateSerialFailurePreviewAsync();
		var before = await fixture.SnapshotAsync();
		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => firstService.ExecuteImportAsync(preview));
		Assert.Contains("tracking data", error.Message, StringComparison.Ordinal);
		Assert.Equal(before, await fixture.SnapshotAsync());
		await fixture.MakeSerialItemUntrackedAsync();

		var secondService = fixture.CreateService();
		ImportResult? firstResult = null;
		ImportResult? secondResult = null;
		var secondFailure = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			async () => { firstResult = await firstService.ExecuteImportAsync(preview); },
			async () => { secondResult = await secondService.ExecuteImportAsync(preview); });
		Assert.Null(secondFailure);
		var result = Assert.IsType<ImportResult>(firstResult);
		Assert.False(result.AlreadyCompleted);
		Assert.Equal(1, result.ImportedItems);
		Assert.Equal(2, result.ImportedMovements);
		InventoryImportFixture.AssertReplay(Assert.IsType<ImportResult>(secondResult), preview);
		await fixture.AssertCommittedAsync(preview);
		var committed = await fixture.SnapshotAsync();
		Assert.Equal(before.Items + 1, committed.Items);
		Assert.Equal(before.Inventories + 2, committed.Inventories);
		Assert.Equal(before.Movements + 2, committed.Movements);
		Assert.Equal(before.Audits + 10, committed.Audits);
		Assert.Equal(before.Operations + 1, committed.Operations);

		var restarted = fixture.CreateService();
		InventoryImportFixture.AssertReplay(await restarted.ExecuteImportAsync(preview), preview);
		await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ExecuteImportAsync(InventoryImportFixture.WithChangedQuantity(preview)));
		Assert.Equal(committed, await fixture.SnapshotAsync());
	}
}
