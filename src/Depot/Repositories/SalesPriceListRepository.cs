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
		var lists = await Database.QueryAsync($"{ListSelect} ORDER BY pl.Name,pl.Code;", ReadList, token);
		foreach (var list in lists) list.Items = await ListItemsAsync(list.Id, token);
		return lists;
	}

	public async Task<SalesPriceList?> GetByIdAsync(long id, CancellationToken token)
	{
		var list = await Database.QuerySingleOrDefaultAsync($"{ListSelect} WHERE pl.Id=$Id;", ReadList, token, Parameter("$Id", id));
		if (list is not null) list.Items = await ListItemsAsync(id, token);
		return list;
	}

	public async Task<SalesPriceList?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken token)
	{
		var list = await transaction.Session.QuerySingleOrDefaultAsync($"{ListSelect} WHERE pl.Id=$Id;", ReadList, token, Parameter("$Id", id));
		if (list is not null) list.Items = await ListItemsAsync(transaction, id, token);
		return list;
	}

	public Task<IReadOnlyList<SalesPriceListItem>> ListItemsAsync(long listId, CancellationToken token) => Database.QueryAsync(
		"SELECT pli.Id,pli.SalesPriceListId,pli.ItemId,i.PartNumber,i.Description,pli.UnitPrice,pli.DiscountPercent,pli.Version FROM SalesPriceListItems pli INNER JOIN Items i ON i.Id=pli.ItemId WHERE pli.SalesPriceListId=$ListId ORDER BY i.PartNumber;",
		ReadItem, token, Parameter("$ListId", listId));
	public Task<IReadOnlyList<SalesPriceListItem>> ListItemsAsync(DatabaseTransactionContext transaction, long listId, CancellationToken token) => transaction.Session.QueryAsync(
		"SELECT pli.Id,pli.SalesPriceListId,pli.ItemId,i.PartNumber,i.Description,pli.UnitPrice,pli.DiscountPercent,pli.Version FROM SalesPriceListItems pli INNER JOIN Items i ON i.Id=pli.ItemId WHERE pli.SalesPriceListId=$ListId ORDER BY i.PartNumber;",
		ReadItem, token, Parameter("$ListId", listId));

	public Task<SalesPriceListItem?> GetItemAsync(DatabaseTransactionContext transaction, long listId, long itemId, CancellationToken token) => transaction.Session.QuerySingleOrDefaultAsync(
		"SELECT pli.Id,pli.SalesPriceListId,pli.ItemId,i.PartNumber,i.Description,pli.UnitPrice,pli.DiscountPercent,pli.Version FROM SalesPriceListItems pli INNER JOIN Items i ON i.Id=pli.ItemId WHERE pli.SalesPriceListId=$ListId AND pli.ItemId=$ItemId;",
		ReadItem, token, Parameter("$ListId", listId), Parameter("$ItemId", itemId));

	public async Task<SalesPriceList> SaveAsync(SalesPriceList value, CancellationToken token)
		=> await Database.ExecuteInWriteTransactionAsync((session, cancellationToken) => SaveAsync(new DatabaseTransactionContext(session), value, cancellationToken), token);

	public async Task<SalesPriceList> SaveAsync(DatabaseTransactionContext transaction, SalesPriceList value, CancellationToken token)
	{
		var session = transaction.Session;
		if (value.Id == 0)
		{
			value.Id = await session.InsertAsync("INSERT INTO SalesPriceLists (Code,Name,Scope,RegionId,Currency,ValidFrom,ValidTo,IsActive) VALUES ($Code,$Name,$Scope,$RegionId,$Currency,$From,$To,$Active);", token, Params(value));
			return value;
		}
		var updated = await session.ExecuteAsync("UPDATE SalesPriceLists SET Code=$Code,Name=$Name,Scope=$Scope,RegionId=$RegionId,Currency=$Currency,ValidFrom=$From,ValidTo=$To,IsActive=$Active,Version=Version+1 WHERE Id=$Id AND Version=$Version;", token, Params(value).Concat([Parameter("$Id", value.Id), Parameter("$Version", value.Version)]).ToArray());
		if (updated != 1) throw new Services.ConcurrencyConflictException("sales price list");
		value.Version++;
		return value;
	}

	public async Task<SalesPriceListItem> SaveItemAsync(SalesPriceListItem item, CancellationToken token)
		=> await Database.ExecuteInWriteTransactionAsync((session, cancellationToken) => SaveItemAsync(new DatabaseTransactionContext(session), item, cancellationToken), token);

	public async Task<SalesPriceListItem> SaveItemAsync(DatabaseTransactionContext transaction, SalesPriceListItem item, CancellationToken token)
	{
		var session = transaction.Session;
		if (item.Id == 0)
		{
			item.Id = await session.InsertAsync("INSERT INTO SalesPriceListItems (SalesPriceListId,ItemId,UnitPrice,DiscountPercent) VALUES ($ListId,$ItemId,$Price,$Discount);", token,
				Parameter("$ListId", item.SalesPriceListId), Parameter("$ItemId", item.ItemId), Parameter("$Price", item.UnitPrice), Parameter("$Discount", item.DiscountPercent));
			return item;
		}
		var updated = await session.ExecuteAsync("UPDATE SalesPriceListItems SET UnitPrice=$Price,DiscountPercent=$Discount,Version=Version+1 WHERE Id=$Id AND Version=$Version;", token,
			Parameter("$Price", item.UnitPrice), Parameter("$Discount", item.DiscountPercent), Parameter("$Id", item.Id), Parameter("$Version", item.Version));
		if (updated != 1) throw new Services.ConcurrencyConflictException("sales price list item");
		item.Version++;
		return item;
	}

	public async Task AssignCustomerAsync(long customerId, long? listId, CancellationToken token)
		=> await Database.ExecuteInWriteTransactionAsync(async (session, cancellationToken) => { await AssignCustomerAsync(new DatabaseTransactionContext(session), customerId, listId, cancellationToken); return true; }, token);

	public async Task AssignCustomerAsync(DatabaseTransactionContext transaction, long customerId, long? listId, CancellationToken token)
	{
		await transaction.Session.ExecuteAsync("DELETE FROM CustomerPriceLists WHERE CustomerId=$CustomerId;", token, Parameter("$CustomerId", customerId));
		if (listId is > 0) await transaction.Session.ExecuteAsync("INSERT INTO CustomerPriceLists (CustomerId,SalesPriceListId) VALUES ($CustomerId,$ListId);", token, Parameter("$CustomerId", customerId), Parameter("$ListId", listId.Value));
	}

	public Task<CustomerPriceListAssignment?> GetCustomerAssignmentAsync(long customerId, CancellationToken token) => Database.QuerySingleOrDefaultAsync(
		"SELECT cpl.CustomerId,cpl.SalesPriceListId,pl.Name,pl.IsActive FROM CustomerPriceLists cpl INNER JOIN SalesPriceLists pl ON pl.Id=cpl.SalesPriceListId WHERE cpl.CustomerId=$CustomerId;",
		r => new CustomerPriceListAssignment { CustomerId=r.GetInt64(0), SalesPriceListId=r.GetInt64(1), PriceListName=r.GetString(2), IsActive=r.GetBoolean(3) }, token, Parameter("$CustomerId", customerId));
	public Task<CustomerPriceListAssignment?> GetCustomerAssignmentAsync(DatabaseTransactionContext transaction, long customerId, CancellationToken token) => transaction.Session.QuerySingleOrDefaultAsync(
		"SELECT cpl.CustomerId,cpl.SalesPriceListId,pl.Name,pl.IsActive FROM CustomerPriceLists cpl INNER JOIN SalesPriceLists pl ON pl.Id=cpl.SalesPriceListId WHERE cpl.CustomerId=$CustomerId;",
		r => new CustomerPriceListAssignment { CustomerId=r.GetInt64(0), SalesPriceListId=r.GetInt64(1), PriceListName=r.GetString(2), IsActive=r.GetBoolean(3) }, token, Parameter("$CustomerId", customerId));

	public Task<SalesPriceList?> FindActiveDefaultAsync(DatabaseTransactionContext transaction, SalesPriceListScope scope, long? regionId, long excludedId, CancellationToken token) => transaction.Session.QuerySingleOrDefaultAsync(
		$"{ListSelect} WHERE pl.Scope=$Scope AND pl.IsActive=1 AND (($RegionId IS NULL AND pl.RegionId IS NULL) OR pl.RegionId=$RegionId) AND pl.Id<>$ExcludedId ORDER BY pl.Id;",
		ReadList, token, Parameter("$Scope", (int)scope), Parameter("$RegionId", regionId), Parameter("$ExcludedId", excludedId));
	public async Task<bool> HasCustomerAssignmentsAsync(DatabaseTransactionContext transaction, long listId, CancellationToken token) => Convert.ToInt64(
		await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM CustomerPriceLists WHERE SalesPriceListId=$ListId;", token, Parameter("$ListId", listId)), CultureInfo.InvariantCulture) > 0;

	public Task<IReadOnlyList<SalesRegion>> ListRegionsAsync(CancellationToken token) => Database.QueryAsync(
		"SELECT Id,Code,Name,IsActive,Version FROM SalesRegions ORDER BY Name,Code;", ReadRegion, token);
	public Task<SalesRegion?> GetRegionAsync(DatabaseTransactionContext transaction, long id, CancellationToken token) => transaction.Session.QuerySingleOrDefaultAsync(
		"SELECT Id,Code,Name,IsActive,Version FROM SalesRegions WHERE Id=$Id;", ReadRegion, token, Parameter("$Id", id));
	public async Task<SalesRegion> SaveRegionAsync(DatabaseTransactionContext transaction, SalesRegion value, CancellationToken token)
	{
		if (value.Id == 0)
		{
			value.Id = await transaction.Session.InsertAsync("INSERT INTO SalesRegions (Code,Name,IsActive) VALUES ($Code,$Name,$Active);", token, RegionParams(value));
			return value;
		}
		var updated = await transaction.Session.ExecuteAsync("UPDATE SalesRegions SET Code=$Code,Name=$Name,IsActive=$Active,Version=Version+1 WHERE Id=$Id AND Version=$Version;", token, RegionParams(value).Concat([Parameter("$Id", value.Id), Parameter("$Version", value.Version)]).ToArray());
		if (updated != 1) throw new Services.ConcurrencyConflictException("sales region");
		value.Version++;
		return value;
	}

	internal Task<CustomerPricingContext?> GetCustomerPricingContextAsync(long customerId, CancellationToken token) => Database.QuerySingleOrDefaultAsync(
		"SELECT Id,SalesRegionId,Currency,IsActive FROM Customers WHERE Id=$CustomerId;",
		r => new CustomerPricingContext(r.GetInt64(0), r.IsDBNull(1)?null:r.GetInt64(1), r.GetString(2), r.GetBoolean(3)), token, Parameter("$CustomerId", customerId));

	public async Task<SalesPriceResult?> ResolveAsync(long customerId, long itemId, DateTime date, string currency, CancellationToken token)
	{
		var candidates = await Database.QueryAsync(
			"SELECT pli.UnitPrice,pli.DiscountPercent,pl.Id,pl.Name,pl.Scope,pl.Currency,pl.RegionId FROM Customers c INNER JOIN Items i ON i.Id=$ItemId INNER JOIN SalesPriceListItems pli ON pli.ItemId=i.Id INNER JOIN SalesPriceLists pl ON pl.Id=pli.SalesPriceListId LEFT JOIN SalesRegions sr ON sr.Id=pl.RegionId LEFT JOIN CustomerPriceLists cpl ON cpl.CustomerId=c.Id AND cpl.SalesPriceListId=pl.Id WHERE c.Id=$CustomerId AND c.IsActive=1 AND i.IsActive=1 AND pl.IsActive=1 AND pl.Currency=$Currency AND (pl.ValidFrom IS NULL OR pl.ValidFrom<=$Date) AND (pl.ValidTo IS NULL OR pl.ValidTo>=$Date) AND ((pl.Scope=$CustomerScope AND cpl.CustomerId IS NOT NULL) OR (pl.Scope=$RegionScope AND sr.IsActive=1 AND c.SalesRegionId IS NOT NULL AND pl.RegionId=c.SalesRegionId) OR pl.Scope=$GlobalScope) ORDER BY CASE pl.Scope WHEN $CustomerScope THEN 0 WHEN $RegionScope THEN 1 ELSE 2 END,pl.Id;",
			ReadPrice, token, Parameter("$CustomerId", customerId), Parameter("$ItemId", itemId), Parameter("$Currency", currency), Parameter("$Date", date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)), Parameter("$CustomerScope", (int)SalesPriceListScope.Customer), Parameter("$RegionScope", (int)SalesPriceListScope.Region), Parameter("$GlobalScope", (int)SalesPriceListScope.Global));
		return candidates.FirstOrDefault();
	}

	private const string ListSelect = "SELECT pl.Id,pl.Code,pl.Name,pl.Scope,pl.RegionId,r.Name,pl.Currency,pl.ValidFrom,pl.ValidTo,pl.IsActive,pl.Version FROM SalesPriceLists pl LEFT JOIN SalesRegions r ON r.Id=pl.RegionId";
	private static DatabaseParameter[] Params(SalesPriceList value) => [new("$Code",value.Code),new("$Name",value.Name),new("$Scope",(int)value.Scope),new("$RegionId",value.RegionId),new("$Currency",value.Currency),new("$From",value.ValidFrom?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$To",value.ValidTo?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$Active",value.IsActive)];
	private static DatabaseParameter[] RegionParams(SalesRegion value) => [new("$Code",value.Code),new("$Name",value.Name),new("$Active",value.IsActive)];
	private static SalesPriceList ReadList(DbDataReader r) => new(){Id=r.GetInt64(0),Code=r.GetString(1),Name=r.GetString(2),Scope=(SalesPriceListScope)r.GetInt32(3),RegionId=r.IsDBNull(4)?null:r.GetInt64(4),RegionName=r.IsDBNull(5)?null:r.GetString(5),Currency=r.GetString(6),ValidFrom=r.IsDBNull(7)?null:Convert.ToDateTime(r.GetValue(7),CultureInfo.InvariantCulture),ValidTo=r.IsDBNull(8)?null:Convert.ToDateTime(r.GetValue(8),CultureInfo.InvariantCulture),IsActive=r.GetBoolean(9),Version=r.GetInt64(10)};
	private static SalesPriceListItem ReadItem(DbDataReader r) => new(){Id=r.GetInt64(0),SalesPriceListId=r.GetInt64(1),ItemId=r.GetInt64(2),PartNumber=r.GetString(3),Description=r.GetString(4),UnitPrice=Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture),DiscountPercent=Convert.ToDecimal(r.GetValue(6),CultureInfo.InvariantCulture),Version=r.GetInt64(7)};
	private static SalesRegion ReadRegion(DbDataReader r) => new(){Id=r.GetInt64(0),Code=r.GetString(1),Name=r.GetString(2),IsActive=r.GetBoolean(3),Version=r.GetInt64(4)};
	private static SalesPriceResult ReadPrice(DbDataReader r) => new(Convert.ToDecimal(r.GetValue(0),CultureInfo.InvariantCulture),Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture),r.GetInt64(2),r.GetString(3),(SalesPriceListScope)r.GetInt32(4),r.GetString(5),r.IsDBNull(6)?null:r.GetInt64(6));
}

internal sealed record CustomerPricingContext(long CustomerId, long? RegionId, string Currency, bool IsActive);
