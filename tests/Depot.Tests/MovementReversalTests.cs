// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class MovementReversalTests
{
	[Fact]
	public async Task MaterialWithdrawalReversalCreatesOneAuditedCounterMovementAndCannotRepeat()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var service = CreateService(context);
		await InsertMovementAsync(context, 10, StockMovementType.OpeningBalance, "OPEN");
		var withdrawalId = await InsertMovementAsync(context, -4, StockMovementType.Withdrawal, "ISSUE-1");
		var reasonCodeId = await ReasonCodeIdAsync(context, ReasonCodeSystemCodes.Returned);

		var reversal = await service.ReverseWithdrawalAsync(withdrawalId, reasonCodeId, "Material returned unused");

		Assert.Equal(StockMovementType.Reversal, reversal.MovementType);
		Assert.Equal(4, reversal.Quantity);
		Assert.Equal(withdrawalId, reversal.ReversalOfMovementId);
		Assert.Equal("ISSUE-1", reversal.Reference);
		Assert.Equal(10, await CurrentStockAsync(context));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'StockMovement' AND EntityId = $Id;", new DatabaseParameter("$Id", reversal.Id)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseWithdrawalAsync(withdrawalId, reasonCodeId, "Duplicate"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseWithdrawalAsync(reversal.Id, reasonCodeId, "Reversal chain"));
	}

	[Fact]
	public async Task ConcurrentMaterialWithdrawalReversalsCreateExactlyOneCounterMovement()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await InsertMovementAsync(context, 10, StockMovementType.OpeningBalance, "OPEN");
		var withdrawalId = await InsertMovementAsync(context, -4, StockMovementType.Withdrawal, "ISSUE-CONCURRENT");
		var reasonCodeId = await ReasonCodeIdAsync(context, ReasonCodeSystemCodes.Returned);
		var services = new[] { CreateService(context), CreateService(context) };

		var attempts = services
			.Select(service => CaptureAsync(() => service.ReverseWithdrawalAsync(withdrawalId, reasonCodeId, "Concurrent reversal")))
			.ToArray();
		var results = await Task.WhenAll(attempts);

		Assert.Single(results, static result => result is null);
		Assert.Single(results, static result => result is InvalidOperationException);
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId = $MovementId;", new DatabaseParameter("$MovementId", withdrawalId)));
		Assert.Equal(10, await CurrentStockAsync(context));
	}

	private static StockMovementReversalService CreateService(ProcurementTestContext context)
	{
		var auditRepository = new AuditRepository(context.Data);
		var audit = new AuditService(auditRepository, context.Authorization);
		return new StockMovementReversalService(new DatabaseTransactionRunner(context.Data), new InventoryRepository(context.Data), new StockMovementRepository(context.Data), new ReasonCodeRepository(context.Data), auditRepository, audit);
	}

	private static Task<long> InsertMovementAsync(ProcurementTestContext context, int quantity, StockMovementType type, string reference) =>
		context.Data.InsertAsync(
			"INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, Reference) VALUES ($InventoryId, $MovementType, $TimestampUtc, $Quantity, $Reference);",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId", context.InventoryId),
			new DatabaseParameter("$MovementType", (int)type),
			new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O")),
			new DatabaseParameter("$Quantity", quantity),
			new DatabaseParameter("$Reference", reference));

	private static async Task<long> ReasonCodeIdAsync(ProcurementTestContext context, string code) =>
		await context.ScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", new DatabaseParameter("$Code", code));

	private static async Task<long> CurrentStockAsync(ProcurementTestContext context) =>
		await context.ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;", new DatabaseParameter("$InventoryId", context.InventoryId));

	private static async Task<Exception?> CaptureAsync(Func<Task<StockMovement>> action)
	{
		try
		{
			await action();
			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}
}
