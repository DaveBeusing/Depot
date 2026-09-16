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
	public Task<IReadOnlyList<ItemCostBaseValue>> GetPreferredBaseValuesAsync(long itemId,CancellationToken token)=>Database.QueryAsync("SELECT Id,PurchasePrice,Version FROM SupplierItems WHERE ItemId=$ItemId AND IsActive=1 AND IsPreferredSupplier=1 ORDER BY Id;",r=>ReadBaseValue(r,"PreferredSupplier"),token,Parameter("$ItemId",itemId));
	public Task<IReadOnlyList<ItemCostBaseValue>> GetPreferredBaseValuesAsync(DatabaseTransactionContext transaction,long itemId,CancellationToken token)=>transaction.Session.QueryAsync("SELECT Id,PurchasePrice,Version FROM SupplierItems WHERE ItemId=$ItemId AND IsActive=1 AND IsPreferredSupplier=1 ORDER BY Id;",r=>ReadBaseValue(r,"PreferredSupplier"),token,Parameter("$ItemId",itemId));
	public Task<ItemCostBaseValue?> GetLastPurchaseBaseValueAsync(long itemId,DateTime effectiveDate,CancellationToken token)=>Database.QuerySingleOrDefaultAsync(LastPurchaseSelect,r=>ReadBaseValue(r,"LastPurchase"),token,Parameter("$ItemId",itemId),Parameter("$EffectiveDate",effectiveDate.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
	public Task<ItemCostBaseValue?> GetLastPurchaseBaseValueAsync(DatabaseTransactionContext transaction,long itemId,DateTime effectiveDate,CancellationToken token)=>transaction.Session.QuerySingleOrDefaultAsync(LastPurchaseSelect,r=>ReadBaseValue(r,"LastPurchase"),token,Parameter("$ItemId",itemId),Parameter("$EffectiveDate",effectiveDate.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));

	public async Task<ItemCostProfile> SaveProfileAsync(DatabaseTransactionContext transaction,ItemCostProfile value,CancellationToken token)
	{
		if(value.Id==0){value.Id=await transaction.Session.InsertAsync("INSERT INTO ItemCostProfiles (ItemId,BaseCostSource,Currency,ManualStandardCost,InventoryCostReference) VALUES ($ItemId,$Source,$Currency,$Manual,$Inventory);",token,ProfileParameters(value));return value;}
		var affected=await transaction.Session.ExecuteAsync("UPDATE ItemCostProfiles SET BaseCostSource=$Source,Currency=$Currency,ManualStandardCost=$Manual,InventoryCostReference=$Inventory,Version=Version+1 WHERE Id=$Id AND Version=$Version;",token,[..ProfileParameters(value),Parameter("$Id",value.Id),Parameter("$Version",value.Version)]);if(affected!=1)throw new Services.ConcurrencyConflictException("item cost profile");value.Version++;return value;
	}
	public async Task<ItemCostComponent> SaveComponentAsync(DatabaseTransactionContext transaction,ItemCostComponent value,CancellationToken token)
	{
		if(value.Id==0){value.Id=await transaction.Session.InsertAsync("INSERT INTO ItemCostComponents (ItemId,Name,CalculationType,Value,CalculationBase,Sequence,IsActive,ValidFrom,ValidUntil) VALUES ($ItemId,$Name,$Type,$Value,$Base,$Sequence,$Active,$From,$Until);",token,Parameters(value));return value;}
		var affected=await transaction.Session.ExecuteAsync("UPDATE ItemCostComponents SET Name=$Name,CalculationType=$Type,Value=$Value,CalculationBase=$Base,Sequence=$Sequence,IsActive=$Active,ValidFrom=$From,ValidUntil=$Until,Version=Version+1 WHERE Id=$Id AND Version=$Version;",token,[..Parameters(value),Parameter("$Id",value.Id),Parameter("$Version",value.Version)]);if(affected!=1)throw new Services.ConcurrencyConflictException("item cost component");value.Version++;return value;
	}
	public Task<IReadOnlyList<ItemCostCandidate>> ListCandidatesAsync(CancellationToken token)=>Database.QueryAsync("SELECT Id,PartNumber,Description,CategoryId,ManufacturerId FROM Items WHERE IsActive=1 ORDER BY PartNumber,Id;",ReadCandidate,token);
	public Task<IReadOnlyList<ItemCostCandidate>> ListCandidatesAsync(DatabaseTransactionContext transaction,CancellationToken token)=>transaction.Session.QueryAsync("SELECT Id,PartNumber,Description,CategoryId,ManufacturerId FROM Items WHERE IsActive=1 ORDER BY PartNumber,Id;",ReadCandidate,token);

	public Task<IReadOnlyList<PricingExchangeRate>> ListExchangeRatesAsync(CancellationToken token)=>Database.QueryAsync(ExchangeRateSelect+" ORDER BY SourceCurrency,TargetCurrency,EffectiveDate DESC,Id DESC;",ReadExchangeRate,token);
	public Task<PricingExchangeRate?> GetExchangeRateAsync(DatabaseTransactionContext transaction,long id,CancellationToken token)=>transaction.Session.QuerySingleOrDefaultAsync(ExchangeRateSelect+" WHERE Id=$Id;",ReadExchangeRate,token,Parameter("$Id",id));
	public Task<PricingExchangeRate?> ResolveExchangeRateAsync(string sourceCurrency,string targetCurrency,DateTime effectiveDate,CancellationToken token)=>Database.QuerySingleOrDefaultAsync(ExchangeRateSelect+" WHERE SourceCurrency=$Source AND TargetCurrency=$Target AND EffectiveDate<=$Date ORDER BY EffectiveDate DESC,Id DESC;",ReadExchangeRate,token,Parameter("$Source",sourceCurrency),Parameter("$Target",targetCurrency),Parameter("$Date",effectiveDate.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
	public Task<PricingExchangeRate?> ResolveExchangeRateAsync(DatabaseTransactionContext transaction,string sourceCurrency,string targetCurrency,DateTime effectiveDate,CancellationToken token)=>transaction.Session.QuerySingleOrDefaultAsync(ExchangeRateSelect+" WHERE SourceCurrency=$Source AND TargetCurrency=$Target AND EffectiveDate<=$Date ORDER BY EffectiveDate DESC,Id DESC;",ReadExchangeRate,token,Parameter("$Source",sourceCurrency),Parameter("$Target",targetCurrency),Parameter("$Date",effectiveDate.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
	public async Task<PricingExchangeRate> SaveExchangeRateAsync(DatabaseTransactionContext transaction,PricingExchangeRate value,CancellationToken token)
	{
		if(value.Id==0){value.Id=await transaction.Session.InsertAsync("INSERT INTO PricingExchangeRates (SourceCurrency,TargetCurrency,EffectiveDate,RateSource,Rate) VALUES ($Source,$Target,$Date,$RateSource,$Rate);",token,ExchangeRateParameters(value));return value;}
		var affected=await transaction.Session.ExecuteAsync("UPDATE PricingExchangeRates SET SourceCurrency=$Source,TargetCurrency=$Target,EffectiveDate=$Date,RateSource=$RateSource,Rate=$Rate,Version=Version+1 WHERE Id=$Id AND Version=$Version;",token,[..ExchangeRateParameters(value),Parameter("$Id",value.Id),Parameter("$Version",value.Version)]);if(affected!=1)throw new Services.ConcurrencyConflictException("pricing exchange rate");value.Version++;return value;
	}

	private const string ProfileSelect="SELECT Id,ItemId,BaseCostSource,Currency,ManualStandardCost,InventoryCostReference,Version FROM ItemCostProfiles";
	private const string ComponentSelect="SELECT Id,ItemId,Name,CalculationType,Value,CalculationBase,Sequence,IsActive,ValidFrom,ValidUntil,Version FROM ItemCostComponents";
	private const string ExchangeRateSelect="SELECT Id,SourceCurrency,TargetCurrency,EffectiveDate,RateSource,Rate,Version FROM PricingExchangeRates";
	private const string LastPurchaseSelect="SELECT grl.Id,pol.UnitPrice,pol.Version FROM GoodsReceiptLines grl INNER JOIN GoodsReceipts gr ON gr.Id=grl.GoodsReceiptId INNER JOIN PurchaseOrderLines pol ON pol.Id=grl.PurchaseOrderLineId WHERE pol.ItemId=$ItemId AND gr.ReceiptDate<=$EffectiveDate ORDER BY gr.ReceiptDate DESC,grl.Id DESC;";
	private static DatabaseParameter[] ProfileParameters(ItemCostProfile value)=>[Parameter("$ItemId",value.ItemId),Parameter("$Source",(int)value.BaseCostSource),Parameter("$Currency",value.Currency),Parameter("$Manual",value.ManualStandardCost),Parameter("$Inventory",value.InventoryCostReference)];
	private static DatabaseParameter[] Parameters(ItemCostComponent value)=>[Parameter("$ItemId",value.ItemId),Parameter("$Name",value.Name),Parameter("$Type",(int)value.CalculationType),Parameter("$Value",value.Value),Parameter("$Base",(int)value.CalculationBase),Parameter("$Sequence",value.Sequence),Parameter("$Active",value.IsActive),Parameter("$From",value.ValidFrom?.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Until",value.ValidUntil?.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))];
	private static DatabaseParameter[] ExchangeRateParameters(PricingExchangeRate value)=>[Parameter("$Source",value.SourceCurrency),Parameter("$Target",value.TargetCurrency),Parameter("$Date",value.EffectiveDate.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$RateSource",value.RateSource),Parameter("$Rate",value.Rate)];
	private static ItemCostProfile ReadProfile(DbDataReader r)=>new(){Id=r.GetInt64(0),ItemId=r.GetInt64(1),BaseCostSource=(ItemCostBaseSource)r.GetInt32(2),Currency=r.GetString(3),ManualStandardCost=r.IsDBNull(4)?null:Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture),InventoryCostReference=r.IsDBNull(5)?null:Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture),Version=r.GetInt64(6)};
	private static ItemCostBaseValue ReadBaseValue(DbDataReader r,string evidenceType)=>new(r.GetInt64(0),Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture),r.GetInt64(2),evidenceType);
	private static ItemCostCandidate ReadCandidate(DbDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt64(3),r.IsDBNull(4)?null:r.GetInt64(4));
	private static ItemCostComponent ReadComponent(DbDataReader r)=>new(){Id=r.GetInt64(0),ItemId=r.GetInt64(1),Name=r.GetString(2),CalculationType=(ItemCostCalculationType)r.GetInt32(3),Value=Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture),CalculationBase=(ItemCostCalculationBase)r.GetInt32(5),Sequence=r.GetInt32(6),IsActive=r.GetBoolean(7),ValidFrom=ReadDate(r,8),ValidUntil=ReadDate(r,9),Version=r.GetInt64(10)};
	private static PricingExchangeRate ReadExchangeRate(DbDataReader r)=>new(){Id=r.GetInt64(0),SourceCurrency=r.GetString(1),TargetCurrency=r.GetString(2),EffectiveDate=Convert.ToDateTime(r.GetValue(3),CultureInfo.InvariantCulture).Date,RateSource=r.GetString(4),Rate=Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture),Version=r.GetInt64(6)};
	private static DateTime? ReadDate(DbDataReader r,int ordinal)=>r.IsDBNull(ordinal)?null:Convert.ToDateTime(r.GetValue(ordinal),CultureInfo.InvariantCulture).Date;
}
