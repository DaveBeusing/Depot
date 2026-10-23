// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
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
		await using var context = await ProcurementTestContext.CreateServerAsync(factory, DatabaseProviderFactory.CreateInitializer(factory));
		try
		{
			var movements = MovementPostingTestSupport.CreateMovements(context);
			await movements.AddPurchaseAsync(context.InventoryId, 1, 5m, null, null, null, CancellationToken.None);
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
		}
		finally
		{
			// The shared procurement fixture does not own standalone movement audit entries.
			await context.Data.ExecuteAsync(
				"DELETE FROM AuditEntries WHERE EntityType = 'StockMovement' AND EntityId IN (SELECT Id FROM StockMovements WHERE InventoryId IN ($InventoryId, $InactiveInventoryId));",
				CancellationToken.None,
				new DatabaseParameter("$InventoryId", context.InventoryId),
				new DatabaseParameter("$InactiveInventoryId", context.InactiveInventoryId));
		}
	}
}
