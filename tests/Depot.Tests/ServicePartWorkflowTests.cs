// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ServicePartWorkflowTests
{
	[Fact]
	public async Task EveryPartsPersistenceBoundaryRollsBackAndAllowsTheSameOperationToRetry()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=ServiceManagementTests.CreateFixture(context);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,20,null,"PARTS-ATOMIC","Opening stock",CancellationToken.None);
		string[] failures=
		[
			"BEFORE INSERT ON AuditEntries WHEN NEW.EntityType='StockMovement'",
			"BEFORE INSERT ON ServicePartEvidence",
			"BEFORE INSERT ON ServicePartOperations",
			"BEFORE INSERT ON AuditEntries WHEN NEW.EntityType='ServicePartEvidence'",
			"BEFORE UPDATE ON ServiceOrders WHEN NEW.Version>OLD.Version",
			"BEFORE INSERT ON AuditEntries WHEN NEW.EntityType='ServiceOrder' AND NEW.Action='PartConsumed'"
		];
		foreach(var failure in failures)
		{
			var order=await CreateOrderAsync(context,fixture.Service);
			var operation=Guid.NewGuid();
			await context.Data.ExecuteAsync($"CREATE TRIGGER RejectServicePart {failure} BEGIN SELECT RAISE(ABORT,'part persistence rejected'); END;",CancellationToken.None);
			var before=await SnapshotAsync(context.Data);
			try
			{
				await Assert.ThrowsAsync<SqliteException>(()=>fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,1,null,12m,true,19m,operationId:operation));
				Assert.Equal(before,await SnapshotAsync(context.Data));
			}
			finally
			{
				await context.Data.ExecuteAsync("DROP TRIGGER RejectServicePart;",CancellationToken.None);
			}
			var posted=await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,1,null,12m,true,19m,operationId:operation);
			var after=await fixture.Service.GetOrderAsync(order.Id)??throw new InvalidOperationException("Service order missing.");
			Assert.Equal(ServiceOrderStatus.InProgress,after.Status);
			Assert.Equal(order.Version+1,after.Version);
			Assert.Equal(operation,posted.OperationId);
			Assert.Single(await fixture.Service.ListPartEvidenceAsync(order.Id));
			var committed=await SnapshotAsync(context.Data);
			Assert.Equal(posted.Id,(await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,1,null,12m,true,19m,operationId:operation)).Id);
			Assert.Equal(committed,await SnapshotAsync(context.Data));
		}
		Assert.Equal(14L,await QuantityAsync(context));
	}

	[Fact]
	public async Task CancellationBeforeCommitAndLostSuccessResponseHaveDifferentDurableOutcomes()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var runner=new ControlledRunner(new DatabaseTransactionRunner(context.Data));
		var fixture=ServiceManagementTests.CreateFixture(context,runner);
		var order=await CreateOrderAsync(context,fixture.Service);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,5,null,"PARTS-RETRY","Opening stock",CancellationToken.None);
		var operation=Guid.NewGuid();
		var before=await SnapshotAsync(context.Data);
		using(var cancellation=new CancellationTokenSource())
		{
			runner.CancelBeforeCommit=cancellation;
			await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,2,null,7m,true,19m,operationId:operation,token:cancellation.Token));
		}
		Assert.Equal(before,await SnapshotAsync(context.Data));
		runner.LoseNextResponse=true;
		await Assert.ThrowsAsync<IOException>(()=>fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,2,null,7m,true,19m,operationId:operation));
		Assert.Equal(3L,await QuantityAsync(context));
		var retained=Assert.Single(await fixture.Service.ListPartEvidenceAsync(order.Id));
		var committed=await SnapshotAsync(context.Data);
		var recreated=ServiceManagementTests.CreateFixture(context);
		Assert.Equal(retained.Id,(await recreated.Service.ConsumePartAsync(order.Id,context.InventoryId,2,null,7m,true,19m,operationId:operation)).Id);
		Assert.Equal(committed,await SnapshotAsync(context.Data));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>recreated.Service.ConsumePartAsync(order.Id,context.InventoryId,3,null,7m,true,19m,operationId:operation));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>recreated.Service.ConsumePartAsync(order.Id,context.InventoryId,2,null,8m,true,19m,operationId:operation));
		Assert.Equal(committed,await SnapshotAsync(context.Data));
		var current=await recreated.Service.GetOrderAsync(order.Id)??throw new InvalidOperationException("Service order missing.");
		await recreated.Service.CompleteServiceOrderAsync(order.Id,current.Version,"Verified completion.");
		var final=await SnapshotAsync(context.Data);
		Assert.Equal(retained.Id,(await recreated.Service.ConsumePartAsync(order.Id,context.InventoryId,2,null,7m,true,19m,operationId:operation)).Id);
		Assert.Equal(final,await SnapshotAsync(context.Data));
	}

	[Fact]
	public async Task ReturnsRequireOriginalConsumptionAndPreserveItsRemainingQuantityAndCommercialSnapshot()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=ServiceManagementTests.CreateFixture(context);
		var order=await CreateOrderAsync(context,fixture.Service);
		var other=await CreateOrderAsync(context,fixture.Service);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,5,null,"PARTS-RETURN","Opening stock",CancellationToken.None);
		var before=await SnapshotAsync(context.Data);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,long.MaxValue,1,null));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		var consumed=await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,3,null,7.5m,true,19m);
		before=await SnapshotAsync(context.Data);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(other.Id,consumed.Id,1,null));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		var key=Guid.NewGuid();
		var returned=await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,operationId:key);
		Assert.Equal(consumed.Id,returned.OriginalConsumptionId);
		Assert.Equal(consumed.InventoryId,returned.InventoryId);
		Assert.Equal(consumed.UnitPrice,returned.UnitPrice);
		Assert.Equal(consumed.TaxRate,returned.TaxRate);
		Assert.False(returned.Billable);
		before=await SnapshotAsync(context.Data);
		Assert.Equal(returned.Id,(await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,operationId:key)).Id);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,2,null,operationId:key));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,3,null));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,returned.Id,1,null));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,2,null);
		Assert.Equal(5L,await QuantityAsync(context));
		before=await SnapshotAsync(context.Data);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null));
		Assert.Equal(before,await SnapshotAsync(context.Data));
	}

	[Fact]
	public async Task FailedReturnFinalizationRestoresTheOriginalReturnableBalance()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=ServiceManagementTests.CreateFixture(context);
		var order=await CreateOrderAsync(context,fixture.Service);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,4,null,"PARTS-RETURN-FAIL","Opening stock",CancellationToken.None);
		var consumed=await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,4,null,4m,true,19m);
		var key=Guid.NewGuid();
		await context.Data.ExecuteAsync("CREATE TRIGGER RejectServiceReturn BEFORE INSERT ON AuditEntries WHEN NEW.EntityType='ServiceOrder' AND NEW.Action='PartReturned' BEGIN SELECT RAISE(ABORT,'return finalization rejected'); END;",CancellationToken.None);
		var before=await SnapshotAsync(context.Data);
		try
		{
			await Assert.ThrowsAsync<SqliteException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,4,null,operationId:key));
			Assert.Equal(before,await SnapshotAsync(context.Data));
		}
		finally{await context.Data.ExecuteAsync("DROP TRIGGER RejectServiceReturn;",CancellationToken.None);}
		await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,4,null,operationId:key);
		Assert.Equal(4L,await QuantityAsync(context));
		Assert.Equal(2,(await fixture.Service.ListPartEvidenceAsync(order.Id)).Count);
	}

	[Fact]
	public async Task ConcurrentReturnsShareOneOriginalBalanceAndRetryIdentity()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var runner=new ControlledRunner(new DatabaseTransactionRunner(context.Data));
		var fixture=ServiceManagementTests.CreateFixture(context,runner);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,6,null,"PARTS-CONCURRENT","Opening stock",CancellationToken.None);
		foreach(var sameOperation in new[]{false,true})
		{
			var order=await CreateOrderAsync(context,fixture.Service);
			var consumed=await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,3,null,5m,true,19m);
			var key=Guid.NewGuid();
			ServicePartEvidence? first=null,second=null;
			var failure=await runner.CompeteAsync(
				async()=>{first=await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,3,null,operationId:key);},
				async()=>{second=await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,3,null,operationId:sameOperation?key:Guid.NewGuid());});
			if(sameOperation){Assert.Null(failure);Assert.Equal(first?.Id,second?.Id);Assert.NotNull(second);}
			else Assert.IsType<InvalidOperationException>(failure);
			Assert.Equal(6L,await QuantityAsync(context));
			Assert.Single(await fixture.Service.ListPartEvidenceAsync(order.Id),value=>value.MovementKind==ServicePartMovementKind.Return);
		}
	}

	[Fact]
	public async Task PartsAndFinalTransitionsSerializeInBothOrders()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var runner=new ControlledRunner(new DatabaseTransactionRunner(context.Data));
		var fixture=ServiceManagementTests.CreateFixture(context,runner);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,10,null,"PARTS-FINAL","Opening stock",CancellationToken.None);
		foreach(var cancel in new[]{false,true})
		foreach(var finalFirst in new[]{false,true})
		{
			var order=await CreateOrderAsync(context,fixture.Service);
			await fixture.Service.AddWorkLineAsync(order.Id,"Completion evidence",1m,1m,false,0m);
			var quantity=await QuantityAsync(context);
			Func<Task> post=()=>fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,1,null,1m,true,0m);
			Func<Task> final=()=>cancel?fixture.Service.CancelServiceOrderAsync(order.Id,order.Version,"Cancelled by another session."):fixture.Service.CompleteServiceOrderAsync(order.Id,order.Version,"Completed by another session.");
			var failure=await runner.CompeteAsync(finalFirst?final:post,finalFirst?post:final);
			Assert.IsAssignableFrom<InvalidOperationException>(failure);
			var current=await fixture.Service.GetOrderAsync(order.Id)??throw new InvalidOperationException("Service order missing.");
			Assert.Equal(finalFirst?(cancel?ServiceOrderStatus.Cancelled:ServiceOrderStatus.Completed):ServiceOrderStatus.InProgress,current.Status);
			Assert.Equal(finalFirst?0:1,(await fixture.Service.ListPartEvidenceAsync(order.Id)).Count);
			Assert.Equal(quantity-(finalFirst?0:1),await QuantityAsync(context));
		}
	}

	[Theory]
	[InlineData(ItemTrackingMode.SerialNumber)]
	[InlineData(ItemTrackingMode.Lot)]
	public async Task ReturnedTrackingMustMatchEachOriginalIdentity(ItemTrackingMode mode)
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=ServiceManagementTests.CreateFixture(context);
		var order=await CreateOrderAsync(context,fixture.Service);
		await context.Data.ExecuteAsync("UPDATE Items SET TrackingMode=$Mode WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Mode",(int)mode),new DatabaseParameter("$Id",context.ItemId));
		var firstQuantity=mode==ItemTrackingMode.SerialNumber?1:2;
		var quantity=firstQuantity+1;
		var expiry=DateTime.Today.AddYears(2);
		TrackingAllocationInput[] allocations=[new(){Code="PART-A",Quantity=firstQuantity,ExpiryDate=expiry},new(){Code="PART-B",Quantity=1,ExpiryDate=expiry}];
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,quantity,null,"TRACKED-PARTS","Opening stock",allocations);
		var before=await SnapshotAsync(context.Data);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,quantity,null,3m,true,19m));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		var consumed=await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,quantity,null,3m,true,19m,allocations);
		var changedMode=mode==ItemTrackingMode.SerialNumber?ItemTrackingMode.Lot:ItemTrackingMode.SerialNumber;
		await context.Data.ExecuteAsync("UPDATE Items SET TrackingMode=$Mode WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Mode",(int)changedMode),new DatabaseParameter("$Id",context.ItemId));
		before=await SnapshotAsync(context.Data);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,[new(){Code="PART-A",Quantity=1}]));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		await context.Data.ExecuteAsync("UPDATE Items SET TrackingMode=$Mode WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Mode",(int)mode),new DatabaseParameter("$Id",context.ItemId));
		before=await SnapshotAsync(context.Data);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,[new(){Code="PART-OTHER",Quantity=1}]));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,[new(){Code="PART-A",Quantity=1,ExpiryDate=expiry.AddDays(1)}]));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		var key=Guid.NewGuid();
		var returned=await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,firstQuantity,null,[new(){Code="part-a",Quantity=firstQuantity}],key);
		before=await SnapshotAsync(context.Data);
		Assert.Equal(returned.Id,(await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,firstQuantity,null,[new(){Code=" PART-A ",Quantity=firstQuantity}],key)).Id);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,[new(){Code="PART-A",Quantity=1}]));
		Assert.Equal(before,await SnapshotAsync(context.Data));
		await fixture.Service.ReturnPartAsync(order.Id,consumed.Id,1,null,[new(){Code="PART-B",Quantity=1}]);
		Assert.Equal(quantity,await QuantityAsync(context));
		var balances=await context.Data.QueryAsync("SELECT unit.Code,SUM(tracking.Quantity) FROM StockMovementTracking tracking INNER JOIN ItemTrackingUnits unit ON unit.Id=tracking.TrackingUnitId WHERE unit.ItemId=$Id GROUP BY unit.Code ORDER BY unit.Code;",reader=>(reader.GetString(0),Convert.ToInt64(reader.GetValue(1),CultureInfo.InvariantCulture)),CancellationToken.None,new DatabaseParameter("$Id",context.ItemId));
		Assert.Equal(new[]{("PART-A",(long)firstQuantity),("PART-B",1L)},balances);
	}

	[Fact]
	public async Task VersionOneUpgradeRetainsEvidenceAndDoesNotGuessHistoricalReturnLinks()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=ServiceManagementTests.CreateFixture(context);
		var cleanOrder=await CreateOrderAsync(context,fixture.Service);
		var ambiguousOrder=await CreateOrderAsync(context,fixture.Service);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,8,null,"LEGACY-PARTS","Opening stock",CancellationToken.None);
		var clean=await fixture.Service.ConsumePartAsync(cleanOrder.Id,context.InventoryId,2,null,3m,true,19m);
		var ambiguous=await fixture.Service.ConsumePartAsync(ambiguousOrder.Id,context.InventoryId,3,null,3m,true,19m);
		await fixture.Service.ReturnPartAsync(ambiguousOrder.Id,ambiguous.Id,1,null);
		await context.Data.ExecuteAsync("DROP TABLE ServicePartOperations; UPDATE DepotFeatureVersions SET Version=1 WHERE Name='ServiceManagement';",CancellationToken.None);
		var retained=await context.Data.QueryAsync("SELECT Id,StockMovementId,Quantity FROM ServicePartEvidence ORDER BY Id;",reader=>(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2)),CancellationToken.None);

		DatabaseProvisioningService.Initialize(context.ConnectionFactory);
		ServiceManagementSchemaMigration.Migrate(context.ConnectionFactory);

		Assert.Equal(2L,Convert.ToInt64(await context.Data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='ServiceManagement';",CancellationToken.None),CultureInfo.InvariantCulture));
		Assert.Equal(retained,await context.Data.QueryAsync("SELECT Id,StockMovementId,Quantity FROM ServicePartEvidence ORDER BY Id;",reader=>(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2)),CancellationToken.None));
		Assert.Null(Assert.Single(await fixture.Service.ListPartEvidenceAsync(cleanOrder.Id)).OperationId);
		await fixture.Service.ReturnPartAsync(cleanOrder.Id,clean.Id,1,null);
		var before=await SnapshotAsync(context.Data);
		var rejected=await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReturnPartAsync(ambiguousOrder.Id,ambiguous.Id,1,null));
		Assert.Contains("Historical returns",rejected.Message,StringComparison.Ordinal);
		Assert.Equal(before,await SnapshotAsync(context.Data));
	}

	private static async Task<ServiceOrder> CreateOrderAsync(ProcurementTestContext context,ServiceManagementService service)
	{
		var customer=await new CustomerRepository(context.Data).SaveAsync(new Customer{Name=$"Service parts {Guid.NewGuid():N}",Currency="EUR",IsActive=true},CancellationToken.None);
		var serviceCase=await service.SaveCaseAsync(new ServiceCase{CustomerId=customer.Id,Subject="Parts workflow verification"});
		return await service.CreateServiceOrderAsync(serviceCase.Id,null,null,null,null,null,null);
	}

	private static async Task<long> QuantityAsync(ProcurementTestContext context)=>Convert.ToInt64(await context.Data.ExecuteScalarAsync("SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$Id;",CancellationToken.None,new DatabaseParameter("$Id",context.InventoryId)),CultureInfo.InvariantCulture);

	private static async Task<string> SnapshotAsync(DatabaseAccess data)
	{
		var result=new Dictionary<string,IReadOnlyList<object?[]>>();
		foreach(var table in new[]{"StockMovements","StockMovementTracking","ItemTrackingUnits","ServicePartEvidence","ServicePartOperations","ServiceOrders","ServiceCases","ServiceCaseHistory","AuditEntries"})
			result[table]=await data.QueryAsync($"SELECT * FROM {table} ORDER BY 1;",reader=>Enumerable.Range(0,reader.FieldCount).Select(index=>reader.IsDBNull(index)?null:reader.GetValue(index)).ToArray(),CancellationToken.None);
		return JsonSerializer.Serialize(result);
	}

	private sealed class ControlledRunner(IDatabaseTransactionRunner inner):IDatabaseTransactionRunner
	{
		private CommitPause? _pause;
		public CancellationTokenSource? CancelBeforeCommit{get;set;}
		public bool LoseNextResponse{get;set;}
		public async Task<T> ExecuteAsync<T>(Func<DatabaseTransactionContext,CancellationToken,Task<T>> operation,CancellationToken cancellationToken=default)
		{
			var pause=_pause;
			var first=pause is not null&&Interlocked.CompareExchange(ref pause.Started,1,0)==0;
			if(pause is not null&&!first)pause.ContenderStarted.TrySetResult();
			var result=await inner.ExecuteAsync(async(tx,token)=>
			{
				var value=await operation(tx,token);
				if(CancelBeforeCommit is{} cancellation){CancelBeforeCommit=null;cancellation.Cancel();token.ThrowIfCancellationRequested();}
				if(first&&pause is not null){pause.BeforeCommit.TrySetResult();await pause.Resume.Task.WaitAsync(token);}
				return value;
			},cancellationToken);
			if(LoseNextResponse){LoseNextResponse=false;throw new IOException("The successful response was lost after commit.");}
			return result;
		}

		public async Task<Exception?> CompeteAsync(Func<Task> first,Func<Task> second)
		{
			var pause=new CommitPause();_pause=pause;
			var firstTask=Task.Run(()=>Record.ExceptionAsync(first));
			Task<Exception?>? secondTask=null;
			Exception? firstFailure=null,secondFailure=null;
			try
			{
				await pause.BeforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(30));
				secondTask=Task.Run(()=>Record.ExceptionAsync(second));
				await pause.ContenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
			}
			finally
			{
				pause.Resume.TrySetResult();
				try{firstFailure=await firstTask;}
				finally{try{if(secondTask is not null)secondFailure=await secondTask;}finally{_pause=null;}}
			}
			Assert.Null(firstFailure);
			if(secondTask is null)throw new InvalidOperationException("The competing transaction did not start.");
			return secondFailure;
		}
	}

	private sealed class CommitPause
	{
		public int Started;
		public TaskCompletionSource BeforeCommit{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource ContenderStarted{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Resume{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
