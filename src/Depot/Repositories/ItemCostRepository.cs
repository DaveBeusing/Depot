// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ItemCostRepository : DatabaseRepository
{
	public ItemCostRepository(DatabaseAccess database) : base(database) { }
	public Task<ItemCostProfile?> GetProfileAsync(long itemId,CancellationToken token)=>Database.QuerySingleOrDefaultAsync(ProfileSelect+" WHERE ItemId=$ItemId;",ReadProfile,token,Parameter("$ItemId",itemId));
	public Task<ItemCostProfile?> GetProfileAsync(DatabaseTransactionContext transaction,long itemId,CancellationToken token)=>transaction.Session.QuerySingleOrDefaultAsync(ProfileSelect+" WHERE ItemId=$ItemId;",ReadProfile,token,Parameter("$ItemId",itemId));
	public Task<IReadOnlyList<ItemCostComponent>> ListComponentsAsync(long itemId,CancellationToken token)=>Database.QueryAsync(ComponentSelect+" WHERE ItemId=$ItemId ORDER BY Sequence,Id;",ReadComponent,token,Parameter("$ItemId",itemId));
	public Task<IReadOnlyList<ItemCostComponent>> ListComponentsAsync(DatabaseTransactionContext transaction,long itemId,CancellationToken token)=>transaction.Session.QueryAsync(ComponentSelect+" WHERE ItemId=$ItemId ORDER BY Sequence,Id;",ReadComponent,token,Parameter("$ItemId",itemId));
	public Task<ItemCostComponent?> GetComponentAsync(DatabaseTransactionContext transaction,long id,CancellationToken token)=>transaction.Session.QuerySingleOrDefaultAsync(ComponentSelect+" WHERE Id=$Id;",ReadComponent,token,Parameter("$Id",id));
	public Task<IReadOnlyList<ItemCostBaseValue>> GetPreferredBaseValuesAsync(long itemId,CancellationToken token)=>Database.QueryAsync("SELECT Id,PurchasePrice,Version FROM SupplierItems WHERE ItemId=$ItemId AND IsActive=1 AND IsPreferredSupplier=1 ORDER BY Id;",ReadBaseValue,token,Parameter("$ItemId",itemId));
	public Task<IReadOnlyList<ItemCostBaseValue>> GetPreferredBaseValuesAsync(DatabaseTransactionContext transaction,long itemId,CancellationToken token)=>transaction.Session.QueryAsync("SELECT Id,PurchasePrice,Version FROM SupplierItems WHERE ItemId=$ItemId AND IsActive=1 AND IsPreferredSupplier=1 ORDER BY Id;",ReadBaseValue,token,Parameter("$ItemId",itemId));
	public async Task<ItemCostProfile> SaveProfileAsync(DatabaseTransactionContext transaction,ItemCostProfile value,CancellationToken token)
	{
		if(value.Id==0){value.Id=await transaction.Session.InsertAsync("INSERT INTO ItemCostProfiles (ItemId,BaseCostSource,Currency) VALUES ($ItemId,$Source,$Currency);",token,Parameter("$ItemId",value.ItemId),Parameter("$Source",(int)value.BaseCostSource),Parameter("$Currency",value.Currency));return value;}
		var affected=await transaction.Session.ExecuteAsync("UPDATE ItemCostProfiles SET BaseCostSource=$Source,Currency=$Currency,Version=Version+1 WHERE Id=$Id AND Version=$Version;",token,Parameter("$Source",(int)value.BaseCostSource),Parameter("$Currency",value.Currency),Parameter("$Id",value.Id),Parameter("$Version",value.Version));if(affected!=1)throw new Services.ConcurrencyConflictException("item cost profile");value.Version++;return value;
	}
	public async Task<ItemCostComponent> SaveComponentAsync(DatabaseTransactionContext transaction,ItemCostComponent value,CancellationToken token)
	{
		if(value.Id==0){value.Id=await transaction.Session.InsertAsync("INSERT INTO ItemCostComponents (ItemId,Name,CalculationType,Value,CalculationBase,Sequence,IsActive,ValidFrom,ValidUntil) VALUES ($ItemId,$Name,$Type,$Value,$Base,$Sequence,$Active,$From,$Until);",token,Parameters(value));return value;}
		var affected=await transaction.Session.ExecuteAsync("UPDATE ItemCostComponents SET Name=$Name,CalculationType=$Type,Value=$Value,CalculationBase=$Base,Sequence=$Sequence,IsActive=$Active,ValidFrom=$From,ValidUntil=$Until,Version=Version+1 WHERE Id=$Id AND Version=$Version;",token,[..Parameters(value),Parameter("$Id",value.Id),Parameter("$Version",value.Version)]);if(affected!=1)throw new Services.ConcurrencyConflictException("item cost component");value.Version++;return value;
	}
	public Task<IReadOnlyList<ItemCostCandidate>> ListCandidatesAsync(CancellationToken token)=>Database.QueryAsync("SELECT Id,PartNumber,Description,CategoryId,ManufacturerId FROM Items WHERE IsActive=1 AND LifecycleStatus=$Active ORDER BY PartNumber,Id;",ReadCandidate,token,Parameter("$Active",(int)ItemLifecycleStatus.Active));
	public Task<IReadOnlyList<ItemCostCandidate>> ListCandidatesAsync(DatabaseTransactionContext transaction,CancellationToken token)=>transaction.Session.QueryAsync("SELECT Id,PartNumber,Description,CategoryId,ManufacturerId FROM Items WHERE IsActive=1 AND LifecycleStatus=$Active ORDER BY PartNumber,Id;",ReadCandidate,token,Parameter("$Active",(int)ItemLifecycleStatus.Active));
	private const string ProfileSelect="SELECT Id,ItemId,BaseCostSource,Currency,Version FROM ItemCostProfiles";
	private const string ComponentSelect="SELECT Id,ItemId,Name,CalculationType,Value,CalculationBase,Sequence,IsActive,ValidFrom,ValidUntil,Version FROM ItemCostComponents";
	private static DatabaseParameter[] Parameters(ItemCostComponent value)=>[Parameter("$ItemId",value.ItemId),Parameter("$Name",value.Name),Parameter("$Type",(int)value.CalculationType),Parameter("$Value",value.Value),Parameter("$Base",(int)value.CalculationBase),Parameter("$Sequence",value.Sequence),Parameter("$Active",value.IsActive),Parameter("$From",value.ValidFrom?.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Until",value.ValidUntil?.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))];
	private static ItemCostProfile ReadProfile(DbDataReader r)=>new(){Id=r.GetInt64(0),ItemId=r.GetInt64(1),BaseCostSource=(ItemCostBaseSource)r.GetInt32(2),Currency=r.GetString(3),Version=r.GetInt64(4)};
	private static ItemCostBaseValue ReadBaseValue(DbDataReader r)=>new(r.GetInt64(0),Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture),r.GetInt64(2));
	private static ItemCostCandidate ReadCandidate(DbDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt64(3),r.IsDBNull(4)?null:r.GetInt64(4));
	private static ItemCostComponent ReadComponent(DbDataReader r)=>new(){Id=r.GetInt64(0),ItemId=r.GetInt64(1),Name=r.GetString(2),CalculationType=(ItemCostCalculationType)r.GetInt32(3),Value=Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture),CalculationBase=(ItemCostCalculationBase)r.GetInt32(5),Sequence=r.GetInt32(6),IsActive=r.GetBoolean(7),ValidFrom=ReadDate(r,8),ValidUntil=ReadDate(r,9),Version=r.GetInt64(10)};
	private static DateTime? ReadDate(DbDataReader r,int ordinal)=>r.IsDBNull(ordinal)?null:Convert.ToDateTime(r.GetValue(ordinal),CultureInfo.InvariantCulture).Date;
}
