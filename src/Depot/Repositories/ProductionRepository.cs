// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ProductionRepository : DatabaseRepository
{
	private const string BomColumns="b.Id,b.FinishedItemId,i.PartNumber,i.Description,b.Revision,b.Status,b.EffectiveFromUtc,b.EffectiveUntilUtc,b.Version,b.CreatedAtUtc,b.CreatedByUserId";
	private const string BomFrom="FROM ProductionBillsOfMaterial b INNER JOIN Items i ON i.Id=b.FinishedItemId";
	private const string OrderColumns="o.Id,o.OrderNumber,o.FinishedItemId,i.PartNumber,i.Description,o.BillOfMaterialId,o.BillOfMaterialRevision,o.PlannedQuantity,o.CompletedQuantity,o.WarehouseId,w.Name,o.FinishedInventoryId,o.Status,o.OwnerUserId,o.CreatedAtUtc,o.CreatedByUserId,o.ReleasedAtUtc,o.ReleasedByUserId,o.CompletedAtUtc,o.CompletedByUserId,o.ReversedAtUtc,o.ReversedByUserId,o.Version";
	private const string OrderFrom="FROM ProductionOrders o INNER JOIN Items i ON i.Id=o.FinishedItemId INNER JOIN Warehouses w ON w.Id=o.WarehouseId";

	public ProductionRepository(DatabaseAccess database):base(database){}

	public Task<PageResult<BillOfMaterial>> SearchBomsAsync(string? searchText,int pageNumber,int pageSize,CancellationToken token)
	{
		var search=searchText?.Trim();
		var where=string.IsNullOrWhiteSpace(search)?string.Empty:"WHERE i.PartNumber LIKE $Search OR i.Description LIKE $Search OR b.Revision LIKE $Search";
		var parameters=string.IsNullOrWhiteSpace(search)?[]:new[]{Parameter("$Search",$"%{search}%")};
		return Database.QueryPageAsync($"SELECT {BomColumns} {BomFrom} {where} ORDER BY i.PartNumber,b.Id DESC",$"SELECT COUNT(*) {BomFrom} {where}",ReadBom,pageNumber,pageSize,token,parameters);
	}

	public async Task<BillOfMaterial?> GetBomAsync(long id,CancellationToken token)
	{
		var bom=await Database.QuerySingleOrDefaultAsync($"SELECT {BomColumns} {BomFrom} WHERE b.Id=$Id;",ReadBom,token,Parameter("$Id",id));
		if(bom is not null) bom.Lines=await ListBomLinesAsync(id,token);
		return bom;
	}

	internal async Task<BillOfMaterial?> GetBomAsync(DatabaseTransactionContext transaction,long id,CancellationToken token)
	{
		var bom=await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {BomColumns} {BomFrom} WHERE b.Id=$Id;",ReadBom,token,Parameter("$Id",id));
		if(bom is not null) bom.Lines=await ListBomLinesAsync(transaction,id,token);
		return bom;
	}

	public async Task<BillOfMaterial?> GetEffectiveBomAsync(long finishedItemId,DateTime atUtc,CancellationToken token)
	{
		var value=Utc(atUtc);
		var bom=await Database.QuerySingleOrDefaultAsync(
			$"SELECT {BomColumns} {BomFrom} WHERE b.FinishedItemId=$Item AND b.Status=$Active AND (b.EffectiveFromUtc IS NULL OR b.EffectiveFromUtc<=$At) AND (b.EffectiveUntilUtc IS NULL OR b.EffectiveUntilUtc>=$At) ORDER BY b.Id DESC;",
			ReadBom,token,Parameter("$Item",finishedItemId),Parameter("$Active",(int)BillOfMaterialStatus.Active),Parameter("$At",value));
		if(bom is not null) bom.Lines=await ListBomLinesAsync(bom.Id,token);
		return bom;
	}

	public Task<IReadOnlyList<(long FinishedItemId,long ComponentItemId)>> ListBomEdgesAsync(long? excludeBomId,CancellationToken token)
	{
		var filter=excludeBomId is null?string.Empty:"AND b.Id<>$Exclude";
		var parameters=excludeBomId is null?[]:new[]{Parameter("$Exclude",excludeBomId.Value)};
		return Database.QuerySliceAsync(
			$"SELECT b.FinishedItemId,l.ComponentItemId FROM ProductionBillsOfMaterial b INNER JOIN ProductionBillOfMaterialLines l ON l.BillOfMaterialId=b.Id WHERE b.Status IN ($Draft,$Active) {filter} ORDER BY b.FinishedItemId,l.ComponentItemId",
			reader=>(reader.GetInt64(0),reader.GetInt64(1)),0,10000,token,[Parameter("$Draft",(int)BillOfMaterialStatus.Draft),Parameter("$Active",(int)BillOfMaterialStatus.Active),..parameters]);
	}

	internal async Task<BillOfMaterial> SaveDraftBomAsync(DatabaseTransactionContext transaction,BillOfMaterial value,long userId,CancellationToken token)
	{
		if(value.Id==0)
		{
			value.Id=await transaction.Session.InsertAsync(
				"INSERT INTO ProductionBillsOfMaterial(FinishedItemId,Revision,Status,EffectiveFromUtc,EffectiveUntilUtc,CreatedAtUtc,CreatedByUserId) VALUES($Item,$Revision,$Status,$From,$Until,$Created,$User);",
				token,Parameter("$Item",value.FinishedItemId),Parameter("$Revision",value.Revision),Parameter("$Status",(int)BillOfMaterialStatus.Draft),
				Parameter("$From",NullableUtc(value.EffectiveFromUtc)),Parameter("$Until",NullableUtc(value.EffectiveUntilUtc)),Parameter("$Created",Utc(DateTime.UtcNow)),Parameter("$User",userId));
		}
		else
		{
			var updated=await transaction.Session.ExecuteAsync(
				"UPDATE ProductionBillsOfMaterial SET FinishedItemId=$Item,Revision=$Revision,EffectiveFromUtc=$From,EffectiveUntilUtc=$Until,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;",
				token,Parameter("$Item",value.FinishedItemId),Parameter("$Revision",value.Revision),Parameter("$From",NullableUtc(value.EffectiveFromUtc)),Parameter("$Until",NullableUtc(value.EffectiveUntilUtc)),
				Parameter("$Id",value.Id),Parameter("$Version",value.Version),Parameter("$Draft",(int)BillOfMaterialStatus.Draft));
			if(updated!=1) throw new ConcurrencyConflictException("bill of material");
			await transaction.Session.ExecuteAsync("DELETE FROM ProductionBillOfMaterialLines WHERE BillOfMaterialId=$Id;",token,Parameter("$Id",value.Id));
		}
		foreach(var line in value.Lines.OrderBy(x=>x.Sequence).ThenBy(x=>x.ComponentItemId))
			await transaction.Session.InsertAsync(
				"INSERT INTO ProductionBillOfMaterialLines(BillOfMaterialId,ComponentItemId,Quantity,Sequence) VALUES($Bom,$Item,$Quantity,$Sequence);",
				token,Parameter("$Bom",value.Id),Parameter("$Item",line.ComponentItemId),Parameter("$Quantity",line.Quantity),Parameter("$Sequence",line.Sequence));
		return await GetBomAsync(transaction,value.Id,token)??throw new InvalidOperationException("Bill of material could not be reloaded.");
	}

	internal async Task<BillOfMaterial> ActivateBomAsync(DatabaseTransactionContext transaction,long id,long version,CancellationToken token)
	{
		var bom=await GetBomAsync(transaction,id,token)??throw new InvalidOperationException("Bill of material was not found.");
		if(bom.Version!=version) throw new ConcurrencyConflictException("bill of material");
		await transaction.Session.ExecuteAsync(
			"UPDATE ProductionBillsOfMaterial SET Status=$Retired,Version=Version+1 WHERE FinishedItemId=$Item AND Id<>$Id AND Status=$Active;",
			token,Parameter("$Retired",(int)BillOfMaterialStatus.Retired),Parameter("$Item",bom.FinishedItemId),Parameter("$Id",id),Parameter("$Active",(int)BillOfMaterialStatus.Active));
		var updated=await transaction.Session.ExecuteAsync(
			"UPDATE ProductionBillsOfMaterial SET Status=$Active,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status IN ($Draft,$Active);",
			token,Parameter("$Active",(int)BillOfMaterialStatus.Active),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Draft",(int)BillOfMaterialStatus.Draft));
		if(updated!=1) throw new ConcurrencyConflictException("bill of material");
		return await GetBomAsync(transaction,id,token)??throw new InvalidOperationException("Bill of material could not be reloaded.");
	}

	public Task<PageResult<ProductionOrder>> SearchOrdersAsync(ProductionOrderStatus? status,int pageNumber,int pageSize,CancellationToken token)
	{
		var where=status is null?string.Empty:"WHERE o.Status=$Status";
		var parameters=status is null?[]:new[]{Parameter("$Status",(int)status.Value)};
		return Database.QueryPageAsync($"SELECT {OrderColumns} {OrderFrom} {where} ORDER BY o.Id DESC",$"SELECT COUNT(*) {OrderFrom} {where}",ReadOrder,pageNumber,pageSize,token,parameters);
	}

	public async Task<ProductionOrder?> GetOrderAsync(long id,CancellationToken token)
	{
		var order=await Database.QuerySingleOrDefaultAsync($"SELECT {OrderColumns} {OrderFrom} WHERE o.Id=$Id;",ReadOrder,token,Parameter("$Id",id));
		if(order is not null) order.Requirements=await ListRequirementsAsync(id,token);
		return order;
	}

	internal async Task<ProductionOrder?> GetOrderAsync(DatabaseTransactionContext transaction,long id,CancellationToken token)
	{
		var order=await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {OrderColumns} {OrderFrom} WHERE o.Id=$Id;",ReadOrder,token,Parameter("$Id",id));
		if(order is not null) order.Requirements=await ListRequirementsAsync(transaction,id,token);
		return order;
	}

	internal async Task<ProductionOrder> CreateOrderAsync(DatabaseTransactionContext transaction,ProductionOrder value,CancellationToken token)
	{
		var temporary=$"PENDING-{Guid.NewGuid():N}";
		var id=await transaction.Session.InsertAsync(
			"INSERT INTO ProductionOrders(OrderNumber,FinishedItemId,BillOfMaterialId,BillOfMaterialRevision,PlannedQuantity,WarehouseId,FinishedInventoryId,Status,OwnerUserId,CreatedAtUtc,CreatedByUserId) VALUES($Number,$Item,$Bom,$Revision,$Quantity,$Warehouse,$Inventory,$Status,$Owner,$Created,$User);",
			token,Parameter("$Number",temporary),Parameter("$Item",value.FinishedItemId),Parameter("$Bom",value.BillOfMaterialId),Parameter("$Revision",value.BillOfMaterialRevision),
			Parameter("$Quantity",value.PlannedQuantity),Parameter("$Warehouse",value.WarehouseId),Parameter("$Inventory",value.FinishedInventoryId),Parameter("$Status",(int)ProductionOrderStatus.Draft),
			Parameter("$Owner",value.OwnerUserId),Parameter("$Created",Utc(value.CreatedAtUtc)),Parameter("$User",value.CreatedByUserId));
		var number=$"AO-{id:D8}";
		await transaction.Session.ExecuteAsync("UPDATE ProductionOrders SET OrderNumber=$Number WHERE Id=$Id;",token,Parameter("$Number",number),Parameter("$Id",id));
		return await GetOrderAsync(transaction,id,token)??throw new InvalidOperationException("Production order could not be reloaded.");
	}

	internal async Task<ProductionOrder> ReleaseOrderAsync(DatabaseTransactionContext transaction,ProductionOrder order,IReadOnlyList<ProductionOrderRequirement> requirements,long userId,DateTime releasedAtUtc,CancellationToken token)
	{
		foreach(var requirement in requirements.OrderBy(x=>x.Sequence).ThenBy(x=>x.SourceBillOfMaterialLineId))
			await transaction.Session.InsertAsync(
				"INSERT INTO ProductionOrderRequirements(ProductionOrderId,SourceBillOfMaterialLineId,ComponentItemId,ComponentPartNumber,ComponentDescription,UnitOfMeasure,RequiredQuantity,IssuedQuantity,Sequence) VALUES($Order,$Line,$Item,$Part,$Description,$Uom,$Required,0,$Sequence);",
				token,Parameter("$Order",order.Id),Parameter("$Line",requirement.SourceBillOfMaterialLineId),Parameter("$Item",requirement.ComponentItemId),Parameter("$Part",requirement.ComponentPartNumber),
				Parameter("$Description",requirement.ComponentDescription),Parameter("$Uom",requirement.UnitOfMeasure),Parameter("$Required",requirement.RequiredQuantity),Parameter("$Sequence",requirement.Sequence));
		var updated=await transaction.Session.ExecuteAsync(
			"UPDATE ProductionOrders SET Status=$Released,ReleasedAtUtc=$At,ReleasedByUserId=$User,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;",
			token,Parameter("$Released",(int)ProductionOrderStatus.Released),Parameter("$At",Utc(releasedAtUtc)),Parameter("$User",userId),Parameter("$Id",order.Id),Parameter("$Version",order.Version),Parameter("$Draft",(int)ProductionOrderStatus.Draft));
		if(updated!=1) throw new ConcurrencyConflictException("production order");
		return await GetOrderAsync(transaction,order.Id,token)??throw new InvalidOperationException("Production order could not be reloaded.");
	}

	public async Task<IReadOnlyList<ProductionRequirementAvailability>> GetAvailabilityAsync(long orderId,CancellationToken token)
	{
		var quantity=Database.CastToInt64("sm.Quantity");
		var reserved=Database.CastToInt64("ir.Quantity");
		var rows=await Database.QuerySliceAsync(
			$"""
			SELECT r.Id,r.ComponentItemId,r.ComponentPartNumber,r.RequiredQuantity,r.IssuedQuantity,
			       COALESCE((SELECT SUM({quantity}) FROM Inventories inv
			                 INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId
			                 INNER JOIN Warehouses w ON w.Id=sl.WarehouseId
			                 LEFT JOIN StockMovements sm ON sm.InventoryId=inv.Id
			                 WHERE inv.ItemId=r.ComponentItemId AND sl.WarehouseId=o.WarehouseId AND inv.IsActive=1 AND sl.IsActive=1 AND w.IsActive=1),0),
			       COALESCE((SELECT SUM({reserved}) FROM InventoryReservations ir
			                 INNER JOIN Inventories inv2 ON inv2.Id=ir.InventoryId
			                 INNER JOIN StorageLocations sl2 ON sl2.Id=inv2.StorageLocationId
			                 WHERE inv2.ItemId=r.ComponentItemId AND sl2.WarehouseId=o.WarehouseId AND ir.Status=$ReservationStatus),0)
			FROM ProductionOrderRequirements r INNER JOIN ProductionOrders o ON o.Id=r.ProductionOrderId
			WHERE r.ProductionOrderId=$Order ORDER BY r.Sequence,r.Id
			""",
			reader=>new RawAvailability(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetInt32(3),reader.GetInt32(4),
				Convert.ToInt64(reader.GetValue(5),CultureInfo.InvariantCulture),Convert.ToInt64(reader.GetValue(6),CultureInfo.InvariantCulture)),
			0,10000,token,Parameter("$ReservationStatus",(int)InventoryReservationStatus.Active),Parameter("$Order",orderId));
		return rows.Select(x=>
		{
			var available=Math.Max(0L,x.OnHand);
			var reservable=Math.Max(0L,available-x.Reserved);
			var remaining=Math.Max(0L,(long)x.Required-x.Issued);
			return new ProductionRequirementAvailability(x.RequirementId,x.ItemId,x.PartNumber,x.Required,x.Issued,available,x.Reserved,reservable,Math.Max(0L,remaining-reservable));
		}).ToArray();
	}

	internal Task<ProductionInventoryContext?> GetInventoryContextAsync(DatabaseTransactionContext transaction,long inventoryId,CancellationToken token)=>
		transaction.Session.QuerySingleOrDefaultAsync(
			"""
			SELECT inv.Id,inv.ItemId,sl.WarehouseId,inv.IsActive,sl.IsActive,w.IsActive
			FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId INNER JOIN Warehouses w ON w.Id=sl.WarehouseId
			WHERE inv.Id=$Id;
			""",
			reader=>new ProductionInventoryContext(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetBoolean(3),reader.GetBoolean(4),reader.GetBoolean(5)),
			token,Parameter("$Id",inventoryId));

	internal Task<ProductionMaterialMovement?> GetMovementByOperationKeyAsync(DatabaseTransactionContext transaction,string operationKey,CancellationToken token)=>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id,ProductionOrderId,RequirementId,StockMovementId,Kind,Quantity,OperationKey,CreatedAtUtc,CreatedByUserId FROM ProductionMaterialMovements WHERE OperationKey=$Key;",
			ReadMovement,token,Parameter("$Key",operationKey));

	internal async Task AddMovementEvidenceAsync(DatabaseTransactionContext transaction,ProductionMaterialMovement value,CancellationToken token)
	{
		value.Id=await transaction.Session.InsertAsync(
			"INSERT INTO ProductionMaterialMovements(ProductionOrderId,RequirementId,StockMovementId,Kind,Quantity,OperationKey,CreatedAtUtc,CreatedByUserId) VALUES($Order,$Requirement,$Movement,$Kind,$Quantity,$Key,$Created,$User);",
			token,Parameter("$Order",value.ProductionOrderId),Parameter("$Requirement",value.RequirementId),Parameter("$Movement",value.StockMovementId),Parameter("$Kind",(int)value.Kind),
			Parameter("$Quantity",value.Quantity),Parameter("$Key",value.OperationKey),Parameter("$Created",Utc(value.CreatedAtUtc)),Parameter("$User",value.CreatedByUserId));
	}

	internal async Task AdjustIssuedQuantityAsync(DatabaseTransactionContext transaction,long requirementId,int delta,CancellationToken token)
	{
		var updated=await transaction.Session.ExecuteAsync(
			"UPDATE ProductionOrderRequirements SET IssuedQuantity=IssuedQuantity+$Delta WHERE Id=$Id AND IssuedQuantity+$Delta>=0 AND IssuedQuantity+$Delta<=RequiredQuantity;",
			token,Parameter("$Delta",delta),Parameter("$Id",requirementId));
		if(updated!=1) throw new ConcurrencyConflictException("production requirement");
	}

	internal Task MarkInProgressAsync(DatabaseTransactionContext transaction,long orderId,CancellationToken token)=>
		transaction.Session.ExecuteAsync("UPDATE ProductionOrders SET Status=$InProgress,Version=Version+1 WHERE Id=$Id AND Status=$Released;",token,Parameter("$InProgress",(int)ProductionOrderStatus.InProgress),Parameter("$Id",orderId));

	internal async Task SaveCostEvidenceAsync(DatabaseTransactionContext transaction,IReadOnlyList<ProductionAssemblyCostEvidence> evidence,CancellationToken token)
	{
		foreach(var value in evidence)
		{
			value.Id=await transaction.Session.InsertAsync(
				"INSERT INTO ProductionAssemblyCostEvidence(ProductionOrderId,RequirementId,ComponentItemId,Quantity,UnitCost,ExtendedCost,Currency,EvidenceVersion,CalculatedAtUtc) VALUES($Order,$Requirement,$Item,$Quantity,$Unit,$Extended,$Currency,$Evidence,$At);",
				token,Parameter("$Order",value.ProductionOrderId),Parameter("$Requirement",value.RequirementId),Parameter("$Item",value.ComponentItemId),Parameter("$Quantity",value.Quantity),
				Parameter("$Unit",value.UnitCost),Parameter("$Extended",value.ExtendedCost),Parameter("$Currency",value.Currency),Parameter("$Evidence",value.EvidenceVersion),Parameter("$At",Utc(value.CalculatedAtUtc)));
		}
	}

	internal Task<IReadOnlyList<ProductionAssemblyCostEvidence>> ListCostEvidenceAsync(DatabaseTransactionContext transaction,long orderId,CancellationToken token)=>
		transaction.Session.QueryAsync(
			"SELECT Id,ProductionOrderId,RequirementId,ComponentItemId,Quantity,UnitCost,ExtendedCost,Currency,EvidenceVersion,CalculatedAtUtc FROM ProductionAssemblyCostEvidence WHERE ProductionOrderId=$Order ORDER BY RequirementId;",
			ReadCost,token,Parameter("$Order",orderId));

	public Task<IReadOnlyList<ProductionAssemblyCostEvidence>> ListCostEvidenceAsync(long orderId,CancellationToken token)=>
		Database.QuerySliceAsync(
			"SELECT Id,ProductionOrderId,RequirementId,ComponentItemId,Quantity,UnitCost,ExtendedCost,Currency,EvidenceVersion,CalculatedAtUtc FROM ProductionAssemblyCostEvidence WHERE ProductionOrderId=$Order ORDER BY RequirementId",
			ReadCost,0,10000,token,Parameter("$Order",orderId));

	internal async Task CompleteAsync(DatabaseTransactionContext transaction,long orderId,long version,int completedQuantity,long userId,DateTime atUtc,CancellationToken token)
	{
		var updated=await transaction.Session.ExecuteAsync(
			"UPDATE ProductionOrders SET Status=$Completed,CompletedQuantity=$Quantity,CompletedAtUtc=$At,CompletedByUserId=$User,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status IN ($Released,$InProgress);",
			token,Parameter("$Completed",(int)ProductionOrderStatus.Completed),Parameter("$Quantity",completedQuantity),Parameter("$At",Utc(atUtc)),Parameter("$User",userId),
			Parameter("$Id",orderId),Parameter("$Version",version),Parameter("$Released",(int)ProductionOrderStatus.Released),Parameter("$InProgress",(int)ProductionOrderStatus.InProgress));
		if(updated!=1) throw new ConcurrencyConflictException("production order");
	}

	internal async Task MarkReversedAsync(DatabaseTransactionContext transaction,long orderId,long version,long userId,DateTime atUtc,CancellationToken token)
	{
		var updated=await transaction.Session.ExecuteAsync(
			"UPDATE ProductionOrders SET Status=$Reversed,ReversedAtUtc=$At,ReversedByUserId=$User,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Completed;",
			token,Parameter("$Reversed",(int)ProductionOrderStatus.Reversed),Parameter("$At",Utc(atUtc)),Parameter("$User",userId),Parameter("$Id",orderId),Parameter("$Version",version),Parameter("$Completed",(int)ProductionOrderStatus.Completed));
		if(updated!=1) throw new ConcurrencyConflictException("production order");
	}

	internal Task<IReadOnlyList<ProductionMaterialMovement>> ListMovementEvidenceAsync(DatabaseTransactionContext transaction,long orderId,CancellationToken token)=>
		transaction.Session.QueryAsync(
			"SELECT Id,ProductionOrderId,RequirementId,StockMovementId,Kind,Quantity,OperationKey,CreatedAtUtc,CreatedByUserId FROM ProductionMaterialMovements WHERE ProductionOrderId=$Order ORDER BY Id;",
			ReadMovement,token,Parameter("$Order",orderId));

	private Task<IReadOnlyList<BillOfMaterialLine>> ListBomLinesAsync(long id,CancellationToken token)=>
		Database.QuerySliceAsync(
			"""
			SELECT l.Id,l.BillOfMaterialId,l.ComponentItemId,i.PartNumber,i.Description,u.Name,l.Quantity,l.Sequence
			FROM ProductionBillOfMaterialLines l INNER JOIN Items i ON i.Id=l.ComponentItemId LEFT JOIN UnitsOfMeasure u ON u.Id=i.UnitOfMeasureId
			WHERE l.BillOfMaterialId=$Id ORDER BY l.Sequence,l.Id
			""",ReadBomLine,0,10000,token,Parameter("$Id",id));

	private static Task<IReadOnlyList<BillOfMaterialLine>> ListBomLinesAsync(DatabaseTransactionContext transaction,long id,CancellationToken token)=>
		transaction.Session.QueryAsync(
			"""
			SELECT l.Id,l.BillOfMaterialId,l.ComponentItemId,i.PartNumber,i.Description,u.Name,l.Quantity,l.Sequence
			FROM ProductionBillOfMaterialLines l INNER JOIN Items i ON i.Id=l.ComponentItemId LEFT JOIN UnitsOfMeasure u ON u.Id=i.UnitOfMeasureId
			WHERE l.BillOfMaterialId=$Id ORDER BY l.Sequence,l.Id
			""",ReadBomLine,token,Parameter("$Id",id));

	private Task<IReadOnlyList<ProductionOrderRequirement>> ListRequirementsAsync(long orderId,CancellationToken token)=>
		Database.QuerySliceAsync(
			"SELECT Id,ProductionOrderId,SourceBillOfMaterialLineId,ComponentItemId,ComponentPartNumber,ComponentDescription,UnitOfMeasure,RequiredQuantity,IssuedQuantity,Sequence FROM ProductionOrderRequirements WHERE ProductionOrderId=$Order ORDER BY Sequence,Id",
			ReadRequirement,0,10000,token,Parameter("$Order",orderId));

	private static Task<IReadOnlyList<ProductionOrderRequirement>> ListRequirementsAsync(DatabaseTransactionContext transaction,long orderId,CancellationToken token)=>
		transaction.Session.QueryAsync(
			"SELECT Id,ProductionOrderId,SourceBillOfMaterialLineId,ComponentItemId,ComponentPartNumber,ComponentDescription,UnitOfMeasure,RequiredQuantity,IssuedQuantity,Sequence FROM ProductionOrderRequirements WHERE ProductionOrderId=$Order ORDER BY Sequence,Id;",
			ReadRequirement,token,Parameter("$Order",orderId));

	private static BillOfMaterial ReadBom(DbDataReader r)=>new(){Id=r.GetInt64(0),FinishedItemId=r.GetInt64(1),FinishedPartNumber=r.GetString(2),FinishedDescription=r.GetString(3),Revision=r.GetString(4),Status=(BillOfMaterialStatus)r.GetInt32(5),EffectiveFromUtc=r.IsDBNull(6)?null:ParseUtc(r.GetString(6)),EffectiveUntilUtc=r.IsDBNull(7)?null:ParseUtc(r.GetString(7)),Version=r.GetInt64(8),CreatedAtUtc=ParseUtc(r.GetString(9)),CreatedByUserId=r.GetInt64(10)};
	private static BillOfMaterialLine ReadBomLine(DbDataReader r)=>new(){Id=r.GetInt64(0),BillOfMaterialId=r.GetInt64(1),ComponentItemId=r.GetInt64(2),ComponentPartNumber=r.GetString(3),ComponentDescription=r.GetString(4),UnitOfMeasure=r.IsDBNull(5)?null:r.GetString(5),Quantity=Convert.ToDecimal(r.GetValue(6),CultureInfo.InvariantCulture),Sequence=r.GetInt32(7)};
	private static ProductionOrder ReadOrder(DbDataReader r)=>new(){Id=r.GetInt64(0),OrderNumber=r.GetString(1),FinishedItemId=r.GetInt64(2),FinishedPartNumber=r.GetString(3),FinishedDescription=r.GetString(4),BillOfMaterialId=r.GetInt64(5),BillOfMaterialRevision=r.GetString(6),PlannedQuantity=r.GetInt32(7),CompletedQuantity=r.GetInt32(8),WarehouseId=r.GetInt64(9),WarehouseName=r.GetString(10),FinishedInventoryId=r.GetInt64(11),Status=(ProductionOrderStatus)r.GetInt32(12),OwnerUserId=r.IsDBNull(13)?null:r.GetInt64(13),CreatedAtUtc=ParseUtc(r.GetString(14)),CreatedByUserId=r.GetInt64(15),ReleasedAtUtc=r.IsDBNull(16)?null:ParseUtc(r.GetString(16)),ReleasedByUserId=r.IsDBNull(17)?null:r.GetInt64(17),CompletedAtUtc=r.IsDBNull(18)?null:ParseUtc(r.GetString(18)),CompletedByUserId=r.IsDBNull(19)?null:r.GetInt64(19),ReversedAtUtc=r.IsDBNull(20)?null:ParseUtc(r.GetString(20)),ReversedByUserId=r.IsDBNull(21)?null:r.GetInt64(21),Version=r.GetInt64(22)};
	private static ProductionOrderRequirement ReadRequirement(DbDataReader r)=>new(){Id=r.GetInt64(0),ProductionOrderId=r.GetInt64(1),SourceBillOfMaterialLineId=r.GetInt64(2),ComponentItemId=r.GetInt64(3),ComponentPartNumber=r.GetString(4),ComponentDescription=r.GetString(5),UnitOfMeasure=r.IsDBNull(6)?null:r.GetString(6),RequiredQuantity=r.GetInt32(7),IssuedQuantity=r.GetInt32(8),Sequence=r.GetInt32(9)};
	private static ProductionMaterialMovement ReadMovement(DbDataReader r)=>new(){Id=r.GetInt64(0),ProductionOrderId=r.GetInt64(1),RequirementId=r.IsDBNull(2)?null:r.GetInt64(2),StockMovementId=r.GetInt64(3),Kind=(ProductionMovementKind)r.GetInt32(4),Quantity=r.GetInt32(5),OperationKey=r.GetString(6),CreatedAtUtc=ParseUtc(r.GetString(7)),CreatedByUserId=r.GetInt64(8)};
	private static ProductionAssemblyCostEvidence ReadCost(DbDataReader r)=>new(){Id=r.GetInt64(0),ProductionOrderId=r.GetInt64(1),RequirementId=r.GetInt64(2),ComponentItemId=r.GetInt64(3),Quantity=r.GetInt32(4),UnitCost=Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture),ExtendedCost=Convert.ToDecimal(r.GetValue(6),CultureInfo.InvariantCulture),Currency=r.GetString(7),EvidenceVersion=r.GetString(8),CalculatedAtUtc=ParseUtc(r.GetString(9))};
	private static string Utc(DateTime value)=>value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
	private static string? NullableUtc(DateTime? value)=>value is null?null:Utc(value.Value);
	private static DateTime ParseUtc(string value)=>DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
	private sealed record RawAvailability(long RequirementId,long ItemId,string PartNumber,int Required,int Issued,long OnHand,long Reserved);
}

internal sealed record ProductionInventoryContext(long InventoryId,long ItemId,long WarehouseId,bool InventoryActive,bool LocationActive,bool WarehouseActive);
