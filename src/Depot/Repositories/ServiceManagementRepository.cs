// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ServiceManagementRepository : DatabaseRepository
{
	private const string CaseColumns = "c.Id,c.Version,c.CaseNumber,c.CustomerId,cu.Name,c.CustomerContactId,cc.Name,c.Subject,c.Description,c.Category,c.Priority,c.Status,c.OwnerUserId,u.DisplayName,c.SalesOrderId,c.ItemId,c.CreatedAtUtc,c.CreatedByUserId,c.UpdatedAtUtc,c.UpdatedByUserId,c.DueAtUtc,c.ResolvedAtUtc,c.ResolvedByUserId,c.ClosedAtUtc,c.ClosedByUserId,c.CancelledAtUtc,c.CancelledByUserId";
	private const string CaseFrom = " FROM ServiceCases c INNER JOIN Customers cu ON cu.Id=c.CustomerId LEFT JOIN CustomerContacts cc ON cc.Id=c.CustomerContactId LEFT JOIN Users u ON u.Id=c.OwnerUserId";
	private const string OrderColumns = "o.Id,o.Version,o.OrderNumber,o.ServiceCaseId,c.CaseNumber,o.CustomerId,cu.Name,o.AssignedOwnerUserId,u.DisplayName,o.ServicedItemId,i.PartNumber,o.ServicedInventoryId,o.SerialLotReference,o.Status,o.PlannedStartAtUtc,o.PlannedEndAtUtc,o.CompletionNotes,o.CompletedAtUtc,o.CompletedByUserId,o.SalesInvoiceId,si.InvoiceNumber,o.CreatedAtUtc,o.CreatedByUserId,o.UpdatedAtUtc,o.UpdatedByUserId";
	private const string OrderFrom = " FROM ServiceOrders o INNER JOIN ServiceCases c ON c.Id=o.ServiceCaseId INNER JOIN Customers cu ON cu.Id=o.CustomerId LEFT JOIN Users u ON u.Id=o.AssignedOwnerUserId LEFT JOIN Items i ON i.Id=o.ServicedItemId LEFT JOIN SalesInvoices si ON si.Id=o.SalesInvoiceId";

	public ServiceManagementRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<ServiceCase>> SearchCasesAsync(ServiceCaseFilter filter,int pageNumber,int pageSize,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var predicates=new List<string>();var parameters=new List<DatabaseParameter>();
		if(filter.Status is{} status){predicates.Add("c.Status=$Status");parameters.Add(Parameter("$Status",(int)status));}
		if(filter.Priority is{} priority){predicates.Add("c.Priority=$Priority");parameters.Add(Parameter("$Priority",(int)priority));}
		if(filter.OwnerUserId is{} owner){predicates.Add("c.OwnerUserId=$Owner");parameters.Add(Parameter("$Owner",owner));}
		if(filter.CustomerId is{} customer){predicates.Add("c.CustomerId=$Customer");parameters.Add(Parameter("$Customer",customer));}
		if(!string.IsNullOrWhiteSpace(filter.SearchText)){predicates.Add("(c.CaseNumber LIKE $Search OR c.Subject LIKE $Search OR cu.Name LIKE $Search OR c.Description LIKE $Search)");parameters.Add(Parameter("$Search",$"%{filter.SearchText.Trim()}%"));}
		var where=predicates.Count==0?string.Empty:" WHERE "+string.Join(" AND ",predicates);
		return Database.QueryPageAsync($"SELECT {CaseColumns}{CaseFrom}{where} ORDER BY CASE WHEN c.Status IN (6,7) THEN 1 ELSE 0 END,c.Priority DESC,c.DueAtUtc,c.Id DESC",$"SELECT COUNT(*){CaseFrom}{where};",ReadCase,Math.Max(1,pageNumber),Math.Clamp(pageSize,1,200),token,parameters.ToArray());
	}

	public Task<ServiceCase?> GetCaseAsync(long id,CancellationToken token=default)=>Database.QuerySingleOrDefaultAsync($"SELECT {CaseColumns}{CaseFrom} WHERE c.Id=$Id;",ReadCase,token,Parameter("$Id",id));
	internal Task<ServiceCase?> GetCaseAsync(DatabaseTransactionContext tx,long id,CancellationToken token)=>tx.Session.QuerySingleOrDefaultAsync($"SELECT {CaseColumns}{CaseFrom} WHERE c.Id=$Id;",ReadCase,token,Parameter("$Id",id));

	public Task<IReadOnlyList<ServiceCaseHistory>> ListCaseHistoryAsync(long caseId,CancellationToken token=default)=>
		Database.QueryAsync("SELECT Id,ServiceCaseId,PreviousStatus,Status,Note,CreatedAtUtc,CreatedByUserId FROM ServiceCaseHistory WHERE ServiceCaseId=$CaseId ORDER BY CreatedAtUtc,Id;",ReadHistory,token,Parameter("$CaseId",caseId));

	internal async Task<ServiceCase> CreateCaseAsync(DatabaseTransactionContext tx,ServiceCase value,CancellationToken token)
	{
		var id=await tx.Session.InsertAsync("""
			INSERT INTO ServiceCases
			(Version,CaseNumber,CustomerId,CustomerContactId,Subject,Description,Category,Priority,Status,OwnerUserId,SalesOrderId,ItemId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId,DueAtUtc,ResolvedAtUtc,ResolvedByUserId,ClosedAtUtc,ClosedByUserId,CancelledAtUtc,CancelledByUserId)
			VALUES(1,$PendingNumber,$CustomerId,$ContactId,$Subject,$Description,$Category,$Priority,$Status,$Owner,$SalesOrderId,$ItemId,$CreatedAt,$CreatedBy,$UpdatedAt,$UpdatedBy,$DueAt,$ResolvedAt,$ResolvedBy,$ClosedAt,$ClosedBy,$CancelledAt,$CancelledBy);
			""",token,[Parameter("$PendingNumber",$"PENDING-{Guid.NewGuid():N}"),..CaseParameters(value)]);
		var number=$"SC-{id:000000}";
		await tx.Session.ExecuteAsync("UPDATE ServiceCases SET CaseNumber=$Number WHERE Id=$Id;",token,Parameter("$Number",number),Parameter("$Id",id));
		return value with{Id=id,Version=1,CaseNumber=number};
	}

	internal Task<int> UpdateCaseAsync(DatabaseTransactionContext tx,ServiceCase value,long expectedVersion,CancellationToken token)=>
		tx.Session.ExecuteAsync("""
			UPDATE ServiceCases SET Version=Version+1,CustomerId=$CustomerId,CustomerContactId=$ContactId,Subject=$Subject,Description=$Description,Category=$Category,
			Priority=$Priority,Status=$Status,OwnerUserId=$Owner,SalesOrderId=$SalesOrderId,ItemId=$ItemId,UpdatedAtUtc=$UpdatedAt,UpdatedByUserId=$UpdatedBy,
			DueAtUtc=$DueAt,ResolvedAtUtc=$ResolvedAt,ResolvedByUserId=$ResolvedBy,ClosedAtUtc=$ClosedAt,ClosedByUserId=$ClosedBy,CancelledAtUtc=$CancelledAt,CancelledByUserId=$CancelledBy
			WHERE Id=$Id AND Version=$Version;
			""",token,[..CaseParameters(value),Parameter("$Id",value.Id),Parameter("$Version",expectedVersion)]);

	internal Task AppendCaseHistoryAsync(DatabaseTransactionContext tx,long caseId,ServiceCaseStatus? previous,ServiceCaseStatus status,string? note,long userId,DateTime now,CancellationToken token)=>
		tx.Session.ExecuteAsync("INSERT INTO ServiceCaseHistory(ServiceCaseId,PreviousStatus,Status,Note,CreatedAtUtc,CreatedByUserId) VALUES($CaseId,$Previous,$Status,$Note,$CreatedAt,$CreatedBy);",token,
			Parameter("$CaseId",caseId),Parameter("$Previous",previous is null?null:(int)previous.Value),Parameter("$Status",(int)status),Parameter("$Note",Normalize(note)),Parameter("$CreatedAt",Utc(now)),Parameter("$CreatedBy",userId));

	public Task<ServiceOrder?> GetOrderAsync(long id,CancellationToken token=default)=>Database.QuerySingleOrDefaultAsync($"SELECT {OrderColumns}{OrderFrom} WHERE o.Id=$Id;",ReadOrder,token,Parameter("$Id",id));
	public Task<ServiceOrder?> GetOrderForCaseAsync(long caseId,CancellationToken token=default)=>Database.QuerySingleOrDefaultAsync($"SELECT {OrderColumns}{OrderFrom} WHERE o.ServiceCaseId=$CaseId;",ReadOrder,token,Parameter("$CaseId",caseId));
	internal Task<ServiceOrder?> GetOrderAsync(DatabaseTransactionContext tx,long id,CancellationToken token)=>tx.Session.QuerySingleOrDefaultAsync($"SELECT {OrderColumns}{OrderFrom} WHERE o.Id=$Id;",ReadOrder,token,Parameter("$Id",id));
	internal Task<ServiceOrder?> GetOrderForCaseAsync(DatabaseTransactionContext tx,long caseId,CancellationToken token)=>tx.Session.QuerySingleOrDefaultAsync($"SELECT {OrderColumns}{OrderFrom} WHERE o.ServiceCaseId=$CaseId;",ReadOrder,token,Parameter("$CaseId",caseId));

	internal async Task<ServiceOrder> CreateOrderAsync(DatabaseTransactionContext tx,ServiceOrder value,CancellationToken token)
	{
		var id=await tx.Session.InsertAsync("""
			INSERT INTO ServiceOrders
			(Version,OrderNumber,ServiceCaseId,CustomerId,AssignedOwnerUserId,ServicedItemId,ServicedInventoryId,SerialLotReference,Status,PlannedStartAtUtc,PlannedEndAtUtc,CompletionNotes,CompletedAtUtc,CompletedByUserId,SalesInvoiceId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId)
			VALUES(1,$PendingNumber,$CaseId,$CustomerId,$Owner,$ItemId,$InventoryId,$SerialLot,$Status,$Start,$End,$CompletionNotes,$CompletedAt,$CompletedBy,$InvoiceId,$CreatedAt,$CreatedBy,$UpdatedAt,$UpdatedBy);
			""",token,[Parameter("$PendingNumber",$"PENDING-{Guid.NewGuid():N}"),..OrderParameters(value)]);
		var number=$"SVC-{id:000000}";
		await tx.Session.ExecuteAsync("UPDATE ServiceOrders SET OrderNumber=$Number WHERE Id=$Id;",token,Parameter("$Number",number),Parameter("$Id",id));
		return value with{Id=id,Version=1,OrderNumber=number};
	}

	internal Task<int> UpdateOrderAsync(DatabaseTransactionContext tx,ServiceOrder value,long expectedVersion,CancellationToken token)=>
		tx.Session.ExecuteAsync("""
			UPDATE ServiceOrders SET Version=Version+1,AssignedOwnerUserId=$Owner,ServicedItemId=$ItemId,ServicedInventoryId=$InventoryId,SerialLotReference=$SerialLot,
			Status=$Status,PlannedStartAtUtc=$Start,PlannedEndAtUtc=$End,CompletionNotes=$CompletionNotes,CompletedAtUtc=$CompletedAt,CompletedByUserId=$CompletedBy,
			SalesInvoiceId=$InvoiceId,UpdatedAtUtc=$UpdatedAt,UpdatedByUserId=$UpdatedBy
			WHERE Id=$Id AND Version=$Version;
			""",token,[..OrderParameters(value),Parameter("$Id",value.Id),Parameter("$Version",expectedVersion)]);

	public Task<IReadOnlyList<ServiceWorkLine>> ListWorkLinesAsync(long orderId,CancellationToken token=default)=>
		Database.QueryAsync("SELECT Id,Version,ServiceOrderId,Description,Quantity,UnitPrice,Billable,TaxRate,CreatedAtUtc,CreatedByUserId FROM ServiceWorkLines WHERE ServiceOrderId=$OrderId ORDER BY Id;",ReadWorkLine,token,Parameter("$OrderId",orderId));
	internal Task<IReadOnlyList<ServiceWorkLine>> ListWorkLinesAsync(DatabaseTransactionContext tx,long orderId,CancellationToken token)=>
		tx.Session.QueryAsync("SELECT Id,Version,ServiceOrderId,Description,Quantity,UnitPrice,Billable,TaxRate,CreatedAtUtc,CreatedByUserId FROM ServiceWorkLines WHERE ServiceOrderId=$OrderId ORDER BY Id;",ReadWorkLine,token,Parameter("$OrderId",orderId));
	internal async Task<ServiceWorkLine> CreateWorkLineAsync(DatabaseTransactionContext tx,ServiceWorkLine value,CancellationToken token)
	{
		var id=await tx.Session.InsertAsync("INSERT INTO ServiceWorkLines(Version,ServiceOrderId,Description,Quantity,UnitPrice,Billable,TaxRate,CreatedAtUtc,CreatedByUserId) VALUES(1,$OrderId,$Description,$Quantity,$UnitPrice,$Billable,$TaxRate,$CreatedAt,$CreatedBy);",token,WorkParameters(value));
		return value with{Id=id,Version=1};
	}
	internal Task<int> DeleteWorkLineAsync(DatabaseTransactionContext tx,long id,long orderId,long expectedVersion,CancellationToken token)=>
		tx.Session.ExecuteAsync("DELETE FROM ServiceWorkLines WHERE Id=$Id AND ServiceOrderId=$OrderId AND Version=$Version;",token,Parameter("$Id",id),Parameter("$OrderId",orderId),Parameter("$Version",expectedVersion));

	public Task<IReadOnlyList<ServicePartEvidence>> ListPartEvidenceAsync(long orderId,CancellationToken token=default)=>
		Database.QueryAsync("SELECT Id,ServiceOrderId,StockMovementId,MovementKind,InventoryId,ItemId,PartNumber,Description,Quantity,UnitPrice,Billable,TaxRate,CreatedAtUtc,CreatedByUserId FROM ServicePartEvidence WHERE ServiceOrderId=$OrderId ORDER BY Id;",ReadPart,token,Parameter("$OrderId",orderId));
	internal Task<IReadOnlyList<ServicePartEvidence>> ListPartEvidenceAsync(DatabaseTransactionContext tx,long orderId,CancellationToken token)=>
		tx.Session.QueryAsync("SELECT Id,ServiceOrderId,StockMovementId,MovementKind,InventoryId,ItemId,PartNumber,Description,Quantity,UnitPrice,Billable,TaxRate,CreatedAtUtc,CreatedByUserId FROM ServicePartEvidence WHERE ServiceOrderId=$OrderId ORDER BY Id;",ReadPart,token,Parameter("$OrderId",orderId));
	internal async Task<ServicePartEvidence> CreatePartEvidenceAsync(DatabaseTransactionContext tx,ServicePartEvidence value,CancellationToken token)
	{
		var id=await tx.Session.InsertAsync("INSERT INTO ServicePartEvidence(ServiceOrderId,StockMovementId,MovementKind,InventoryId,ItemId,PartNumber,Description,Quantity,UnitPrice,Billable,TaxRate,CreatedAtUtc,CreatedByUserId) VALUES($OrderId,$MovementId,$Kind,$InventoryId,$ItemId,$PartNumber,$Description,$Quantity,$UnitPrice,$Billable,$TaxRate,$CreatedAt,$CreatedBy);",token,
			Parameter("$OrderId",value.ServiceOrderId),Parameter("$MovementId",value.StockMovementId),Parameter("$Kind",(int)value.MovementKind),Parameter("$InventoryId",value.InventoryId),Parameter("$ItemId",value.ItemId),Parameter("$PartNumber",value.PartNumber),Parameter("$Description",value.Description),Parameter("$Quantity",value.Quantity),Parameter("$UnitPrice",value.UnitPrice),Parameter("$Billable",value.Billable?1:0),Parameter("$TaxRate",value.TaxRate),Parameter("$CreatedAt",Utc(value.CreatedAtUtc)),Parameter("$CreatedBy",value.CreatedByUserId));
		return value with{Id=id};
	}

	internal Task<bool> CustomerExistsAsync(DatabaseTransactionContext tx,long id,CancellationToken token)=>ExistsAsync(tx,"SELECT COUNT(*) FROM Customers WHERE Id=$Id AND IsActive=1;",token,Parameter("$Id",id));
	internal Task<bool> ContactBelongsToCustomerAsync(DatabaseTransactionContext tx,long contactId,long customerId,CancellationToken token)=>ExistsAsync(tx,"SELECT COUNT(*) FROM CustomerContacts WHERE Id=$ContactId AND CustomerId=$CustomerId AND IsActive=1;",token,Parameter("$ContactId",contactId),Parameter("$CustomerId",customerId));
	internal Task<bool> UserExistsAsync(DatabaseTransactionContext tx,long id,CancellationToken token)=>ExistsAsync(tx,"SELECT COUNT(*) FROM Users WHERE Id=$Id AND IsActive=1;",token,Parameter("$Id",id));
	internal Task<bool> SalesOrderBelongsToCustomerAsync(DatabaseTransactionContext tx,long id,long customerId,CancellationToken token)=>ExistsAsync(tx,"SELECT COUNT(*) FROM SalesOrders WHERE Id=$Id AND CustomerId=$CustomerId;",token,Parameter("$Id",id),Parameter("$CustomerId",customerId));
	internal Task<bool> ItemExistsAsync(DatabaseTransactionContext tx,long id,CancellationToken token)=>ExistsAsync(tx,"SELECT COUNT(*) FROM Items WHERE Id=$Id AND IsActive=1;",token,Parameter("$Id",id));
	internal async Task<long?> InventoryItemIdAsync(DatabaseTransactionContext tx,long inventoryId,CancellationToken token)
	{
		var value=await tx.Session.ExecuteScalarAsync("SELECT ItemId FROM Inventories WHERE Id=$Id;",token,Parameter("$Id",inventoryId));
		return value is null or DBNull?null:Convert.ToInt64(value,CultureInfo.InvariantCulture);
	}

	public Task<IReadOnlyList<ServiceCase>> GetOwnedOpenCasesAsync(long ownerUserId,int count,CancellationToken token)=>
		Database.QuerySliceAsync($"SELECT {CaseColumns}{CaseFrom} WHERE c.OwnerUserId=$Owner AND c.Status NOT IN ($Closed,$Cancelled) ORDER BY c.DueAtUtc,c.Priority DESC,c.Id",ReadCase,0,Math.Clamp(count,1,100),token,Parameter("$Owner",ownerUserId),Parameter("$Closed",(int)ServiceCaseStatus.Closed),Parameter("$Cancelled",(int)ServiceCaseStatus.Cancelled));
	public Task<IReadOnlyList<ServiceOrder>> GetOwnedOpenOrdersAsync(long ownerUserId,int count,CancellationToken token)=>
		Database.QuerySliceAsync($"SELECT {OrderColumns}{OrderFrom} WHERE o.AssignedOwnerUserId=$Owner AND o.Status NOT IN ($Completed,$Cancelled) ORDER BY o.PlannedEndAtUtc,o.Id",ReadOrder,0,Math.Clamp(count,1,100),token,Parameter("$Owner",ownerUserId),Parameter("$Completed",(int)ServiceOrderStatus.Completed),Parameter("$Cancelled",(int)ServiceOrderStatus.Cancelled));

	private static async Task<bool> ExistsAsync(DatabaseTransactionContext tx,string sql,CancellationToken token,params DatabaseParameter[] parameters)
	{
		var value=await tx.Session.ExecuteScalarAsync(sql,token,parameters);
		return Convert.ToInt32(value,CultureInfo.InvariantCulture)>0;
	}

	private static DatabaseParameter[] CaseParameters(ServiceCase value)=>
	[
		Parameter("$CustomerId",value.CustomerId),Parameter("$ContactId",value.CustomerContactId),Parameter("$Subject",value.Subject),Parameter("$Description",Normalize(value.Description)),
		Parameter("$Category",Normalize(value.Category)),Parameter("$Priority",(int)value.Priority),Parameter("$Status",(int)value.Status),Parameter("$Owner",value.OwnerUserId),
		Parameter("$SalesOrderId",value.SalesOrderId),Parameter("$ItemId",value.ItemId),Parameter("$CreatedAt",Utc(value.CreatedAtUtc)),Parameter("$CreatedBy",value.CreatedByUserId),
		Parameter("$UpdatedAt",Utc(value.UpdatedAtUtc)),Parameter("$UpdatedBy",value.UpdatedByUserId),Parameter("$DueAt",NullableUtc(value.DueAtUtc)),
		Parameter("$ResolvedAt",NullableUtc(value.ResolvedAtUtc)),Parameter("$ResolvedBy",value.ResolvedByUserId),Parameter("$ClosedAt",NullableUtc(value.ClosedAtUtc)),
		Parameter("$ClosedBy",value.ClosedByUserId),Parameter("$CancelledAt",NullableUtc(value.CancelledAtUtc)),Parameter("$CancelledBy",value.CancelledByUserId)
	];

	private static DatabaseParameter[] OrderParameters(ServiceOrder value)=>
	[
		Parameter("$CaseId",value.ServiceCaseId),Parameter("$CustomerId",value.CustomerId),Parameter("$Owner",value.AssignedOwnerUserId),Parameter("$ItemId",value.ServicedItemId),
		Parameter("$InventoryId",value.ServicedInventoryId),Parameter("$SerialLot",Normalize(value.SerialLotReference)),Parameter("$Status",(int)value.Status),
		Parameter("$Start",NullableUtc(value.PlannedStartAtUtc)),Parameter("$End",NullableUtc(value.PlannedEndAtUtc)),Parameter("$CompletionNotes",Normalize(value.CompletionNotes)),
		Parameter("$CompletedAt",NullableUtc(value.CompletedAtUtc)),Parameter("$CompletedBy",value.CompletedByUserId),Parameter("$InvoiceId",value.SalesInvoiceId),
		Parameter("$CreatedAt",Utc(value.CreatedAtUtc)),Parameter("$CreatedBy",value.CreatedByUserId),Parameter("$UpdatedAt",Utc(value.UpdatedAtUtc)),Parameter("$UpdatedBy",value.UpdatedByUserId)
	];

	private static DatabaseParameter[] WorkParameters(ServiceWorkLine value)=>
	[
		Parameter("$OrderId",value.ServiceOrderId),Parameter("$Description",value.Description),Parameter("$Quantity",value.Quantity),Parameter("$UnitPrice",value.UnitPrice),
		Parameter("$Billable",value.Billable?1:0),Parameter("$TaxRate",value.TaxRate),Parameter("$CreatedAt",Utc(value.CreatedAtUtc)),Parameter("$CreatedBy",value.CreatedByUserId)
	];

	private static ServiceCase ReadCase(DbDataReader r)=>new()
	{
		Id=r.GetInt64(0),Version=r.GetInt64(1),CaseNumber=r.GetString(2),CustomerId=r.GetInt64(3),CustomerName=r.GetString(4),
		CustomerContactId=r.IsDBNull(5)?null:r.GetInt64(5),ContactName=r.IsDBNull(6)?null:r.GetString(6),Subject=r.GetString(7),Description=r.IsDBNull(8)?null:r.GetString(8),
		Category=r.IsDBNull(9)?null:r.GetString(9),Priority=(ServiceCasePriority)Convert.ToInt32(r.GetValue(10),CultureInfo.InvariantCulture),Status=(ServiceCaseStatus)Convert.ToInt32(r.GetValue(11),CultureInfo.InvariantCulture),
		OwnerUserId=r.IsDBNull(12)?null:r.GetInt64(12),OwnerDisplayName=r.IsDBNull(13)?null:r.GetString(13),SalesOrderId=r.IsDBNull(14)?null:r.GetInt64(14),ItemId=r.IsDBNull(15)?null:r.GetInt64(15),
		CreatedAtUtc=ReadUtc(r,16),CreatedByUserId=r.GetInt64(17),UpdatedAtUtc=ReadUtc(r,18),UpdatedByUserId=r.GetInt64(19),DueAtUtc=ReadNullableUtc(r,20),
		ResolvedAtUtc=ReadNullableUtc(r,21),ResolvedByUserId=r.IsDBNull(22)?null:r.GetInt64(22),ClosedAtUtc=ReadNullableUtc(r,23),ClosedByUserId=r.IsDBNull(24)?null:r.GetInt64(24),
		CancelledAtUtc=ReadNullableUtc(r,25),CancelledByUserId=r.IsDBNull(26)?null:r.GetInt64(26)
	};
	private static ServiceCaseHistory ReadHistory(DbDataReader r)=>new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:(ServiceCaseStatus)Convert.ToInt32(r.GetValue(2),CultureInfo.InvariantCulture),(ServiceCaseStatus)Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),r.IsDBNull(4)?null:r.GetString(4),ReadUtc(r,5),r.GetInt64(6));
	private static ServiceOrder ReadOrder(DbDataReader r)=>new()
	{
		Id=r.GetInt64(0),Version=r.GetInt64(1),OrderNumber=r.GetString(2),ServiceCaseId=r.GetInt64(3),CaseNumber=r.GetString(4),CustomerId=r.GetInt64(5),CustomerName=r.GetString(6),
		AssignedOwnerUserId=r.IsDBNull(7)?null:r.GetInt64(7),AssignedOwnerDisplayName=r.IsDBNull(8)?null:r.GetString(8),ServicedItemId=r.IsDBNull(9)?null:r.GetInt64(9),
		ServicedPartNumber=r.IsDBNull(10)?null:r.GetString(10),ServicedInventoryId=r.IsDBNull(11)?null:r.GetInt64(11),SerialLotReference=r.IsDBNull(12)?null:r.GetString(12),
		Status=(ServiceOrderStatus)Convert.ToInt32(r.GetValue(13),CultureInfo.InvariantCulture),PlannedStartAtUtc=ReadNullableUtc(r,14),PlannedEndAtUtc=ReadNullableUtc(r,15),
		CompletionNotes=r.IsDBNull(16)?null:r.GetString(16),CompletedAtUtc=ReadNullableUtc(r,17),CompletedByUserId=r.IsDBNull(18)?null:r.GetInt64(18),SalesInvoiceId=r.IsDBNull(19)?null:r.GetInt64(19),
		SalesInvoiceNumber=r.IsDBNull(20)?null:r.GetString(20),CreatedAtUtc=ReadUtc(r,21),CreatedByUserId=r.GetInt64(22),UpdatedAtUtc=ReadUtc(r,23),UpdatedByUserId=r.GetInt64(24)
	};
	private static ServiceWorkLine ReadWorkLine(DbDataReader r)=>new(){Id=r.GetInt64(0),Version=r.GetInt64(1),ServiceOrderId=r.GetInt64(2),Description=r.GetString(3),Quantity=Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture),UnitPrice=Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture),Billable=Convert.ToBoolean(r.GetValue(6),CultureInfo.InvariantCulture),TaxRate=Convert.ToDecimal(r.GetValue(7),CultureInfo.InvariantCulture),CreatedAtUtc=ReadUtc(r,8),CreatedByUserId=r.GetInt64(9)};
	private static ServicePartEvidence ReadPart(DbDataReader r)=>new(){Id=r.GetInt64(0),ServiceOrderId=r.GetInt64(1),StockMovementId=r.GetInt64(2),MovementKind=(ServicePartMovementKind)Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),InventoryId=r.GetInt64(4),ItemId=r.GetInt64(5),PartNumber=r.GetString(6),Description=r.GetString(7),Quantity=Convert.ToInt32(r.GetValue(8),CultureInfo.InvariantCulture),UnitPrice=Convert.ToDecimal(r.GetValue(9),CultureInfo.InvariantCulture),Billable=Convert.ToBoolean(r.GetValue(10),CultureInfo.InvariantCulture),TaxRate=Convert.ToDecimal(r.GetValue(11),CultureInfo.InvariantCulture),CreatedAtUtc=ReadUtc(r,12),CreatedByUserId=r.GetInt64(13)};
	private static DateTime ReadUtc(DbDataReader r,int ordinal)=>r.GetValue(ordinal) is DateTime value?DateTime.SpecifyKind(value,DateTimeKind.Utc):DateTime.Parse(Convert.ToString(r.GetValue(ordinal),CultureInfo.InvariantCulture)!,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
	private static DateTime? ReadNullableUtc(DbDataReader r,int ordinal)=>r.IsDBNull(ordinal)?null:ReadUtc(r,ordinal);
	private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.ToUniversalTime();
	private static DateTime? NullableUtc(DateTime? value)=>value is null?null:Utc(value.Value);
	private static string? Normalize(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
