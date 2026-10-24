// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

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
		var originalIssue=Assert.Single(await fixture.Production.GetReversibleComponentIssuesAsync(order.Id));
		var reasonCode=(await new ReasonCodeRepository(context.Data).ListActiveAsync(CancellationToken.None)).First();
		var beforeRejection=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		var standaloneError=await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Movements.ReverseWithdrawalAsync(originalIssue.StockMovementId,reasonCode.Id,"Independent correction",CancellationToken.None));
		Assert.Contains("owning workflow",standaloneError.Message,StringComparison.Ordinal);
		Assert.Equal(beforeRejection,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));

		var correctionOperation=Guid.NewGuid();
		var corrected=await fixture.Production.ReverseComponentIssueAsync(order.Id,originalIssue.StockMovementId,reasonCode.Id,"Replace component issue",correctionOperation);
		Assert.Equal(0,Assert.Single(corrected.Requirements).IssuedQuantity);
		Assert.Equal(4L,await InventoryBalanceAsync(context,context.InventoryId));
		Assert.Empty(await fixture.Production.GetReversibleComponentIssuesAsync(order.Id));
		var afterCorrection=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		await fixture.Production.ReverseComponentIssueAsync(order.Id,originalIssue.StockMovementId,reasonCode.Id,"Replace component issue",correctionOperation);
		Assert.Equal(afterCorrection,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.ReverseComponentIssueAsync(order.Id,originalIssue.StockMovementId,reasonCode.Id,"Changed correction request",correctionOperation));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.CompleteAsync(order.Id));
		Assert.Equal(afterCorrection,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		await fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,4,operationId:Guid.NewGuid());

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

		var replacementIssue=Assert.Single(await fixture.Production.GetReversibleComponentIssuesAsync(order.Id));
		var beforeCompletedCorrection=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.ReverseComponentIssueAsync(order.Id,replacementIssue.StockMovementId,reasonCode.Id,"Completed component correction"));
		Assert.Equal(beforeCompletedCorrection,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		var reversalOperation=Guid.NewGuid();
		var reversed=await fixture.Production.ReverseAsync(order.Id,reasonCode.Id,"Assembly test reversal",reversalOperation);
		var afterReversal=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		var retriedReversal=await fixture.Production.ReverseAsync(order.Id,reasonCode.Id,"Assembly test reversal",reversalOperation);
		Assert.Equal(ProductionOrderStatus.Reversed,reversed.Status);
		Assert.Equal(reversed.Id,retriedReversal.Id);
		Assert.Equal(4L,await InventoryBalanceAsync(context,context.InventoryId));
		Assert.Equal(0L,await InventoryBalanceAsync(context,context.SecondInventoryId));
		Assert.Equal(0,Assert.Single(retriedReversal.Requirements).IssuedQuantity);
		Assert.Equal(afterReversal,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		Assert.Equal(3L,await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId IS NOT NULL;"));
	}

	[Fact]
	public async Task ComponentIssuesUseOnlyUnreservedStockAndRollBackAtFinalPersistence()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,10,null,"COMPONENT-STOCK",null,CancellationToken.None);
		var order=await CreateReleasedOrderAsync(context,fixture.Production);
		var requirement=Assert.Single(order.Requirements);
		var reservation=await CreateReservationAsync(context,10);
		var before=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		Assert.Equal(0L,Assert.Single(await fixture.Production.GetAvailabilityAsync(order.Id)).ReservableQuantity);
		await Assert.ThrowsAsync<InsufficientStockException>(()=>fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,1));
		Assert.Equal(before,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));

		await context.Data.ExecuteAsync("UPDATE InventoryReservations SET Quantity=8,Version=Version+1 WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Id",reservation.ReservationId));
		await context.Data.ExecuteAsync("UPDATE SalesOrderLines SET ReservedQuantity=8,Version=Version+1 WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Id",reservation.OrderLineId));
		await Assert.ThrowsAsync<InsufficientStockException>(()=>fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,3));
		await fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,2);
		Assert.Equal(8L,await InventoryBalanceAsync(context,context.InventoryId));
		Assert.Equal(8L,await context.ScalarAsync("SELECT Quantity FROM InventoryReservations WHERE Id=$Id;",new DatabaseParameter("$Id",reservation.ReservationId)));

		await context.Data.ExecuteAsync("UPDATE InventoryReservations SET Status=$Released,Version=Version+1 WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Released",(int)InventoryReservationStatus.Released),new DatabaseParameter("$Id",reservation.ReservationId));
		await context.Data.ExecuteAsync("UPDATE SalesOrderLines SET ReservedQuantity=0,Version=Version+1 WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Id",reservation.OrderLineId));
		await context.Data.ExecuteAsync("CREATE TRIGGER FailComponentIssue BEFORE INSERT ON AuditEntries WHEN NEW.Action='ComponentIssued' BEGIN SELECT RAISE(ABORT,'forced component issue failure'); END;",CancellationToken.None);
		var beforeFailure=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		var operation=Guid.NewGuid();
		await Assert.ThrowsAsync<SqliteException>(()=>fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,2,operationId:operation));
		Assert.Equal(beforeFailure,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		await context.Data.ExecuteAsync("DROP TRIGGER FailComponentIssue;",CancellationToken.None);
		var issued=await fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,2,operationId:operation);
		Assert.Equal(4,Assert.Single(issued.Requirements).IssuedQuantity);
		Assert.Equal(6L,await InventoryBalanceAsync(context,context.InventoryId));
	}

	[Fact]
	public async Task ComponentCorrectionsRetainTrackingAndRollBackAtFinalPersistence()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		await MovementPostingTestSupport.EnableLotTrackingAsync(context);
		await fixture.Movements.AddPurchaseAsync(context.InventoryId,4,5m,null,"COMPONENT-LOT",null,[new TrackingAllocationInput{Code="COMPONENT-LOT",Quantity=4}]);
		var order=await CreateReleasedOrderAsync(context,fixture.Production);
		var otherOrder=await CreateReleasedOrderAsync(context,fixture.Production);
		var requirement=Assert.Single(order.Requirements);
		await fixture.Production.IssueComponentAsync(order.Id,requirement.Id,context.InventoryId,4,[new TrackingAllocationInput{Code="COMPONENT-LOT",Quantity=4}]);
		var issue=Assert.Single(await fixture.Production.GetReversibleComponentIssuesAsync(order.Id));
		var reasonCode=(await new ReasonCodeRepository(context.Data).ListActiveAsync(CancellationToken.None)).First();
		var before=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.ReverseComponentIssueAsync(otherOrder.Id,issue.StockMovementId,reasonCode.Id,"Wrong production order"));
		Assert.Equal(before,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		await context.Data.ExecuteAsync("CREATE TRIGGER FailComponentCorrection BEFORE INSERT ON AuditEntries WHEN NEW.Action='ComponentIssueReversed' BEGIN SELECT RAISE(ABORT,'forced component correction failure'); END;",CancellationToken.None);
		var operation=Guid.NewGuid();
		await Assert.ThrowsAsync<SqliteException>(()=>fixture.Production.ReverseComponentIssueAsync(order.Id,issue.StockMovementId,reasonCode.Id,"Restore issued component",operation));
		Assert.Equal(before,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
		await context.Data.ExecuteAsync("DROP TRIGGER FailComponentCorrection;",CancellationToken.None);
		var corrected=await fixture.Production.ReverseComponentIssueAsync(order.Id,issue.StockMovementId,reasonCode.Id,"Restore issued component",operation);
		Assert.Equal(0,Assert.Single(corrected.Requirements).IssuedQuantity);
		Assert.Equal(4L,await InventoryBalanceAsync(context,context.InventoryId));
		Assert.Equal(1L,await context.ScalarAsync("SELECT COUNT(*) FROM StockMovementTracking original INNER JOIN StockMovements reversal ON reversal.ReversalOfMovementId=original.StockMovementId INNER JOIN StockMovementTracking restored ON restored.StockMovementId=reversal.Id AND restored.TrackingUnitId=original.TrackingUnitId AND restored.Quantity=-original.Quantity WHERE original.StockMovementId=$Id;",new DatabaseParameter("$Id",issue.StockMovementId)));
		var after=await CaptureExecutionStateAsync(context,fixture.Production,order.Id);
		await fixture.Production.ReverseComponentIssueAsync(order.Id,issue.StockMovementId,reasonCode.Id,"Restore issued component",operation);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Production.ReverseComponentIssueAsync(order.Id,issue.StockMovementId,reasonCode.Id,"Duplicate correction"));
		Assert.Equal(after,await CaptureExecutionStateAsync(context,fixture.Production,order.Id));
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

	private static async Task<ProductionOrder> CreateReleasedOrderAsync(ProcurementTestContext context,ProductionService production)
	{
		var bom=await production.SaveBomAsync(new BillOfMaterial{FinishedItemId=context.SecondItemId,Revision=$"CORRECTION-{Guid.NewGuid():N}",Lines=[new BillOfMaterialLine{ComponentItemId=context.ItemId,Quantity=2m,Sequence=10}]});
		bom=await production.ActivateBomAsync(bom.Id,bom.Version);
		var order=await production.CreateOrderAsync(context.SecondItemId,bom.Id,2,await GetWarehouseIdAsync(context,context.SecondInventoryId),context.SecondInventoryId);
		return await production.ReleaseOrderAsync(order.Id,order.Version);
	}

	private static async Task<(long ReservationId,long OrderLineId)> CreateReservationAsync(ProcurementTestContext context,int quantity)
	{
		var user=context.Authorization.CurrentUser??throw new InvalidOperationException("A signed-in test user is required.");
		var customer=await context.Data.InsertAsync("INSERT INTO Customers(CustomerNumber,Name) VALUES($Number,'Component reservation customer');",CancellationToken.None,new DatabaseParameter("$Number",$"COMPONENT-{Guid.NewGuid():N}"));
		var order=await context.Data.InsertAsync("INSERT INTO SalesOrders(OrderNumber,CustomerId,OrderDate,Status,CreatedByUserId) VALUES($Number,$Customer,$Date,$Status,$User);",CancellationToken.None,new DatabaseParameter("$Number",$"RESERVATION-{Guid.NewGuid():N}"),new DatabaseParameter("$Customer",customer),new DatabaseParameter("$Date",DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new DatabaseParameter("$Status",(int)SalesOrderStatus.Released),new DatabaseParameter("$User",user.Id));
		var line=await context.Data.InsertAsync("INSERT INTO SalesOrderLines(SalesOrderId,LineNumber,ItemId,PartNumber,Description,Quantity,ReservedQuantity) VALUES($Order,1,$Item,'COMPONENT','Reserved component',$Quantity,$Quantity);",CancellationToken.None,new DatabaseParameter("$Order",order),new DatabaseParameter("$Item",context.ItemId),new DatabaseParameter("$Quantity",quantity));
		var reservation=await context.Data.InsertAsync("INSERT INTO InventoryReservations(SalesOrderLineId,InventoryId,Quantity,Status,CreatedAtUtc,CreatedByUserId) VALUES($Line,$Inventory,$Quantity,$Active,$At,$User);",CancellationToken.None,new DatabaseParameter("$Line",line),new DatabaseParameter("$Inventory",context.InventoryId),new DatabaseParameter("$Quantity",quantity),new DatabaseParameter("$Active",(int)InventoryReservationStatus.Active),new DatabaseParameter("$At",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)),new DatabaseParameter("$User",user.Id));
		return (reservation,line);
	}

	private static async Task<ProductionExecutionState> CaptureExecutionStateAsync(ProcurementTestContext context,ProductionService production,long orderId)
	{
		var order=await production.GetOrderAsync(orderId)??throw new InvalidOperationException("The production order was not found.");
		return new ProductionExecutionState(
			await InventoryBalanceAsync(context,context.InventoryId),await InventoryBalanceAsync(context,context.SecondInventoryId),
			await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements;"),await context.ScalarAsync("SELECT COUNT(*) FROM StockMovementTracking;"),
			await context.ScalarAsync("SELECT COALESCE(SUM(Quantity),0) FROM StockMovementTracking;"),await context.ScalarAsync("SELECT COUNT(*) FROM ProductionMaterialMovements;"),
			await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries;"),order.Version,order.Status,string.Join(";",order.Requirements.Select(value=>$"{value.Id}:{value.IssuedQuantity}")));
	}

	private sealed record ProductionExecutionState(long ComponentQuantity,long FinishedQuantity,long MovementCount,long TrackingCount,long TrackingQuantity,long EvidenceCount,long AuditCount,long OrderVersion,ProductionOrderStatus Status,string IssuedQuantities);

	private static string FindRepositoryRoot()
	{
		for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
			if(File.Exists(Path.Combine(directory.FullName,"Depot.slnx")))return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}

	private sealed record ProductionFixture(ProductionService Production,MovementService Movements,ItemCostCalculationService Costs);
}
