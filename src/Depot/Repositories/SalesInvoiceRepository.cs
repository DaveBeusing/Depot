// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesInvoiceRepository : DatabaseRepository
{
	private const string Columns="si.Id,si.InvoiceNumber,si.CustomerId,c.Name,si.SalesOrderId,so.OrderNumber,si.ShipmentId,sh.ShipmentNumber,si.InvoiceDate,si.DueDate,si.Currency,si.Status,si.CustomerReference,si.BillingAddress,si.Notes,si.CreatedByUserId,si.PostedByUserId,si.PostedAtUtc,si.Version";
	private const string From="FROM SalesInvoices si INNER JOIN Customers c ON c.Id=si.CustomerId INNER JOIN SalesOrders so ON so.Id=si.SalesOrderId INNER JOIN Shipments sh ON sh.Id=si.ShipmentId";
	public SalesInvoiceRepository(DatabaseAccess database):base(database){}

	public Task<PageResult<SalesInvoice>> SearchAsync(string? searchText,SalesInvoiceStatus? status,int pageNumber,int pageSize,CancellationToken token)
	{
		var filters=new List<string>();var parameters=new List<DatabaseParameter>();
		if(!string.IsNullOrWhiteSpace(searchText)){filters.Add("(si.InvoiceNumber LIKE $Search OR so.OrderNumber LIKE $Search OR sh.ShipmentNumber LIKE $Search OR c.Name LIKE $Search)");parameters.Add(Parameter("$Search",$"%{searchText.Trim()}%"));}
		if(status is not null){filters.Add("si.Status=$Status");parameters.Add(Parameter("$Status",(int)status.Value));}
		var where=filters.Count==0?string.Empty:$"WHERE {string.Join(" AND ",filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} {From} {where} ORDER BY si.InvoiceDate DESC,si.Id DESC",$"SELECT COUNT(*) {From} {where}",Read,pageNumber,pageSize,token,parameters.ToArray());
	}

	public async Task<SalesInvoice?> GetByIdAsync(long id,CancellationToken token)
	{
		var invoice=await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE si.Id=$Id;",Read,token,Parameter("$Id",id));
		if(invoice is null)return null;invoice.Lines=await ListLinesAsync(id,token);return invoice;
	}
	public async Task<SalesInvoice?> GetByIdAsync(DatabaseTransactionContext tx,long id,CancellationToken token)
	{
		var rows=await tx.Session.QueryAsync($"SELECT {Columns} {From} WHERE si.Id=$Id;",Read,token,Parameter("$Id",id));if(rows.Count==0)return null;var invoice=rows[0];invoice.Lines=await tx.Session.QueryAsync(LineSql+" WHERE sil.SalesInvoiceId=$Id ORDER BY sil.LineNumber;",ReadLine,token,Parameter("$Id",id));return invoice;
	}
	public Task<IReadOnlyList<SalesInvoiceLine>> ListLinesAsync(long id,CancellationToken token)=>Database.QueryAsync(LineSql+" WHERE sil.SalesInvoiceId=$Id ORDER BY sil.LineNumber;",ReadLine,token,Parameter("$Id",id));
	public Task<SalesInvoice?> GetByShipmentIdAsync(long shipmentId,CancellationToken token)=>Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE si.ShipmentId=$ShipmentId;",Read,token,Parameter("$ShipmentId",shipmentId));
	public async Task<SalesInvoice?> GetByShipmentIdAsync(DatabaseTransactionContext tx,long shipmentId,CancellationToken token)
	{
		var rows=await tx.Session.QueryAsync($"SELECT {Columns} {From} WHERE si.ShipmentId=$ShipmentId;",Read,token,Parameter("$ShipmentId",shipmentId));
		if(rows.Count==0)return null;var invoice=rows[0];invoice.Lines=await tx.Session.QueryAsync(LineSql+" WHERE sil.SalesInvoiceId=$Id ORDER BY sil.LineNumber;",ReadLine,token,Parameter("$Id",invoice.Id));return invoice;
	}

	public async Task<long> CreateAsync(DatabaseTransactionContext tx,SalesInvoice invoice,CancellationToken token)
	{
		invoice.InvoiceNumber=$"PENDING-{Guid.NewGuid():N}";
		invoice.Id=await tx.Session.InsertAsync("INSERT INTO SalesInvoices (InvoiceNumber,CustomerId,SalesOrderId,ShipmentId,InvoiceDate,DueDate,Currency,Status,CustomerReference,BillingAddress,Notes,CreatedByUserId) VALUES ($Number,$CustomerId,$OrderId,$ShipmentId,$InvoiceDate,$DueDate,$Currency,$Status,$Reference,$Address,$Notes,$UserId);",token,Parameters(invoice));
		invoice.InvoiceNumber=$"INV-{invoice.Id:000000}";
		await tx.Session.ExecuteAsync("UPDATE SalesInvoices SET InvoiceNumber=$Number WHERE Id=$Id;",token,Parameter("$Number",invoice.InvoiceNumber),Parameter("$Id",invoice.Id));
		var lineNumber=1;
		foreach(var line in invoice.Lines)
		{
			line.SalesInvoiceId=invoice.Id;line.LineNumber=lineNumber++;
			line.Id=await tx.Session.InsertAsync("INSERT INTO SalesInvoiceLines (SalesInvoiceId,LineNumber,SalesOrderLineId,ShipmentLineId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate) VALUES ($InvoiceId,$LineNumber,$OrderLineId,$ShipmentLineId,$PartNumber,$Description,$Quantity,$UnitPrice,$Discount,$TaxRate);",token,Parameter("$InvoiceId",invoice.Id),Parameter("$LineNumber",line.LineNumber),Parameter("$OrderLineId",line.SalesOrderLineId),Parameter("$ShipmentLineId",line.ShipmentLineId),Parameter("$PartNumber",line.PartNumber),Parameter("$Description",line.Description),Parameter("$Quantity",line.Quantity),Parameter("$UnitPrice",line.UnitPrice),Parameter("$Discount",line.DiscountPercent),Parameter("$TaxRate",line.TaxRate));
		}
		return invoice.Id;
	}
	public async Task<bool> PostAsync(DatabaseTransactionContext tx,long id,long version,long userId,DateTime at,CancellationToken token)=>await tx.Session.ExecuteAsync("UPDATE SalesInvoices SET Status=$Posted,PostedByUserId=$UserId,PostedAtUtc=$At,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;",token,Parameter("$Posted",(int)SalesInvoiceStatus.Posted),Parameter("$UserId",userId),Parameter("$At",at.ToString("O",CultureInfo.InvariantCulture)),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Draft",(int)SalesInvoiceStatus.Draft))==1;
	public async Task<bool> CancelDraftAsync(DatabaseTransactionContext tx,long id,long version,CancellationToken token)=>await tx.Session.ExecuteAsync("UPDATE SalesInvoices SET Status=$Cancelled,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;",token,Parameter("$Cancelled",(int)SalesInvoiceStatus.Cancelled),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Draft",(int)SalesInvoiceStatus.Draft))==1;

	private static DatabaseParameter[] Parameters(SalesInvoice i)=>[new("$Number",i.InvoiceNumber),new("$CustomerId",i.CustomerId),new("$OrderId",i.SalesOrderId),new("$ShipmentId",i.ShipmentId),new("$InvoiceDate",i.InvoiceDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$DueDate",i.DueDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$Currency",i.Currency),new("$Status",(int)i.Status),new("$Reference",i.CustomerReference),new("$Address",i.BillingAddress),new("$Notes",i.Notes),new("$UserId",i.CreatedByUserId)];
	private const string LineSql="SELECT sil.Id,sil.SalesInvoiceId,sil.LineNumber,sil.SalesOrderLineId,sil.ShipmentLineId,sil.PartNumber,sil.Description,sil.Quantity,sil.UnitPrice,sil.DiscountPercent,sil.TaxRate,sil.Version FROM SalesInvoiceLines sil";
	private static SalesInvoice Read(DbDataReader r)=>new(){Id=r.GetInt64(0),InvoiceNumber=r.GetString(1),CustomerId=r.GetInt64(2),CustomerName=r.GetString(3),SalesOrderId=r.GetInt64(4),SalesOrderNumber=r.GetString(5),ShipmentId=r.GetInt64(6),ShipmentNumber=r.GetString(7),InvoiceDate=Convert.ToDateTime(r.GetValue(8),CultureInfo.InvariantCulture),DueDate=Convert.ToDateTime(r.GetValue(9),CultureInfo.InvariantCulture),Currency=r.GetString(10),Status=(SalesInvoiceStatus)r.GetInt32(11),CustomerReference=r.IsDBNull(12)?null:r.GetString(12),BillingAddress=r.IsDBNull(13)?null:r.GetString(13),Notes=r.IsDBNull(14)?null:r.GetString(14),CreatedByUserId=r.GetInt64(15),PostedByUserId=r.IsDBNull(16)?null:r.GetInt64(16),PostedAtUtc=r.IsDBNull(17)?null:DateTime.Parse(r.GetString(17),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),Version=r.GetInt64(18)};
	private static SalesInvoiceLine ReadLine(DbDataReader r)=>new(){Id=r.GetInt64(0),SalesInvoiceId=r.GetInt64(1),LineNumber=r.GetInt32(2),SalesOrderLineId=r.GetInt64(3),ShipmentLineId=r.GetInt64(4),PartNumber=r.GetString(5),Description=r.GetString(6),Quantity=r.GetInt32(7),UnitPrice=r.GetDecimal(8),DiscountPercent=r.GetDecimal(9),TaxRate=r.GetDecimal(10),Version=r.GetInt64(11)};
}
