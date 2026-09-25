// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ProductionAssemblyTests
{
	[Fact]
	public async Task BomRevisionSnapshotRemainsStableAndCyclesAreRejected()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		var warehouseId=await GetWarehouseIdAsync(context,context.SecondInventoryId);

		var first=await fixture.Production.SaveBomAsync(new BillOfMaterial
		{
			FinishedItemId=context.SecondItemId,
			Revision="A",
			Lines=[new BillOfMaterialLine{ComponentItemId=context.ItemId,Quantity=2m,Sequence=10}]
		});
		first=await fixture.Production.ActivateBomAsync(first.Id,first.Version);

		var order=await fixture.Production.CreateOrderAsync(context.SecondItemId,first.Id,2,warehouseId,context.SecondInventoryId);
		order=await fixture.Production.ReleaseOrderAsync(order.Id,order.Version);
		var requirement=Assert.Single(order.Requirements);
		Assert.Equal(4,requirement.RequiredQuantity);
		Assert.Equal("A",order.BillOfMaterialRevision);

		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.SaveBomAsync(new BillOfMaterial
		{
			Id=first.Id,
			FinishedItemId=first.FinishedItemId,
			Revision=first.Revision,
			Status=first.Status,
			EffectiveFromUtc=first.EffectiveFromUtc,
			EffectiveUntilUtc=first.EffectiveUntilUtc,
			Version=first.Version,
			Lines=first.Lines
		}));

		var second=await fixture.Production.SaveBomAsync(new BillOfMaterial
		{
			FinishedItemId=context.SecondItemId,
			Revision="B",
			Lines=[new BillOfMaterialLine{ComponentItemId=context.ItemId,Quantity=3m,Sequence=10}]
		});
		await fixture.Production.ActivateBomAsync(second.Id,second.Version);

		var persisted=await fixture.Production.GetOrderAsync(order.Id);
		var persistedRequirement=Assert.Single(Assert.IsType<ProductionOrder>(persisted).Requirements);
		Assert.Equal(4,persistedRequirement.RequiredQuantity);
		Assert.Equal("A",persisted!.BillOfMaterialRevision);

		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.SaveBomAsync(new BillOfMaterial
		{
			FinishedItemId=context.ItemId,
			Revision="CYCLE",
			Lines=[new BillOfMaterialLine{ComponentItemId=context.SecondItemId,Quantity=1m,Sequence=10}]
		}));
	}

	[Fact]
	public async Task IssueCompletionAndReversalUseInventoryAndCostAuthoritiesIdempotently()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		var warehouseId=await GetWarehouseIdAsync(context,context.SecondInventoryId);
		await fixture.Costs.SaveProfileAsync(new ItemCostProfile
		{
			ItemId=context.ItemId,
			BaseCostSource=ItemCostBaseSource.ManualStandard,
			Currency="EUR",
			ManualStandardCost=5m
		});
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,4,null,"PRODUCTION-TEST-STOCK","Production test component stock",CancellationToken.None);

		var bom=await fixture.Production.SaveBomAsync(new BillOfMaterial
		{
			FinishedItemId=context.SecondItemId,
			Revision="A",
			Lines=[new BillOfMaterialLine{ComponentItemId=context.ItemId,Quantity=2m,Sequence=10}]
		});
		bom=await fixture.Production.ActivateBomAsync(bom.Id,bom.Version);
		var order=await fixture.Production.CreateOrderAsync(context.SecondItemId,bom.Id,2,warehouseId,context.SecondInventoryId);
		order=await fixture.Production.ReleaseOrderAsync(order.Id,order.Version);
		var requirement=Assert.Single(order.Requirements);

		var issueOperation=Guid.NewGuid();
		var issued=await fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,4,operationId:issueOperation);
		var retriedIssue=await fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,4,operationId:issueOperation);
		Assert.Equal(ProductionOrderStatus.InProgress,issued.Status);
		Assert.Equal(4,Assert.Single(retriedIssue.Requirements).IssuedQuantity);
		Assert.Equal(0L,await InventoryBalanceAsync(context,context.InventoryId));

		var completeOperation=Guid.NewGuid();
		var completed=await fixture.Production.CompleteAsync(order.Id,operationId:completeOperation);
		var retriedCompletion=await fixture.Production.CompleteAsync(order.Id,operationId:completeOperation);
		Assert.Equal(ProductionOrderStatus.Completed,completed.Order.Status);
		Assert.Equal(completed.Order.Id,retriedCompletion.Order.Id);
		Assert.Equal(20m,completed.Cost.TotalComponentCost);
		Assert.Equal(10m,completed.Cost.UnitFinishedCost);
		Assert.Equal("EUR",completed.Cost.Currency);
		Assert.Equal(2L,await InventoryBalanceAsync(context,context.SecondInventoryId));
		Assert.Single(await fixture.Production.GetCostEvidenceAsync(order.Id));

		var reasonCode=(await new ReasonCodeRepository(context.Data).ListActiveAsync(CancellationToken.None)).First();
		var reversalOperation=Guid.NewGuid();
		var reversed=await fixture.Production.ReverseAsync(order.Id,reasonCode.Id,"Assembly test reversal",reversalOperation);
		var retriedReversal=await fixture.Production.ReverseAsync(order.Id,reasonCode.Id,"Assembly test reversal",reversalOperation);
		Assert.Equal(ProductionOrderStatus.Reversed,reversed.Status);
		Assert.Equal(reversed.Id,retriedReversal.Id);
		Assert.Equal(4L,await InventoryBalanceAsync(context,context.InventoryId));
		Assert.Equal(0L,await InventoryBalanceAsync(context,context.SecondInventoryId));
	}

	[Fact]
	public async Task ProductionSchemaWorkspaceAndAuthorityBoundariesAreIntegrated()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		Assert.Equal(ProductionSchemaMigration.CurrentVersion,Convert.ToInt32(await context.Data.ExecuteScalarAsync(
			"SELECT Version FROM DepotFeatureVersions WHERE Name='Production';",CancellationToken.None),CultureInfo.InvariantCulture));
		foreach(var table in new[]{"ProductionBillsOfMaterial","ProductionBillOfMaterialLines","ProductionOrders","ProductionOrderRequirements","ProductionMaterialMovements","ProductionAssemblyCostEvidence"})
			Assert.Equal(1L,Convert.ToInt64(await context.Data.ExecuteScalarAsync(
				"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$Name;",CancellationToken.None,new DatabaseParameter("$Name",table)),CultureInfo.InvariantCulture));

		var root=FindRepositoryRoot();
		var service=File.ReadAllText(Path.Combine(root,"src","Depot","Services","ProductionService.cs"));
		var viewModel=File.ReadAllText(Path.Combine(root,"src","Depot","ViewModels","ProductionViewModel.cs"));
		var view=File.ReadAllText(Path.Combine(root,"src","Depot","Views","ProductionView.xaml"));
		var providers=File.ReadAllText(Path.Combine(root,"src","Depot","Services","MyWorkProviders.cs"));
		var composition=File.ReadAllText(Path.Combine(root,"src","Depot","Composition","ServiceComposition.cs"));

		Assert.Contains("_movements.AddWithdrawalInTransactionAsync",service,StringComparison.Ordinal);
		Assert.Contains("_movements.AddCorrectionInTransactionAsync",service,StringComparison.Ordinal);
		Assert.Contains("_itemCosts.CalculateAsync",service,StringComparison.Ordinal);
		Assert.Contains("_replenishment.RecalculateItemWarehouseAsync",service,StringComparison.Ordinal);
		Assert.DoesNotContain("UPDATE Inventories",service,StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("ProductionRepository",viewModel,StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess",viewModel,StringComparison.Ordinal);
		Assert.Contains("<controls:PageHeader",view,StringComparison.Ordinal);
		Assert.Contains("<controls:OperationPanel",view,StringComparison.Ordinal);
		Assert.Contains("ProductionMyWorkProvider",providers,StringComparison.Ordinal);
		Assert.Contains("new ProductionMyWorkProvider(Production)",composition,StringComparison.Ordinal);
	}

	private static ProductionFixture CreateFixture(ProcurementTestContext context)
	{
		var data=context.Data;
		var authorization=context.Authorization;
		var runner=new DatabaseTransactionRunner(data);
		var auditRepository=new AuditRepository(data);
		var audit=new AuditService(auditRepository,authorization);
		var items=new ItemRepository(data);
		var warehouses=new WarehouseRepository(data);
		var inventories=new InventoryRepository(data);
		var movementsRepository=new StockMovementRepository(data);
		var reasonCodes=new ReasonCodeRepository(data);
		var traceability=new ItemTraceabilityService(new ItemTraceabilityRepository(data),audit);
		var reversals=new StockMovementReversalService(runner,inventories,movementsRepository,reasonCodes,auditRepository,audit,traceability);
		var movements=new MovementService(items,inventories,reasonCodes,movementsRepository,audit,reversals,runner,auditRepository,traceability);
		var costs=new ItemCostCalculationService(runner,new ItemCostRepository(data),auditRepository,audit,authorization);
		var replenishment=new ReplenishmentService(
			runner,
			new ReplenishmentRepository(data),
			items,
			warehouses,
			new SupplierRepository(data),
			context.Sourcing,
			auditRepository,
			audit,
			authorization);
		var production=new ProductionService(
			runner,
			new ProductionRepository(data),
			items,
			warehouses,
			movements,
			movementsRepository,
			reversals,
			costs,
			replenishment,
			auditRepository,
			audit,
			authorization);
		return new ProductionFixture(production,movements,costs);
	}

	private static async Task<long> GetWarehouseIdAsync(ProcurementTestContext context,long inventoryId)=>
		Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT sl.WarehouseId FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId WHERE inv.Id=$Id;",
			CancellationToken.None,new DatabaseParameter("$Id",inventoryId)),CultureInfo.InvariantCulture);

	private static async Task<long> InventoryBalanceAsync(ProcurementTestContext context,long inventoryId)=>
		Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$Id;",
			CancellationToken.None,new DatabaseParameter("$Id",inventoryId)),CultureInfo.InvariantCulture);

	private static string FindRepositoryRoot()
	{
		for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
			if(File.Exists(Path.Combine(directory.FullName,"Depot.slnx")))return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}

	private sealed record ProductionFixture(ProductionService Production,MovementService Movements,ItemCostCalculationService Costs);
}
