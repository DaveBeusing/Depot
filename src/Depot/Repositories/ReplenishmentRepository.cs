// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Services;

namespace Depot.Repositories;

public sealed class ReplenishmentRepository : DatabaseRepository
{
	private const string PolicyColumns = "p.Id,p.ItemId,i.PartNumber,i.Description,p.WarehouseId,w.Name,p.IsActive,p.ReorderPoint,p.SafetyStock,p.TargetStock,p.PreferredSupplierId,s.Name,p.Version";
	private const string PolicyFrom = "FROM ReplenishmentPolicies p INNER JOIN Items i ON i.Id=p.ItemId INNER JOIN Warehouses w ON w.Id=p.WarehouseId LEFT JOIN Suppliers s ON s.Id=p.PreferredSupplierId";

	private const string InputCtes = """
		WITH stock AS (
			SELECT inv.ItemId,sl.WarehouseId,SUM(sm.Quantity) AS OnHand
			FROM Inventories inv
			INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId
			INNER JOIN Warehouses w0 ON w0.Id=sl.WarehouseId
			LEFT JOIN StockMovements sm ON sm.InventoryId=inv.Id
			WHERE inv.IsActive=1 AND sl.IsActive=1 AND w0.IsActive=1
			GROUP BY inv.ItemId,sl.WarehouseId
		),
		reserved AS (
			SELECT inv.ItemId,sl.WarehouseId,SUM(r.Quantity) AS Reserved
			FROM InventoryReservations r
			INNER JOIN Inventories inv ON inv.Id=r.InventoryId
			INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId
			INNER JOIN Warehouses w0 ON w0.Id=sl.WarehouseId
			WHERE r.Status=$ActiveReservation AND inv.IsActive=1 AND sl.IsActive=1 AND w0.IsActive=1
			GROUP BY inv.ItemId,sl.WarehouseId
		),
		backorders AS (
			SELECT sol.ItemId,SUM(CASE WHEN sol.Quantity-sol.ShippedQuantity-sol.ReservedQuantity>0 THEN sol.Quantity-sol.ShippedQuantity-sol.ReservedQuantity ELSE 0 END) AS Backordered
			FROM SalesOrderLines sol
			INNER JOIN SalesOrders so ON so.Id=sol.SalesOrderId
			WHERE so.Status IN ($ReleasedOrder,$PartiallyShippedOrder)
			GROUP BY sol.ItemId
		),
		inbound AS (
			SELECT pol.ItemId,SUM(CASE WHEN pol.Quantity-pol.ReceivedQuantity>0 THEN pol.Quantity-pol.ReceivedQuantity ELSE 0 END) AS OpenInbound
			FROM PurchaseOrderLines pol
			INNER JOIN PurchaseOrders po ON po.Id=pol.PurchaseOrderId
			WHERE po.Status IN ($OrderedPurchase,$PartiallyReceivedPurchase)
			GROUP BY pol.ItemId
		),
		policy_counts AS (
			SELECT ItemId,COUNT(*) AS PolicyCount FROM ReplenishmentPolicies WHERE IsActive=1 GROUP BY ItemId
		)
		""";

	private const string InputSelect = """
		SELECT p.Id,p.ItemId,i.PartNumber,i.Description,p.WarehouseId,w.Name,p.IsActive,p.ReorderPoint,p.SafetyStock,p.TargetStock,
		       p.PreferredSupplierId,preferred.Name,p.Version,
		       COALESCE(st.OnHand,0),COALESCE(rv.Reserved,0),COALESCE(bo.Backordered,0),COALESCE(ib.OpenInbound,0),COALESCE(pc.PolicyCount,1),
		       COUNT(si.Id),MIN(si.Id),MIN(si.SupplierId),MIN(si.LeadTimeDays),MIN(si.MinimumOrderQuantity)
		FROM ReplenishmentPolicies p
		INNER JOIN Items i ON i.Id=p.ItemId
		INNER JOIN Warehouses w ON w.Id=p.WarehouseId
		LEFT JOIN Suppliers preferred ON preferred.Id=p.PreferredSupplierId
		LEFT JOIN stock st ON st.ItemId=p.ItemId AND st.WarehouseId=p.WarehouseId
		LEFT JOIN reserved rv ON rv.ItemId=p.ItemId AND rv.WarehouseId=p.WarehouseId
		LEFT JOIN backorders bo ON bo.ItemId=p.ItemId
		LEFT JOIN inbound ib ON ib.ItemId=p.ItemId
		LEFT JOIN policy_counts pc ON pc.ItemId=p.ItemId
		LEFT JOIN SupplierItems si ON si.ItemId=p.ItemId AND si.IsActive=1
		  AND ((p.PreferredSupplierId IS NOT NULL AND si.SupplierId=p.PreferredSupplierId)
		    OR (p.PreferredSupplierId IS NULL AND si.IsPreferredSupplier=1))
		""";

