// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesQuoteRepository : DatabaseRepository
{
	private const string Columns = "q.Id,q.QuoteNumber,q.CustomerId,c.Name,q.BillingAddress,q.ShippingAddress,q.ContactId,q.ContactName,q.QuoteDate,q.ValidUntil,q.Currency,q.CustomerReference,q.Notes,q.Status,q.CreatedByUserId,q.CreatedAtUtc,q.ConvertedSalesOrderId,q.ConvertedAtUtc,q.Version";
	private const string From = "FROM SalesQuotes q INNER JOIN Customers c ON c.Id=q.CustomerId";
	public SalesQuoteRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<SalesQuote>> SearchAsync(string? searchText, SalesQuoteStatus? status, int pageNumber, int pageSize, CancellationToken token)
	{
		var filters=new List<string>(); var parameters=new List<DatabaseParameter>();
		if(!string.IsNullOrWhiteSpace(searchText)){filters.Add("(q.QuoteNumber LIKE $Search OR c.Name LIKE $Search OR q.CustomerReference LIKE $Search)");parameters.Add(Parameter("$Search",$"%{searchText.Trim()}%"));}
		if(status is not null){filters.Add("q.Status=$Status");parameters.Add(Parameter("$Status",(int)status.Value));}
		var where=filters.Count==0?string.Empty:$"WHERE {string.Join(" AND ",filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} {From} {where} ORDER BY q.QuoteDate DESC,q.Id DESC",$"SELECT COUNT(*) {From} {where}",Read,pageNumber,pageSize,token,parameters.ToArray());
	}

	public async Task<SalesQuote?> GetByIdAsync(long id,CancellationToken token)
	{
		var value=await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE q.Id=$Id;",Read,token,Parameter("$Id",id));
		if(value is not null)value.Lines=await ListLinesAsync(id,token); return value;
	}

	public Task<IReadOnlyList<SalesQuoteLine>> ListLinesAsync(long id,CancellationToken token)=>Database.QueryAsync("SELECT Id,SalesQuoteId,LineNumber,ItemId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate,Version FROM SalesQuoteLines WHERE SalesQuoteId=$Id ORDER BY LineNumber,Id;",ReadLine,token,Parameter("$Id",id));

	public async Task<SalesQuote> SaveDraftAsync(SalesQuote quote,CancellationToken token)
	{
		if(quote.Id==0)
		{
			quote.QuoteNumber=$"PENDING-{Guid.NewGuid():N}";
			quote.Id=await Database.InsertAsync("INSERT INTO SalesQuotes (QuoteNumber,CustomerId,BillingAddress,ShippingAddress,ContactId,ContactName,QuoteDate,ValidUntil,Currency,CustomerReference,Notes,Status,CreatedByUserId,CreatedAtUtc) VALUES ($Number,$CustomerId,$Billing,$Shipping,$ContactId,$ContactName,$Date,$ValidUntil,$Currency,$Reference,$Notes,$Status,$UserId,$CreatedAt);",token,Params(quote));
			quote.QuoteNumber=$"QU-{quote.Id:000000}";
			await Database.ExecuteAsync("UPDATE SalesQuotes SET QuoteNumber=$Number WHERE Id=$Id;",token,Parameter("$Number",quote.QuoteNumber),Parameter("$Id",quote.Id));
		}
		else
		{
			var updated=await Database.ExecuteAsync("UPDATE SalesQuotes SET CustomerId=$CustomerId,BillingAddress=$Billing,ShippingAddress=$Shipping,ContactId=$ContactId,ContactName=$ContactName,QuoteDate=$Date,ValidUntil=$ValidUntil,Currency=$Currency,CustomerReference=$Reference,Notes=$Notes,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;",token,Params(quote).Concat([Parameter("$Id",quote.Id),Parameter("$Version",quote.Version),Parameter("$Draft",(int)SalesQuoteStatus.Draft)]).ToArray());
			if(updated!=1)throw new Services.ConcurrencyConflictException("sales quote"); quote.Version++;
			await Database.ExecuteAsync("DELETE FROM SalesQuoteLines WHERE SalesQuoteId=$Id;",token,Parameter("$Id",quote.Id));
		}
		var lineNo=1;
		foreach(var line in quote.Lines)
		{
			line.SalesQuoteId=quote.Id; line.LineNumber=lineNo++;
			line.Id=await Database.InsertAsync("INSERT INTO SalesQuoteLines (SalesQuoteId,LineNumber,ItemId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate) VALUES ($QuoteId,$LineNumber,$ItemId,$PartNumber,$Description,$Quantity,$UnitPrice,$Discount,$Tax);",token,
				Parameter("$QuoteId",quote.Id),Parameter("$LineNumber",line.LineNumber),Parameter("$ItemId",line.ItemId),Parameter("$PartNumber",line.PartNumber),Parameter("$Description",line.Description),Parameter("$Quantity",line.Quantity),Parameter("$UnitPrice",line.UnitPrice),Parameter("$Discount",line.DiscountPercent),Parameter("$Tax",line.TaxRate));
		}
		return await GetByIdAsync(quote.Id,token)??quote;
	}

	public async Task<bool> SetStatusAsync(long id,long version,SalesQuoteStatus expected,SalesQuoteStatus target,CancellationToken token)=>await Database.ExecuteAsync("UPDATE SalesQuotes SET Status=$Target,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Expected;",token,Parameter("$Target",(int)target),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Expected",(int)expected))==1;

	public async Task<bool> MarkConvertedAsync(long id,long version,long orderId,DateTime atUtc,CancellationToken token)=>await Database.ExecuteAsync("UPDATE SalesQuotes SET Status=$Converted,ConvertedSalesOrderId=$OrderId,ConvertedAtUtc=$At,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status IN ($Accepted,$Sent);",token,Parameter("$Converted",(int)SalesQuoteStatus.Converted),Parameter("$OrderId",orderId),Parameter("$At",atUtc.ToString("O",CultureInfo.InvariantCulture)),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Accepted",(int)SalesQuoteStatus.Accepted),Parameter("$Sent",(int)SalesQuoteStatus.Sent))==1;

	private static DatabaseParameter[] Params(SalesQuote q)=>[new("$Number",q.QuoteNumber),new("$CustomerId",q.CustomerId),new("$Billing",q.BillingAddress),new("$Shipping",q.ShippingAddress),new("$ContactId",q.ContactId),new("$ContactName",q.ContactName),new("$Date",q.QuoteDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$ValidUntil",q.ValidUntil.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$Currency",q.Currency),new("$Reference",q.CustomerReference),new("$Notes",q.Notes),new("$Status",(int)q.Status),new("$UserId",q.CreatedByUserId),new("$CreatedAt",q.CreatedAtUtc.ToString("O",CultureInfo.InvariantCulture))];
	private static SalesQuote Read(DbDataReader r)=>new(){Id=r.GetInt64(0),QuoteNumber=r.GetString(1),CustomerId=r.GetInt64(2),CustomerName=r.GetString(3),BillingAddress=r.IsDBNull(4)?null:r.GetString(4),ShippingAddress=r.IsDBNull(5)?null:r.GetString(5),ContactId=r.IsDBNull(6)?null:r.GetInt64(6),ContactName=r.IsDBNull(7)?null:r.GetString(7),QuoteDate=Convert.ToDateTime(r.GetValue(8),CultureInfo.InvariantCulture),ValidUntil=Convert.ToDateTime(r.GetValue(9),CultureInfo.InvariantCulture),Currency=r.GetString(10),CustomerReference=r.IsDBNull(11)?null:r.GetString(11),Notes=r.IsDBNull(12)?null:r.GetString(12),Status=(SalesQuoteStatus)r.GetInt32(13),CreatedByUserId=r.GetInt64(14),CreatedAtUtc=DateTime.Parse(r.GetString(15),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),ConvertedSalesOrderId=r.IsDBNull(16)?null:r.GetInt64(16),ConvertedAtUtc=r.IsDBNull(17)?null:DateTime.Parse(r.GetString(17),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),Version=r.GetInt64(18)};
	private static SalesQuoteLine ReadLine(DbDataReader r)=>new(){Id=r.GetInt64(0),SalesQuoteId=r.GetInt64(1),LineNumber=r.GetInt32(2),ItemId=r.GetInt64(3),PartNumber=r.GetString(4),Description=r.GetString(5),Quantity=r.GetInt32(6),UnitPrice=Convert.ToDecimal(r.GetValue(7),CultureInfo.InvariantCulture),DiscountPercent=Convert.ToDecimal(r.GetValue(8),CultureInfo.InvariantCulture),TaxRate=Convert.ToDecimal(r.GetValue(9),CultureInfo.InvariantCulture),Version=r.GetInt64(10)};
}
