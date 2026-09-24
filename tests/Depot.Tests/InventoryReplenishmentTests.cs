// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class InventoryReplenishmentTests
{
	[Fact]
	public void CalculationExplainsDemandSupplyAndRoundsToSupplierMoq()
	{
		var snapshot = ReplenishmentService.Calculate(new ReplenishmentRequirementInput
		{
			Policy = new ReplenishmentPolicy { Id = 7, ItemId = 11, WarehouseId = 13, ReorderPoint = 15, SafetyStock = 10, TargetStock = 30, Version = 2 },
			OnHandQuantity = 20,
			ReservedQuantity = 5,
			BackorderedQuantity = 3,
			EligibleInboundQuantity = 2,
			ActivePolicyCountForItem = 1,
			SupplierEvidenceCount = 1,
			SupplierItemId = 17,
			PlanningSupplierId = 19,
			SupplierLeadTimeDays = 8,
			SupplierMinimumOrderQuantity = 7m
		});

		Assert.Equal(14, snapshot.ProjectedAvailableQuantity);
		Assert.Equal(16, snapshot.RequiredReplenishmentQuantity);
		Assert.Equal(21, snapshot.SuggestedPurchaseQuantity);
		Assert.False(snapshot.IsBlocked);
		Assert.Contains("On hand 20", snapshot.Explanation, StringComparison.Ordinal);
		Assert.Contains("eligible inbound 2", snapshot.Explanation, StringComparison.Ordinal);
		Assert.Contains("MOQ 7", snapshot.Explanation, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RecalculationIsDeterministicAndConversionIsIdempotent()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var warehouseId = await context.ScalarAsync("SELECT MIN(Id) FROM Warehouses;");
		await context.Data.ExecuteAsync(
			"INSERT INTO SupplierItems (SupplierId,ItemId,SupplierPartNumber,PurchasePrice,LeadTimeDays,MinimumOrderQuantity,IsPreferredSupplier,IsActive) VALUES($Supplier,$Item,$Part,10,5,6,1,1);",
			CancellationToken.None,
			new DatabaseParameter("$Supplier", context.SupplierId),
			new DatabaseParameter("$Item", context.ItemId),
			new DatabaseParameter("$Part", $"REPL-{Guid.NewGuid():N}"));

		var service = CreateService(context);
		var policy = await service.SavePolicyAsync(new ReplenishmentPolicy
		{
			ItemId = context.ItemId,
			WarehouseId = warehouseId,
			ReorderPoint = 5,
			SafetyStock = 2,
			TargetStock = 10,
			PreferredSupplierId = context.SupplierId
		});

		var first = await service.RecalculatePolicyAsync(policy.Id);
		var repeated = await service.RecalculatePolicyAsync(policy.Id);
		Assert.NotNull(first);
		Assert.NotNull(repeated);
		Assert.Equal(first!.Id, repeated!.Id);
		Assert.Equal(first.Snapshot.SnapshotKey, repeated.Snapshot.SnapshotKey);
		Assert.Equal(12, first.SuggestedQuantity);

		var conversion = await service.ConvertToPurchaseRequisitionAsync([first.Id]);
		var repeatedConversion = await service.ConvertToPurchaseRequisitionAsync([first.Id]);
		Assert.Equal(conversion.PurchaseRequisitionId, repeatedConversion.PurchaseRequisitionId);
		var requisition = await context.Sourcing.GetRequisitionAsync(conversion.PurchaseRequisitionId);
		Assert.NotNull(requisition);
		Assert.Equal(12, Assert.Single(requisition!.Lines).Quantity);
		Assert.Equal(context.SupplierId, requisition.PreferredSupplierId);
	}

	[Fact]
	public async Task MissingSupplierEvidenceProducesVisibleBlockedSuggestion()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var warehouseId = await context.ScalarAsync("SELECT MIN(Id) FROM Warehouses;");
		var service = CreateService(context);
		var policy = await service.SavePolicyAsync(new ReplenishmentPolicy
		{
			ItemId = context.SecondItemId,
			WarehouseId = warehouseId,
			ReorderPoint = 3,
			SafetyStock = 1,
			TargetStock = 8
		});

		var suggestion = await service.RecalculatePolicyAsync(policy.Id);
		Assert.NotNull(suggestion);
		Assert.Equal(ReplenishmentSuggestionStatus.Blocked, suggestion!.Status);
		Assert.True(suggestion.Snapshot.IsBlocked);
		Assert.Contains("No active supplier-item evidence", suggestion.Snapshot.BlockReason, StringComparison.Ordinal);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertToPurchaseRequisitionAsync([suggestion.Id]));
	}

	[Fact]
	public async Task PolicyUpdatesUseOptimisticConcurrencyAndBatchIsBounded()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var warehouseId = await context.ScalarAsync("SELECT MIN(Id) FROM Warehouses;");
		var service = CreateService(context);
		var created = await service.SavePolicyAsync(new ReplenishmentPolicy
		{
			ItemId = context.ItemId,
			WarehouseId = warehouseId,
			ReorderPoint = 2,
			SafetyStock = 1,
			TargetStock = 6
		});
		var staleVersion = created.Version;
		created.TargetStock = 7;
		var updated = await service.SavePolicyAsync(created);
		Assert.Equal(staleVersion + 1, updated.Version);

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.SavePolicyAsync(new ReplenishmentPolicy
		{
			Id = updated.Id,
			ItemId = updated.ItemId,
			WarehouseId = updated.WarehouseId,
			ReorderPoint = updated.ReorderPoint,
			SafetyStock = updated.SafetyStock,
			TargetStock = 8,
			Version = staleVersion
		}));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecalculateBatchAsync(count: 501));
	}

	private static ReplenishmentService CreateService(ProcurementTestContext context)
	{
		var auditRepository = new AuditRepository(context.Data);
		return new ReplenishmentService(
			new DatabaseTransactionRunner(context.Data),
			new ReplenishmentRepository(context.Data),
			new ItemRepository(context.Data),
			new WarehouseRepository(context.Data),
			new SupplierRepository(context.Data),
			context.Sourcing,
			auditRepository,
			new AuditService(auditRepository, context.Authorization),
			context.Authorization);
	}
}
