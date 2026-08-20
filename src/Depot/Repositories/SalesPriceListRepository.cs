// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesPriceListRepository : DatabaseRepository
{
	public SalesPriceListRepository(DatabaseAccess database) : base(database) { }

	public async Task<IReadOnlyList<SalesPriceList>> ListAsync(CancellationToken token)
	{
		var lists = await Database.QueryAsync("SELECT Id,Code,Name,Currency,ValidFrom,ValidTo,IsActive,Version FROM SalesPriceLists ORDER BY Name,Code;", ReadList, token);
		foreach (var list in lists) list.Items = await ListItemsAsync(list.Id, token);
		return lists;
	}

	public async Task<SalesPriceList?> GetByIdAsync(long id, CancellationToken token)
	{
		var list = await Database.QuerySingleOrDefaultAsync("SELECT Id,Code,Name,Currency,ValidFrom,ValidTo,IsActive,Version FROM SalesPriceLists WHERE Id=$Id;", ReadList, token, Parameter("$Id", id));
		if (list is not null) list.Items = await ListItemsAsync(id, token);
		return list;
	}

	public Task<IReadOnlyList<SalesPriceListItem>> ListItemsAsync(long listId, CancellationToken token) => Database.QueryAsync(
		"SELECT pli.Id,pli.SalesPriceListId,pli.ItemId,i.PartNumber,i.Description,pli.UnitPrice,pli.DiscountPercent,pli.Version FROM SalesPriceListItems pli INNER JOIN Items i ON i.Id=pli.ItemId WHERE pli.SalesPriceListId=$ListId ORDER BY i.PartNumber;",
		ReadItem, token, Parameter("$ListId", listId));

	public async Task<SalesPriceList> SaveAsync(SalesPriceList value, CancellationToken token)
	{
		if (value.Id == 0)
		{
			value.Id = await Database.InsertAsync("INSERT INTO SalesPriceLists (Code,Name,Currency,ValidFrom,ValidTo,IsActive) VALUES ($Code,$Name,$Currency,$From,$To,$Active);", token, Params(value));
			return value;
		}
		var updated = await Database.ExecuteAsync("UPDATE SalesPriceLists SET Code=$Code,Name=$Name,Currency=$Currency,ValidFrom=$From,ValidTo=$To,IsActive=$Active,Version=Version+1 WHERE Id=$Id AND Version=$Version;", token, Params(value).Concat([Parameter("$Id", value.Id), Parameter("$Version", value.Version)]).ToArray());
		if (updated != 1) throw new Services.ConcurrencyConflictException("sales price list");
		value.Version++;
		return value;
	}

	public async Task<SalesPriceListItem> SaveItemAsync(SalesPriceListItem item, CancellationToken token)
	{
		if (item.Id == 0)
		{
			item.Id = await Database.InsertAsync("INSERT INTO SalesPriceListItems (SalesPriceListId,ItemId,UnitPrice,DiscountPercent) VALUES ($ListId,$ItemId,$Price,$Discount);", token,
				Parameter("$ListId", item.SalesPriceListId), Parameter("$ItemId", item.ItemId), Parameter("$Price", item.UnitPrice), Parameter("$Discount", item.DiscountPercent));
			return item;
		}
		var updated = await Database.ExecuteAsync("UPDATE SalesPriceListItems SET UnitPrice=$Price,DiscountPercent=$Discount,Version=Version+1 WHERE Id=$Id AND Version=$Version;", token,
			Parameter("$Price", item.UnitPrice), Parameter("$Discount", item.DiscountPercent), Parameter("$Id", item.Id), Parameter("$Version", item.Version));
		if (updated != 1) throw new Services.ConcurrencyConflictException("sales price list item");
		item.Version++;
		return item;
	}

	public async Task AssignCustomerAsync(long customerId, long? listId, CancellationToken token)
	{
		await Database.ExecuteAsync("DELETE FROM CustomerPriceLists WHERE CustomerId=$CustomerId;", token, Parameter("$CustomerId", customerId));
		if (listId is > 0) await Database.ExecuteAsync("INSERT INTO CustomerPriceLists (CustomerId,SalesPriceListId) VALUES ($CustomerId,$ListId);", token, Parameter("$CustomerId", customerId), Parameter("$ListId", listId.Value));
	}

	public Task<CustomerPriceListAssignment?> GetCustomerAssignmentAsync(long customerId, CancellationToken token) => Database.QuerySingleOrDefaultAsync(
		"SELECT cpl.CustomerId,cpl.SalesPriceListId,pl.Name FROM CustomerPriceLists cpl INNER JOIN SalesPriceLists pl ON pl.Id=cpl.SalesPriceListId WHERE cpl.CustomerId=$CustomerId;",
		r => new CustomerPriceListAssignment { CustomerId=r.GetInt64(0), SalesPriceListId=r.GetInt64(1), PriceListName=r.GetString(2) }, token, Parameter("$CustomerId", customerId));

	public Task<SalesPriceResult?> ResolveAsync(long customerId, long itemId, DateTime date, CancellationToken token) => Database.QuerySingleOrDefaultAsync(
		"SELECT pli.UnitPrice,pli.DiscountPercent,pl.Name FROM CustomerPriceLists cpl INNER JOIN SalesPriceLists pl ON pl.Id=cpl.SalesPriceListId INNER JOIN SalesPriceListItems pli ON pli.SalesPriceListId=pl.Id WHERE cpl.CustomerId=$CustomerId AND pli.ItemId=$ItemId AND pl.IsActive=1 AND (pl.ValidFrom IS NULL OR pl.ValidFrom<=$Date) AND (pl.ValidTo IS NULL OR pl.ValidTo>=$Date);",
		r => new SalesPriceResult(Convert.ToDecimal(r.GetValue(0),CultureInfo.InvariantCulture), Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture), r.GetString(2)), token,
		Parameter("$CustomerId", customerId), Parameter("$ItemId", itemId), Parameter("$Date", date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));

	private static DatabaseParameter[] Params(SalesPriceList value) => [new("$Code",value.Code),new("$Name",value.Name),new("$Currency",value.Currency),new("$From",value.ValidFrom?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$To",value.ValidTo?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$Active",value.IsActive)];
	private static SalesPriceList ReadList(DbDataReader r) => new(){Id=r.GetInt64(0),Code=r.GetString(1),Name=r.GetString(2),Currency=r.GetString(3),ValidFrom=r.IsDBNull(4)?null:Convert.ToDateTime(r.GetValue(4),CultureInfo.InvariantCulture),ValidTo=r.IsDBNull(5)?null:Convert.ToDateTime(r.GetValue(5),CultureInfo.InvariantCulture),IsActive=r.GetBoolean(6),Version=r.GetInt64(7)};
	private static SalesPriceListItem ReadItem(DbDataReader r) => new(){Id=r.GetInt64(0),SalesPriceListId=r.GetInt64(1),ItemId=r.GetInt64(2),PartNumber=r.GetString(3),Description=r.GetString(4),UnitPrice=Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture),DiscountPercent=Convert.ToDecimal(r.GetValue(6),CultureInfo.InvariantCulture),Version=r.GetInt64(7)};
}
