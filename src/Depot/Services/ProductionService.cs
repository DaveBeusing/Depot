// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ProductionService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ProductionRepository _production;
	private readonly ItemRepository _items;
	private readonly WarehouseRepository _warehouses;
	private readonly MovementService _movements;
	private readonly StockMovementRepository _stockMovements;
	private readonly StockMovementReversalService _movementReversals;
	private readonly ItemCostCalculationService _itemCosts;
	private readonly ReplenishmentService _replenishment;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public ProductionService(
		IDatabaseTransactionRunner transactions,
		ProductionRepository production,
		ItemRepository items,
		WarehouseRepository warehouses,
		MovementService movements,
		StockMovementRepository stockMovements,
		StockMovementReversalService movementReversals,
		ItemCostCalculationService itemCosts,
		ReplenishmentService replenishment,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions=transactions;_production=production;_items=items;_warehouses=warehouses;_movements=movements;_stockMovements=stockMovements;
		_movementReversals=movementReversals;_itemCosts=itemCosts;_replenishment=replenishment;_auditEntries=auditEntries;_audit=audit;_authorization=authorization;
	}

	public bool CanView=>_authorization.HasPermission(ApplicationPermission.ProductionView);
	public bool CanManageBoms=>_authorization.HasPermission(ApplicationPermission.BillsOfMaterialManage);
	public bool CanManageOrders=>_authorization.HasPermission(ApplicationPermission.ProductionOrdersManage);
	public bool CanIssue=>_authorization.HasPermission(ApplicationPermission.ProductionOrdersIssue);
	public bool CanComplete=>_authorization.HasPermission(ApplicationPermission.ProductionOrdersComplete);
	public bool CanReverse=>_authorization.HasPermission(ApplicationPermission.ProductionOrdersReverse);

	public async Task<IReadOnlyList<Item>> ListStockItemsAsync(int count=500,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		if(count is <1 or >2000) throw new ArgumentOutOfRangeException(nameof(count));
		var values=await _items.GetActiveItemsAsync(token);
		return values.Where(value=>value.IsActive && value.ItemType==ItemType.StockItem).OrderBy(value=>value.PartNumber,StringComparer.CurrentCultureIgnoreCase).Take(count).ToArray();
	}

	public Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(int count=200,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		if(count is <1 or >1000) throw new ArgumentOutOfRangeException(nameof(count));
		return _warehouses.ListActiveOptionsAsync(count,token);
	}

	public Task<IReadOnlyList<InventoryLookupItem>> SearchInventoriesAsync(string? searchText=null,int count=500,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		if(count is <1 or >2000) throw new ArgumentOutOfRangeException(nameof(count));
		return _movements.SearchAvailableInventoriesAsync(searchText,count,token);
	}

	public Task<IReadOnlyList<ProductionAssemblyCostEvidence>> GetCostEvidenceAsync(long orderId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		return _production.ListCostEvidenceAsync(orderId,token);
	}

	public bool CanHandoffShortages=>CanManageOrders && _authorization.HasPermission(ApplicationPermission.ReplenishmentSuggestionsManage);

	public Task<PageResult<BillOfMaterial>> SearchBomsAsync(string? searchText=null,int pageNumber=1,int pageSize=100,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		return _production.SearchBomsAsync(searchText,pageNumber,pageSize,token);
	}

	public Task<BillOfMaterial?> GetBomAsync(long id,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		return _production.GetBomAsync(id,token);
	}

	public async Task<BillOfMaterial> SaveBomAsync(BillOfMaterial value,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.BillsOfMaterialManage);
		ArgumentNullException.ThrowIfNull(value);
		NormalizeBom(value);
		var finished=await RequireStockItemAsync(value.FinishedItemId,"finished item",token);
		var normalizedLines=new List<BillOfMaterialLine>(value.Lines.Count);
		foreach(var line in value.Lines)
		{
			if(line.ComponentItemId==finished.Id) throw new InvalidOperationException("A bill of material cannot contain its finished item as a component.");
			var component=await RequireStockItemAsync(line.ComponentItemId,"component item",token);
			normalizedLines.Add(new BillOfMaterialLine{Id=line.Id,BillOfMaterialId=value.Id,ComponentItemId=component.Id,ComponentPartNumber=component.PartNumber,ComponentDescription=component.Description,UnitOfMeasure=component.UnitOfMeasure,Quantity=line.Quantity,Sequence=line.Sequence});
		}
		if(normalizedLines.Select(x=>x.ComponentItemId).Distinct().Count()!=normalizedLines.Count) throw new InvalidOperationException("A component item may appear only once in a BOM revision.");
		value.Lines=normalizedLines;
		var edges=await _production.ListBomEdgesAsync(value.Id==0?null:value.Id,token);
		if(CreatesCycle(value.FinishedItemId,normalizedLines.Select(x=>x.ComponentItemId),edges)) throw new InvalidOperationException("The bill of material would create a component cycle.");
		var before=value.Id==0?null:await _production.GetBomAsync(value.Id,token);
		if(before is not null && before.Status!=BillOfMaterialStatus.Draft) throw new InvalidOperationException("Released BOM revisions are immutable. Create a new revision instead.");
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var saved=await _production.SaveDraftBomAsync(transaction,value,user.Id,ct);
			await _auditEntries.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),ct);
			return saved;
		},token);
	}

	public async Task<BillOfMaterial> ActivateBomAsync(long id,long version,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.BillsOfMaterialManage);
		var before=await _production.GetBomAsync(id,token)??throw new InvalidOperationException("Bill of material was not found.");
		if(before.Lines.Count==0) throw new InvalidOperationException("A bill of material must contain at least one component before activation.");
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var saved=await _production.ActivateBomAsync(transaction,id,version,ct);
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(id,"Activated",before,saved),ct);
			return saved;
		},token);
	}

	public Task<PageResult<ProductionOrder>> SearchOrdersAsync(ProductionOrderStatus? status=null,int pageNumber=1,int pageSize=100,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		return _production.SearchOrdersAsync(status,pageNumber,pageSize,token);
	}

	public Task<IReadOnlyList<ProductionOrder>> GetOwnedOpenOrdersAsync(long userId,int count,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		if(count is <1 or >100) throw new ArgumentOutOfRangeException(nameof(count));
		return _production.GetOwnedOpenOrdersAsync(userId,count,token);
	}

	public Task<ProductionOrder?> GetOrderAsync(long id,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		return _production.GetOrderAsync(id,token);
	}

	public async Task<ProductionOrder> CreateOrderAsync(long finishedItemId,long billOfMaterialId,int plannedQuantity,long warehouseId,long finishedInventoryId,long? ownerUserId=null,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionOrdersManage);
		if(plannedQuantity<=0) throw new ArgumentOutOfRangeException(nameof(plannedQuantity));
		var finished=await RequireStockItemAsync(finishedItemId,"finished item",token);
		var warehouse=await _warehouses.GetByIdAsync(warehouseId,token)??throw new InvalidOperationException("Production warehouse was not found.");
		if(!warehouse.IsActive) throw new InvalidOperationException("Production warehouse is inactive.");
		var bom=await _production.GetBomAsync(billOfMaterialId,token)??throw new InvalidOperationException("Bill of material was not found.");
		if(bom.FinishedItemId!=finished.Id) throw new InvalidOperationException("The selected BOM belongs to another finished item.");
		EnsureBomEffective(bom,DateTime.UtcNow);
		var user=CurrentUser();
		var candidate=new ProductionOrder{FinishedItemId=finished.Id,BillOfMaterialId=bom.Id,BillOfMaterialRevision=bom.Revision,PlannedQuantity=plannedQuantity,WarehouseId=warehouseId,FinishedInventoryId=finishedInventoryId,OwnerUserId=ownerUserId,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id};
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var inventory=await _production.GetInventoryContextAsync(transaction,finishedInventoryId,ct)??throw new InvalidOperationException("Finished-goods inventory was not found.");
			EnsureInventoryContext(inventory,finished.Id,warehouseId,"finished-goods");
			var saved=await _production.CreateOrderAsync(transaction,candidate,ct);
			await _auditEntries.CreateAsync(transaction,_audit.CreateCreatedEntry(saved.Id,saved),ct);
			return saved;
		},token);
	}

	public async Task<ProductionOrder> ReleaseOrderAsync(long orderId,long version,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionOrdersManage);
		var before=await _production.GetOrderAsync(orderId,token)??throw new InvalidOperationException("Production order was not found.");
		if(before.Version!=version) throw new ConcurrencyConflictException("production order");
		if(before.Status!=ProductionOrderStatus.Draft) throw new InvalidOperationException("Only draft production orders can be released.");
		var bom=await _production.GetBomAsync(before.BillOfMaterialId,token)??throw new InvalidOperationException("The production order BOM no longer exists.");
		if(bom.Revision!=before.BillOfMaterialRevision) throw new InvalidOperationException("The production order BOM revision reference is inconsistent.");
		EnsureBomEffective(bom,DateTime.UtcNow);
		var requirements=new List<ProductionOrderRequirement>(bom.Lines.Count);
		foreach(var line in bom.Lines)
		{
			var required=line.Quantity*before.PlannedQuantity;
			if(required<=0m || required!=decimal.Truncate(required) || required>int.MaxValue)
				throw new InvalidOperationException($"BOM component '{line.ComponentPartNumber}' does not resolve to a supported positive whole base-unit quantity for this order.");
			requirements.Add(new ProductionOrderRequirement{ProductionOrderId=before.Id,SourceBillOfMaterialLineId=line.Id,ComponentItemId=line.ComponentItemId,ComponentPartNumber=line.ComponentPartNumber,ComponentDescription=line.ComponentDescription,UnitOfMeasure=line.UnitOfMeasure,RequiredQuantity=(int)required,Sequence=line.Sequence});
		}
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var saved=await _production.ReleaseOrderAsync(transaction,before,requirements,user.Id,DateTime.UtcNow,ct);
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(saved.Id,"Released",before,saved),ct);
			return saved;
		},token);
	}

	public Task<IReadOnlyList<ProductionRequirementAvailability>> GetAvailabilityAsync(long orderId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionView);
		return _production.GetAvailabilityAsync(orderId,token);
	}

	public async Task<ProductionShortageHandoffResult> HandoffShortagesToReplenishmentAsync(long orderId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionOrdersManage);
		var order=await _production.GetOrderAsync(orderId,token)??throw new InvalidOperationException("Production order was not found.");
		if(order.Status is ProductionOrderStatus.Draft or ProductionOrderStatus.Cancelled or ProductionOrderStatus.Reversed) throw new InvalidOperationException("Only released or active production demand can be handed to replenishment.");
		var shortages=(await _production.GetAvailabilityAsync(orderId,token)).Where(x=>x.ShortageQuantity>0).ToArray();
		var suggestions=new List<long>();
		foreach(var shortage in shortages)
		{
			var suggestion=await _replenishment.RecalculateItemWarehouseAsync(shortage.ComponentItemId,order.WarehouseId,token);
			if(suggestion is not null) suggestions.Add(suggestion.Id);
		}
		return new ProductionShortageHandoffResult(order.Id,shortages.Length,suggestions.Distinct().Order().ToArray());
	}

	public async Task<ProductionOrder> IssueComponentAsync(long orderId,long requirementId,long inventoryId,int quantity,IReadOnlyList<TrackingAllocationInput>? trackingAllocations=null,Guid? operationId=null,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionOrdersIssue);
		_authorization.RequirePermission(ApplicationPermission.StockMovementsPost);
		if(quantity<=0) throw new ArgumentOutOfRangeException(nameof(quantity));
		var key=$"issue:{(operationId??Guid.NewGuid()):N}";
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var existing=await _production.GetMovementByOperationKeyAsync(transaction,key,ct);
			if(existing is not null)
			{
				if(existing.ProductionOrderId!=orderId || existing.RequirementId!=requirementId || existing.Kind!=ProductionMovementKind.ComponentIssue) throw new InvalidOperationException("The operation key is already used by another production movement.");
				return await _production.GetOrderAsync(transaction,orderId,ct)??throw new InvalidOperationException("Production order was not found.");
			}
			var before=await _production.GetOrderAsync(transaction,orderId,ct)??throw new InvalidOperationException("Production order was not found.");
			if(before.Status is not ProductionOrderStatus.Released and not ProductionOrderStatus.InProgress) throw new InvalidOperationException("Components can be issued only to a released or in-progress production order.");
			var requirement=before.Requirements.SingleOrDefault(x=>x.Id==requirementId)??throw new InvalidOperationException("Production requirement was not found.");
			if(quantity>requirement.RemainingQuantity) throw new InvalidOperationException("Component issue exceeds the remaining requirement quantity.");
			var inventory=await _production.GetInventoryContextAsync(transaction,inventoryId,ct)??throw new InvalidOperationException("Component inventory was not found.");
			EnsureInventoryContext(inventory,requirement.ComponentItemId,before.WarehouseId,"component");
			var movement=await _movements.AddWithdrawalInTransactionAsync(transaction,inventoryId,quantity,null,$"Assembly {before.OrderNumber} issue {key}",$"Component issue for {requirement.ComponentPartNumber}",trackingAllocations??[],ct);
			await _production.AddMovementEvidenceAsync(transaction,new ProductionMaterialMovement{ProductionOrderId=before.Id,RequirementId=requirement.Id,StockMovementId=movement.Id,Kind=ProductionMovementKind.ComponentIssue,Quantity=quantity,OperationKey=key,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id},ct);
			await _production.AdjustIssuedQuantityAsync(transaction,requirement.Id,quantity,ct);
			await _production.MarkInProgressAsync(transaction,before.Id,ct);
			var after=await _production.GetOrderAsync(transaction,before.Id,ct)??throw new InvalidOperationException("Production order could not be reloaded.");
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(before.Id,"ComponentIssued",before,after),ct);
			return after;
		},token);
	}

	public async Task<ProductionCompletionResult> CompleteAsync(long orderId,IReadOnlyList<TrackingAllocationInput>? finishedTrackingAllocations=null,Guid? operationId=null,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionOrdersComplete);
		_authorization.RequirePermission(ApplicationPermission.StockMovementsPost);
		var key=$"complete:{(operationId??Guid.NewGuid()):N}";
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var existing=await _production.GetMovementByOperationKeyAsync(transaction,key,ct);
			if(existing is not null)
			{
				if(existing.ProductionOrderId!=orderId || existing.Kind!=ProductionMovementKind.FinishedGoodsReceipt) throw new InvalidOperationException("The operation key is already used by another production movement.");
				var idempotentOrder=await _production.GetOrderAsync(transaction,orderId,ct)??throw new InvalidOperationException("Production order was not found.");
				var idempotentEvidence=await _production.ListCostEvidenceAsync(transaction,orderId,ct);
				return BuildCompletion(idempotentOrder,idempotentEvidence);
			}
			var before=await _production.GetOrderAsync(transaction,orderId,ct)??throw new InvalidOperationException("Production order was not found.");
			if(before.Status is not ProductionOrderStatus.Released and not ProductionOrderStatus.InProgress) throw new InvalidOperationException("Only released or in-progress production orders can be completed.");
			if(before.Requirements.Count==0 || before.Requirements.Any(x=>x.IssuedQuantity!=x.RequiredQuantity)) throw new InvalidOperationException("All component requirements must be issued exactly before completion.");
			var evidence=new List<ProductionAssemblyCostEvidence>(before.Requirements.Count);
			string? currency=null;
			var at=DateTime.UtcNow;
			foreach(var requirement in before.Requirements)
			{
				var result=await _itemCosts.CalculateAsync(transaction,requirement.ComponentItemId,at,null,ct);
				if(!result.IsSuccess) throw new InvalidOperationException($"Cost evidence for component '{requirement.ComponentPartNumber}' is unavailable: {result.Error}");
				currency??=result.Currency;
				if(!string.Equals(currency,result.Currency,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Component cost currencies differ. Resolve item costing currencies before assembly completion.");
				var extended=result.CalculatedCost*requirement.IssuedQuantity;
				evidence.Add(new ProductionAssemblyCostEvidence{ProductionOrderId=before.Id,RequirementId=requirement.Id,ComponentItemId=requirement.ComponentItemId,Quantity=requirement.IssuedQuantity,UnitCost=result.CalculatedCost,ExtendedCost=extended,Currency=result.Currency,EvidenceVersion=result.EvidenceVersion,CalculatedAtUtc=at});
			}
			var inventory=await _production.GetInventoryContextAsync(transaction,before.FinishedInventoryId,ct)??throw new InvalidOperationException("Finished-goods inventory was not found.");
			EnsureInventoryContext(inventory,before.FinishedItemId,before.WarehouseId,"finished-goods");
			var movement=await _movements.AddCorrectionInTransactionAsync(transaction,before.FinishedInventoryId,before.PlannedQuantity,null,$"Assembly {before.OrderNumber} completion {key}","Finished-goods assembly receipt",finishedTrackingAllocations??[],ct);
			await _production.AddMovementEvidenceAsync(transaction,new ProductionMaterialMovement{ProductionOrderId=before.Id,StockMovementId=movement.Id,Kind=ProductionMovementKind.FinishedGoodsReceipt,Quantity=before.PlannedQuantity,OperationKey=key,CreatedAtUtc=at,CreatedByUserId=user.Id},ct);
			await _production.SaveCostEvidenceAsync(transaction,evidence,ct);
			await _production.CompleteAsync(transaction,before.Id,before.Version,before.PlannedQuantity,user.Id,at,ct);
			var after=await _production.GetOrderAsync(transaction,before.Id,ct)??throw new InvalidOperationException("Production order could not be reloaded.");
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(before.Id,"Completed",before,after),ct);
			return BuildCompletion(after,evidence);
		},token);
	}

	public async Task<ProductionOrder> ReverseAsync(long orderId,long reasonCodeId,string reversalReason,Guid? operationId=null,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ProductionOrdersReverse);
		_authorization.RequirePermission(ApplicationPermission.StockMovementsReverse);
		var operation=(operationId??Guid.NewGuid()).ToString("N");
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,ct)=>
		{
			var before=await _production.GetOrderAsync(transaction,orderId,ct)??throw new InvalidOperationException("Production order was not found.");
			var productionMovements=(await _production.ListMovementEvidenceAsync(transaction,orderId,ct))
				.Where(x=>x.Kind is ProductionMovementKind.ComponentIssue or ProductionMovementKind.FinishedGoodsReceipt).ToArray();
			if(productionMovements.Length==0) throw new InvalidOperationException("Production order has no material movements to reverse.");
			var firstKey=$"reverse:{operation}:{productionMovements[0].StockMovementId}";
			var existing=await _production.GetMovementByOperationKeyAsync(transaction,firstKey,ct);
			if(existing is not null) return before;
			if(before.Status!=ProductionOrderStatus.Completed) throw new InvalidOperationException("Only completed production orders can be reversed as a complete assembly transaction.");
			var originals=new List<StockMovement>(productionMovements.Length);
			foreach(var evidence in productionMovements)
				originals.Add(await _stockMovements.GetByIdAsync(transaction,evidence.StockMovementId,ct)??throw new InvalidOperationException("A production stock movement was not found."));
			var reversals=await _movementReversals.CreateReversalsAsync(transaction,originals,reasonCodeId,reversalReason,user.Id,ct);
			var byOriginal=productionMovements.ToDictionary(x=>x.StockMovementId);
			foreach(var reversal in reversals)
			{
				var originalId=reversal.ReversalOfMovementId??throw new InvalidOperationException("Production reversal did not retain original movement identity.");
				var source=byOriginal[originalId];
				var kind=source.Kind==ProductionMovementKind.ComponentIssue?ProductionMovementKind.ComponentIssueReversal:ProductionMovementKind.FinishedGoodsReceiptReversal;
				await _production.AddMovementEvidenceAsync(transaction,new ProductionMaterialMovement{ProductionOrderId=before.Id,RequirementId=source.RequirementId,StockMovementId=reversal.Id,Kind=kind,Quantity=Math.Abs(reversal.Quantity),OperationKey=$"reverse:{operation}:{originalId}",CreatedAtUtc=reversal.TimestampUtc,CreatedByUserId=user.Id},ct);
				if(source.Kind==ProductionMovementKind.ComponentIssue && source.RequirementId is long requirementId) await _production.AdjustIssuedQuantityAsync(transaction,requirementId,-source.Quantity,ct);
			}
			await _production.MarkReversedAsync(transaction,before.Id,before.Version,user.Id,DateTime.UtcNow,ct);
			var after=await _production.GetOrderAsync(transaction,before.Id,ct)??throw new InvalidOperationException("Production order could not be reloaded.");
			await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(before.Id,"Reversed",before,after),ct);
			return after;
		},token);
	}

	private static ProductionCompletionResult BuildCompletion(ProductionOrder order,IReadOnlyList<ProductionAssemblyCostEvidence> evidence)
	{
		var total=evidence.Sum(x=>x.ExtendedCost);
		var currency=evidence.Select(x=>x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).SingleOrDefault()??string.Empty;
		var unit=order.CompletedQuantity>0?total/order.CompletedQuantity:0m;
		return new ProductionCompletionResult(order,new ProductionCompletionCost(total,unit,currency,evidence));
	}

	private async Task<Item> RequireStockItemAsync(long itemId,string role,CancellationToken token)
	{
		var item=await _items.GetByIdAsync(itemId,token)??throw new InvalidOperationException($"Production {role} was not found.");
		if(!item.IsActive) throw new InvalidOperationException($"Production {role} is inactive.");
		if(item.ItemType!=ItemType.StockItem) throw new InvalidOperationException($"Production {role} must be a physical stock item.");
		return item;
	}

	private static void NormalizeBom(BillOfMaterial value)
	{
		if(value.FinishedItemId<=0) throw new ArgumentOutOfRangeException(nameof(value.FinishedItemId));
		value.Revision=value.Revision?.Trim()??string.Empty;
		if(value.Revision.Length is 0 or >80) throw new ArgumentException("BOM revision is required and limited to 80 characters.");
		if(value.EffectiveFromUtc is not null && value.EffectiveUntilUtc is not null && value.EffectiveUntilUtc.Value.ToUniversalTime()<value.EffectiveFromUtc.Value.ToUniversalTime()) throw new ArgumentException("BOM effective-until cannot be before effective-from.");
		if(value.Lines.Count==0) throw new InvalidOperationException("A bill of material requires at least one component line.");
		foreach(var line in value.Lines)
		{
			if(line.ComponentItemId<=0) throw new InvalidOperationException("A BOM component item is required.");
			if(line.Quantity<=0m) throw new InvalidOperationException("BOM component quantities must be positive.");
			if(line.Sequence<0) throw new InvalidOperationException("BOM sequence cannot be negative.");
		}
	}

	private static bool CreatesCycle(long finishedItemId,IEnumerable<long> proposedComponents,IReadOnlyList<(long FinishedItemId,long ComponentItemId)> existing)
	{
		var graph=existing.GroupBy(x=>x.FinishedItemId).ToDictionary(g=>g.Key,g=>g.Select(x=>x.ComponentItemId).ToHashSet());
		graph[finishedItemId]=proposedComponents.ToHashSet();
		foreach(var component in graph[finishedItemId])
		{
			var stack=new Stack<long>();stack.Push(component);
			var visited=new HashSet<long>();
			while(stack.Count>0)
			{
				var current=stack.Pop();
				if(current==finishedItemId) return true;
				if(!visited.Add(current) || !graph.TryGetValue(current,out var next)) continue;
				foreach(var child in next) stack.Push(child);
			}
		}
		return false;
	}

	private static void EnsureBomEffective(BillOfMaterial bom,DateTime atUtc)
	{
		if(bom.Status!=BillOfMaterialStatus.Active) throw new InvalidOperationException("Production orders require an active BOM revision.");
		var at=atUtc.ToUniversalTime();
		if(bom.EffectiveFromUtc is { } from && from.ToUniversalTime()>at) throw new InvalidOperationException("The BOM revision is not effective yet.");
		if(bom.EffectiveUntilUtc is { } until && until.ToUniversalTime()<at) throw new InvalidOperationException("The BOM revision is no longer effective.");
	}

	private static void EnsureInventoryContext(ProductionInventoryContext inventory,long itemId,long warehouseId,string role)
	{
		if(inventory.ItemId!=itemId) throw new InvalidOperationException($"The selected {role} inventory belongs to another item.");
		if(inventory.WarehouseId!=warehouseId) throw new InvalidOperationException($"The selected {role} inventory belongs to another warehouse.");
		if(!inventory.InventoryActive || !inventory.LocationActive || !inventory.WarehouseActive) throw new InvalidOperationException($"The selected {role} inventory context is inactive.");
	}

	private User CurrentUser()=>_authorization.CurrentUser is {IsActive:true} user?user:throw new UnauthorizedAccessException("An active signed-in user is required.");
}
