// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class MovementPostingProviderTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerProtectsStockThroughProductionMovementComposition() => VerifyProviderContractAsync(
		new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbProtectsStockThroughProductionMovementComposition() => VerifyProviderContractAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlProtectsStockThroughProductionMovementComposition() => VerifyProviderContractAsync(
		new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyProviderContractAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		await using var context = await ProcurementTestContext.CreateServerAsync(factory, DatabaseProviderFactory.CreateInitializer(factory));
		try
		{
			var movements = MovementPostingTestSupport.CreateMovements(context);
			var purchase = await movements.AddPurchaseAsync(context.InventoryId, 1, 5m, null, null, null, CancellationToken.None);
			Assert.False(purchase.IsReversed);
			var auditCount = await MovementPostingTestSupport.StockMovementAuditCountAsync(context);

			await Assert.ThrowsAsync<InsufficientStockException>(() => movements.AddWithdrawalAsync(context.InventoryId, 2, null, null, null, CancellationToken.None));
			await Assert.ThrowsAsync<InsufficientStockException>(() => movements.AddCorrectionAsync(context.InventoryId, -2, null, null, null, CancellationToken.None));
			await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, 1, 1);
			Assert.Equal(auditCount, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));
			await MovementPostingTestSupport.AssertTrackingStateAsync(context, 0, 0, 0);

			var inactive = await Assert.ThrowsAsync<InvalidOperationException>(() => movements.AddPurchaseAsync(context.InactiveInventoryId, 1, 5m, null, null, null, CancellationToken.None));
			Assert.Contains("inactive", inactive.Message);
			await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InactiveInventoryId, 0, 0);
			Assert.Equal(auditCount, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));

			await MovementPostingTestSupport.WithdrawLastUnitConcurrentlyAsync(context);
			Assert.Equal(auditCount + 1, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));

			var withdrawalId = await context.ScalarAsync(
				"SELECT MIN(Id) FROM StockMovements WHERE InventoryId = $InventoryId AND MovementType = $MovementType;",
				new DatabaseParameter("$InventoryId", context.InventoryId),
				new DatabaseParameter("$MovementType", (int)StockMovementType.Withdrawal));
			var repository = new StockMovementRepository(context.Data);
			var withdrawal = await repository.GetOverviewByIdAsync(withdrawalId, CancellationToken.None);
			Assert.NotNull(withdrawal);
			Assert.False(withdrawal.IsReversed);
			var reasonCodeId = await context.ScalarAsync("SELECT MIN(Id) FROM ReasonCodes WHERE IsActive = 1;");
			var reversal = await movements.ReverseWithdrawalAsync(withdrawalId, reasonCodeId, "Provider reversal flag verification", CancellationToken.None);
			Assert.Equal(withdrawalId, reversal.ReversalOfMovementId);
			Assert.False(reversal.IsReversed);
			var reversedOriginal = await repository.GetOverviewByIdAsync(withdrawalId, CancellationToken.None);
			Assert.NotNull(reversedOriginal);
			Assert.True(reversedOriginal.IsReversed);
			var overview = await movements.SearchAsync(purchase.PartNumber, 1, 10, CancellationToken.None);
			Assert.Equal(3, overview.TotalCount);
			Assert.True(Assert.Single(overview.Items, item => item.MovementId == withdrawalId).IsReversed);
			Assert.False(Assert.Single(overview.Items, item => item.MovementId == purchase.MovementId).IsReversed);
			await MovementPostingTestSupport.AssertInventoryStateAsync(context, context.InventoryId, 1, 3);
			Assert.Equal(auditCount + 2, await MovementPostingTestSupport.StockMovementAuditCountAsync(context));
		}
		finally
		{
			// MySQL checks self-references row by row during the fixture's bulk delete.
			// Remove only this test's reversal links before the fixture removes its rows.
			await context.Data.ExecuteAsync(
				"UPDATE StockMovements SET ReversalOfMovementId = NULL WHERE InventoryId IN ($InventoryId, $InactiveInventoryId);",
				CancellationToken.None,
				new DatabaseParameter("$InventoryId", context.InventoryId),
				new DatabaseParameter("$InactiveInventoryId", context.InactiveInventoryId));
			// The shared procurement fixture does not own standalone movement audit entries.
			await context.Data.ExecuteAsync(
				"DELETE FROM AuditEntries WHERE EntityType = 'StockMovement' AND EntityId IN (SELECT Id FROM StockMovements WHERE InventoryId IN ($InventoryId, $InactiveInventoryId));",
				CancellationToken.None,
				new DatabaseParameter("$InventoryId", context.InventoryId),
				new DatabaseParameter("$InactiveInventoryId", context.InactiveInventoryId));
		}
	}
}