	private const string InputGroup = """
		GROUP BY p.Id,p.ItemId,i.PartNumber,i.Description,p.WarehouseId,w.Name,p.IsActive,p.ReorderPoint,p.SafetyStock,p.TargetStock,
		         p.PreferredSupplierId,preferred.Name,p.Version,st.OnHand,rv.Reserved,bo.Backordered,ib.OpenInbound,pc.PolicyCount
		""";

	private const string SuggestionSelect = """
		SELECT sg.Id,sg.PolicyId,sg.SnapshotId,sg.Status,sg.SuggestedQuantity,sg.CreatedAtUtc,sg.ReviewedAtUtc,sg.ReviewedByUserId,sg.ConvertedPurchaseRequisitionId,sg.Version,
		       p.ItemId,i.PartNumber,i.Description,p.WarehouseId,w.Name,sup.Name,
		       rs.Id,rs.PolicyId,rs.SnapshotKey,rs.CalculatedAtUtc,rs.OnHandQuantity,rs.ReservedQuantity,rs.BackorderedQuantity,rs.EligibleInboundQuantity,
		       rs.ProjectedAvailableQuantity,rs.ReorderPoint,rs.SafetyStock,rs.TargetStock,rs.RequiredReplenishmentQuantity,rs.SuggestedPurchaseQuantity,
		       rs.PlanningSupplierId,rs.SupplierItemId,rs.SupplierLeadTimeDays,rs.SupplierMinimumOrderQuantity,rs.IsBlocked,rs.BlockReason,rs.Explanation
		FROM ReplenishmentSuggestions sg
		INNER JOIN ReplenishmentPolicies p ON p.Id=sg.PolicyId
		INNER JOIN Items i ON i.Id=p.ItemId
		INNER JOIN Warehouses w ON w.Id=p.WarehouseId
		INNER JOIN ReplenishmentRequirementSnapshots rs ON rs.Id=sg.SnapshotId
		LEFT JOIN Suppliers sup ON sup.Id=rs.PlanningSupplierId
		""";

	public ReplenishmentRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<ReplenishmentPolicy>> SearchPoliciesAsync(string? searchText, bool includeInactive, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!includeInactive) predicates.Add("p.IsActive=1");
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			predicates.Add("(i.PartNumber LIKE $Search OR i.Description LIKE $Search OR w.Name LIKE $Search OR s.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
		return Database.QueryPageAsync(
			$"SELECT {PolicyColumns} {PolicyFrom} {where} ORDER BY i.PartNumber,w.Name,p.Id",
			$"SELECT COUNT(*) {PolicyFrom} {where}",
			ReadPolicy,pageNumber,pageSize,cancellationToken,parameters.ToArray());
	}

	public Task<ReplenishmentPolicy?> GetPolicyAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {PolicyColumns} {PolicyFrom} WHERE p.Id=$Id;",ReadPolicy,cancellationToken,Parameter("$Id",id));

	internal Task<ReplenishmentPolicy?> GetPolicyAsync(DatabaseTransactionContext transaction,long id,CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {PolicyColumns} {PolicyFrom} WHERE p.Id=$Id;",ReadPolicy,cancellationToken,Parameter("$Id",id));

	internal async Task<ReplenishmentPolicy> SavePolicyAsync(DatabaseTransactionContext transaction, ReplenishmentPolicy value, CancellationToken cancellationToken)
	{
		if (value.Id == 0)
		{
			value.Id = await transaction.Session.InsertAsync(
				"INSERT INTO ReplenishmentPolicies(ItemId,WarehouseId,IsActive,ReorderPoint,SafetyStock,TargetStock,PreferredSupplierId) VALUES($Item,$Warehouse,$Active,$Reorder,$Safety,$Target,$Supplier);",
				cancellationToken,Parameter("$Item",value.ItemId),Parameter("$Warehouse",value.WarehouseId),Parameter("$Active",value.IsActive),
				Parameter("$Reorder",value.ReorderPoint),Parameter("$Safety",value.SafetyStock),Parameter("$Target",value.TargetStock),Parameter("$Supplier",value.PreferredSupplierId));
		}
		else
		{
			var updated=await transaction.Session.ExecuteAsync(
				"UPDATE ReplenishmentPolicies SET ItemId=$Item,WarehouseId=$Warehouse,IsActive=$Active,ReorderPoint=$Reorder,SafetyStock=$Safety,TargetStock=$Target,PreferredSupplierId=$Supplier,Version=Version+1 WHERE Id=$Id AND Version=$Version;",
				cancellationToken,Parameter("$Item",value.ItemId),Parameter("$Warehouse",value.WarehouseId),Parameter("$Active",value.IsActive),
				Parameter("$Reorder",value.ReorderPoint),Parameter("$Safety",value.SafetyStock),Parameter("$Target",value.TargetStock),Parameter("$Supplier",value.PreferredSupplierId),
				Parameter("$Id",value.Id),Parameter("$Version",value.Version));
			if(updated!=1) throw new ConcurrencyConflictException("replenishment policy");
			value.Version++;
		}
		return await GetPolicyAsync(transaction,value.Id,cancellationToken) ?? throw new InvalidOperationException("Replenishment policy could not be reloaded.");
	}

	public Task<IReadOnlyList<ReplenishmentRequirementInput>> LoadRequirementInputsSliceAsync(int offset,int count,CancellationToken cancellationToken) =>
		Database.QuerySliceAsync(
			$"{InputCtes} {InputSelect} WHERE p.IsActive=1 {InputGroup} ORDER BY p.Id",
			ReadInput,offset,count,cancellationToken,InputParameters());

	public Task<ReplenishmentRequirementInput?> LoadRequirementInputAsync(long policyId,CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"{InputCtes} {InputSelect} WHERE p.IsActive=1 AND p.Id=$PolicyId {InputGroup};",
			ReadInput,cancellationToken,[..InputParameters(),Parameter("$PolicyId",policyId)]);

	public Task<PageResult<ReplenishmentSuggestion>> SearchSuggestionsAsync(ReplenishmentSuggestionStatus? status,int pageNumber,int pageSize,CancellationToken cancellationToken)
	{
		var where=status is null ? string.Empty : "WHERE sg.Status=$Status";
		var parameters=status is null ? [] : new[] { Parameter("$Status",(int)status.Value) };
		return Database.QueryPageAsync(
			$"{SuggestionSelect} {where} ORDER BY sg.CreatedAtUtc DESC,sg.Id DESC",
			$"SELECT COUNT(*) FROM ReplenishmentSuggestions sg {where}",
			ReadSuggestion,pageNumber,pageSize,cancellationToken,parameters);
	}

	public Task<IReadOnlyList<ReplenishmentSuggestion>> ListActionableAsync(int count,CancellationToken cancellationToken) =>
		Database.QuerySliceAsync(
			$"{SuggestionSelect} WHERE sg.Status IN ($Open,$Blocked) ORDER BY CASE WHEN sg.Status=$Open THEN 0 ELSE 1 END,sg.CreatedAtUtc,sg.Id",
			ReadSuggestion,0,count,cancellationToken,Parameter("$Open",(int)ReplenishmentSuggestionStatus.Open),Parameter("$Blocked",(int)ReplenishmentSuggestionStatus.Blocked));

	public Task<ReplenishmentSuggestion?> GetSuggestionAsync(long id,CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"{SuggestionSelect} WHERE sg.Id=$Id;",ReadSuggestion,cancellationToken,Parameter("$Id",id));

	internal async Task<(ReplenishmentSuggestion? Suggestion,int Superseded)> ApplySnapshotAsync(
		DatabaseTransactionContext transaction,ReplenishmentRequirementSnapshot snapshot,bool createSuggestion,CancellationToken cancellationToken)
	{
		var existingSnapshotId=await transaction.Session.ExecuteScalarAsync(
			"SELECT Id FROM ReplenishmentRequirementSnapshots WHERE PolicyId=$PolicyId AND SnapshotKey=$SnapshotKey;",
			cancellationToken,Parameter("$PolicyId",snapshot.PolicyId),Parameter("$SnapshotKey",snapshot.SnapshotKey));
		if(existingSnapshotId is not null and not DBNull)
		{
			snapshot.Id=Convert.ToInt64(existingSnapshotId,CultureInfo.InvariantCulture);
			return (await GetSuggestionBySnapshotAsync(transaction,snapshot.Id,cancellationToken),0);
		}

		snapshot.Id=await transaction.Session.InsertAsync(
			"INSERT INTO ReplenishmentRequirementSnapshots(PolicyId,SnapshotKey,CalculatedAtUtc,OnHandQuantity,ReservedQuantity,BackorderedQuantity,EligibleInboundQuantity,ProjectedAvailableQuantity,ReorderPoint,SafetyStock,TargetStock,RequiredReplenishmentQuantity,SuggestedPurchaseQuantity,PlanningSupplierId,SupplierItemId,SupplierLeadTimeDays,SupplierMinimumOrderQuantity,IsBlocked,BlockReason,Explanation) VALUES($Policy,$Key,$Calculated,$OnHand,$Reserved,$Backordered,$Inbound,$Projected,$Reorder,$Safety,$Target,$Required,$Suggested,$Supplier,$SupplierItem,$LeadTime,$Moq,$Blocked,$BlockReason,$Explanation);",
			cancellationToken,
			Parameter("$Policy",snapshot.PolicyId),Parameter("$Key",snapshot.SnapshotKey),Parameter("$Calculated",Utc(snapshot.CalculatedAtUtc)),
			Parameter("$OnHand",snapshot.OnHandQuantity),Parameter("$Reserved",snapshot.ReservedQuantity),Parameter("$Backordered",snapshot.BackorderedQuantity),
			Parameter("$Inbound",snapshot.EligibleInboundQuantity),Parameter("$Projected",snapshot.ProjectedAvailableQuantity),Parameter("$Reorder",snapshot.ReorderPoint),
			Parameter("$Safety",snapshot.SafetyStock),Parameter("$Target",snapshot.TargetStock),Parameter("$Required",snapshot.RequiredReplenishmentQuantity),
			Parameter("$Suggested",snapshot.SuggestedPurchaseQuantity),Parameter("$Supplier",snapshot.PlanningSupplierId),Parameter("$SupplierItem",snapshot.SupplierItemId),
			Parameter("$LeadTime",snapshot.SupplierLeadTimeDays),Parameter("$Moq",snapshot.SupplierMinimumOrderQuantity),Parameter("$Blocked",snapshot.IsBlocked),
			Parameter("$BlockReason",snapshot.BlockReason),Parameter("$Explanation",snapshot.Explanation));

		var superseded=await transaction.Session.ExecuteAsync(
			"UPDATE ReplenishmentSuggestions SET Status=$Superseded,Version=Version+1 WHERE PolicyId=$Policy AND Status IN ($Open,$Blocked);",
			cancellationToken,Parameter("$Superseded",(int)ReplenishmentSuggestionStatus.Superseded),Parameter("$Policy",snapshot.PolicyId),
			Parameter("$Open",(int)ReplenishmentSuggestionStatus.Open),Parameter("$Blocked",(int)ReplenishmentSuggestionStatus.Blocked));

		if(!createSuggestion) return (null,superseded);
		var status=snapshot.IsBlocked ? ReplenishmentSuggestionStatus.Blocked : ReplenishmentSuggestionStatus.Open;
		var suggestionId=await transaction.Session.InsertAsync(
			"INSERT INTO ReplenishmentSuggestions(PolicyId,SnapshotId,Status,SuggestedQuantity,CreatedAtUtc) VALUES($Policy,$Snapshot,$Status,$Quantity,$Created);",
			cancellationToken,Parameter("$Policy",snapshot.PolicyId),Parameter("$Snapshot",snapshot.Id),Parameter("$Status",(int)status),
			Parameter("$Quantity",snapshot.SuggestedPurchaseQuantity),Parameter("$Created",Utc(snapshot.CalculatedAtUtc)));
		return (await GetSuggestionAsync(transaction,suggestionId,cancellationToken),superseded);
	}

	internal async Task<IReadOnlyList<ReplenishmentSuggestion>> GetSuggestionsByIdsAsync(DatabaseTransactionContext transaction,IReadOnlyCollection<long> ids,CancellationToken cancellationToken)
	{
		var ordered=ids.Distinct().Order().ToArray();
		if(ordered.Length==0) return [];
		var parameters=ordered.Select((id,index)=>Parameter($"$Id{index}",id)).ToArray();
		var list=string.Join(",",parameters.Select(x=>x.Name));
		return await transaction.Session.QueryAsync($"{SuggestionSelect} WHERE sg.Id IN ({list}) ORDER BY sg.Id;",ReadSuggestion,cancellationToken,parameters);
	}

	internal async Task<bool> DismissAsync(DatabaseTransactionContext transaction,long id,long version,long userId,DateTime reviewedAtUtc,CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE ReplenishmentSuggestions SET Status=$Dismissed,ReviewedAtUtc=$ReviewedAt,ReviewedByUserId=$User,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status IN ($Open,$Blocked);",
			cancellationToken,Parameter("$Dismissed",(int)ReplenishmentSuggestionStatus.Dismissed),Parameter("$ReviewedAt",Utc(reviewedAtUtc)),Parameter("$User",userId),
			Parameter("$Id",id),Parameter("$Version",version),Parameter("$Open",(int)ReplenishmentSuggestionStatus.Open),Parameter("$Blocked",(int)ReplenishmentSuggestionStatus.Blocked))==1;

	internal async Task<bool> AcceptAsync(DatabaseTransactionContext transaction,long id,long version,long requisitionId,long userId,DateTime reviewedAtUtc,CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE ReplenishmentSuggestions SET Status=$Accepted,ReviewedAtUtc=$ReviewedAt,ReviewedByUserId=$User,ConvertedPurchaseRequisitionId=$Requisition,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Open AND ConvertedPurchaseRequisitionId IS NULL;",
			cancellationToken,Parameter("$Accepted",(int)ReplenishmentSuggestionStatus.Accepted),Parameter("$ReviewedAt",Utc(reviewedAtUtc)),Parameter("$User",userId),
			Parameter("$Requisition",requisitionId),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Open",(int)ReplenishmentSuggestionStatus.Open))==1;

	private static Task<ReplenishmentSuggestion?> GetSuggestionAsync(DatabaseTransactionContext transaction,long id,CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"{SuggestionSelect} WHERE sg.Id=$Id;",ReadSuggestion,cancellationToken,Parameter("$Id",id));

	private static Task<ReplenishmentSuggestion?> GetSuggestionBySnapshotAsync(DatabaseTransactionContext transaction,long snapshotId,CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"{SuggestionSelect} WHERE sg.SnapshotId=$SnapshotId;",ReadSuggestion,cancellationToken,Parameter("$SnapshotId",snapshotId));

	private static DatabaseParameter[] InputParameters() =>
	[
		Parameter("$ActiveReservation",(int)InventoryReservationStatus.Active),
		Parameter("$ReleasedOrder",(int)SalesOrderStatus.Released),
		Parameter("$PartiallyShippedOrder",(int)SalesOrderStatus.PartiallyShipped),
		Parameter("$OrderedPurchase",(int)PurchaseOrderStatus.Ordered),
		Parameter("$PartiallyReceivedPurchase",(int)PurchaseOrderStatus.PartiallyReceived)
	];

	private static ReplenishmentPolicy ReadPolicy(DbDataReader reader) => new()
	{
		Id=reader.GetInt64(0),ItemId=reader.GetInt64(1),ItemPartNumber=reader.GetString(2),ItemDescription=reader.GetString(3),
		WarehouseId=reader.GetInt64(4),WarehouseName=reader.GetString(5),IsActive=reader.GetBoolean(6),
		ReorderPoint=reader.GetInt32(7),SafetyStock=reader.GetInt32(8),TargetStock=reader.GetInt32(9),
		PreferredSupplierId=reader.IsDBNull(10)?null:reader.GetInt64(10),PreferredSupplierName=reader.IsDBNull(11)?null:reader.GetString(11),Version=reader.GetInt64(12)
	};

	private static ReplenishmentRequirementInput ReadInput(DbDataReader reader) => new()
	{
		Policy=ReadPolicy(reader),
		OnHandQuantity=Convert.ToInt64(reader.GetValue(13),CultureInfo.InvariantCulture),
		ReservedQuantity=Convert.ToInt64(reader.GetValue(14),CultureInfo.InvariantCulture),
		BackorderedQuantity=Convert.ToInt64(reader.GetValue(15),CultureInfo.InvariantCulture),
		EligibleInboundQuantity=Convert.ToInt64(reader.GetValue(16),CultureInfo.InvariantCulture),
		ActivePolicyCountForItem=Convert.ToInt32(reader.GetValue(17),CultureInfo.InvariantCulture),
		SupplierEvidenceCount=Convert.ToInt32(reader.GetValue(18),CultureInfo.InvariantCulture),
		SupplierItemId=reader.IsDBNull(19)?null:reader.GetInt64(19),
		PlanningSupplierId=reader.IsDBNull(20)?null:reader.GetInt64(20),
		SupplierLeadTimeDays=reader.IsDBNull(21)?null:reader.GetInt32(21),
		SupplierMinimumOrderQuantity=reader.IsDBNull(22)?null:Convert.ToDecimal(reader.GetValue(22),CultureInfo.InvariantCulture)
	};

	private static ReplenishmentSuggestion ReadSuggestion(DbDataReader reader)
	{
		var snapshot=new ReplenishmentRequirementSnapshot
		{
			Id=reader.GetInt64(16),PolicyId=reader.GetInt64(17),SnapshotKey=reader.GetString(18),CalculatedAtUtc=ParseUtc(reader.GetString(19)),
			OnHandQuantity=reader.GetInt64(20),ReservedQuantity=reader.GetInt64(21),BackorderedQuantity=reader.GetInt64(22),EligibleInboundQuantity=reader.GetInt64(23),
			ProjectedAvailableQuantity=reader.GetInt64(24),ReorderPoint=reader.GetInt32(25),SafetyStock=reader.GetInt32(26),TargetStock=reader.GetInt32(27),
			RequiredReplenishmentQuantity=reader.GetInt64(28),SuggestedPurchaseQuantity=reader.GetInt32(29),
			PlanningSupplierId=reader.IsDBNull(30)?null:reader.GetInt64(30),SupplierItemId=reader.IsDBNull(31)?null:reader.GetInt64(31),
			SupplierLeadTimeDays=reader.IsDBNull(32)?null:reader.GetInt32(32),SupplierMinimumOrderQuantity=reader.IsDBNull(33)?null:reader.GetDecimal(33),
			IsBlocked=reader.GetBoolean(34),BlockReason=reader.IsDBNull(35)?null:reader.GetString(35),Explanation=reader.GetString(36)
		};
		return new ReplenishmentSuggestion
		{
			Id=reader.GetInt64(0),PolicyId=reader.GetInt64(1),SnapshotId=reader.GetInt64(2),Status=(ReplenishmentSuggestionStatus)reader.GetInt32(3),
			SuggestedQuantity=reader.GetInt32(4),CreatedAtUtc=ParseUtc(reader.GetString(5)),ReviewedAtUtc=reader.IsDBNull(6)?null:ParseUtc(reader.GetString(6)),
			ReviewedByUserId=reader.IsDBNull(7)?null:reader.GetInt64(7),ConvertedPurchaseRequisitionId=reader.IsDBNull(8)?null:reader.GetInt64(8),Version=reader.GetInt64(9),
			ItemId=reader.GetInt64(10),ItemPartNumber=reader.GetString(11),ItemDescription=reader.GetString(12),WarehouseId=reader.GetInt64(13),WarehouseName=reader.GetString(14),
			PlanningSupplierName=reader.IsDBNull(15)?null:reader.GetString(15),Snapshot=snapshot
		};
	}

	private static string Utc(DateTime value)=>value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
	private static DateTime ParseUtc(string value)=>DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
}
