// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ItemCostCalculationService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ItemCostRepository _costs;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	public ItemCostCalculationService(IDatabaseTransactionRunner transactions,ItemCostRepository costs,AuditRepository auditEntries,AuditService audit,IAuthorizationService authorization){_transactions=transactions;_costs=costs;_auditEntries=auditEntries;_audit=audit;_authorization=authorization;}
	public bool CanView=>_authorization.HasPermission(ApplicationPermission.ItemsView);
	public bool CanManage=>_authorization.HasPermission(ApplicationPermission.ItemsEdit)||_authorization.HasPermission(ApplicationPermission.ItemsManage);
	public Task<ItemCostProfile?> GetProfileAsync(long itemId,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ItemsView);return _costs.GetProfileAsync(itemId,token);}
	public Task<IReadOnlyList<ItemCostComponent>> ListComponentsAsync(long itemId,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ItemsView);return _costs.ListComponentsAsync(itemId,token);}
	public async Task<ItemCostProfile> SaveProfileAsync(ItemCostProfile value,CancellationToken token=default)
	{
		RequireManage();NormalizeProfile(value);return await _transactions.ExecuteAsync(async(transaction,ct)=>{var before=await _costs.GetProfileAsync(transaction,value.ItemId,ct);if(before is not null&&value.Id==0){value.Id=before.Id;value.Version=before.Version;}var saved=await _costs.SaveProfileAsync(transaction,value,ct);await _auditEntries.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),ct);return saved;},token);
	}
	public async Task<ItemCostComponent> SaveComponentAsync(ItemCostComponent value,CancellationToken token=default)
	{
		RequireManage();NormalizeComponent(value);return await _transactions.ExecuteAsync(async(transaction,ct)=>{var before=value.Id==0?null:await _costs.GetComponentAsync(transaction,value.Id,ct)??throw new InvalidOperationException("Item cost component was not found.");if(before is not null&&before.ItemId!=value.ItemId)throw new InvalidOperationException("Item cost component belongs to another item.");var saved=await _costs.SaveComponentAsync(transaction,value,ct);await _auditEntries.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),ct);return saved;},token);
	}
	public async Task<ItemCostComponent> SetComponentActiveAsync(ItemCostComponent value,bool active,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(value);var changed=new ItemCostComponent{Id=value.Id,ItemId=value.ItemId,Name=value.Name,CalculationType=value.CalculationType,Value=value.Value,CalculationBase=value.CalculationBase,Sequence=value.Sequence,IsActive=active,ValidFrom=value.ValidFrom,ValidUntil=value.ValidUntil,Version=value.Version};return await SaveComponentAsync(changed,token);
	}
	public async Task<ItemCostCalculationResult> CalculateAsync(long itemId,DateTime effectiveDate,string? expectedCurrency=null,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ItemsView);return await CalculateCoreAsync(null,itemId,effectiveDate,expectedCurrency,token);}
	internal Task<ItemCostCalculationResult> CalculateAsync(DatabaseTransactionContext transaction,long itemId,DateTime effectiveDate,string? expectedCurrency,CancellationToken token)=>CalculateCoreAsync(transaction,itemId,effectiveDate,expectedCurrency,token);
	private async Task<ItemCostCalculationResult> CalculateCoreAsync(DatabaseTransactionContext? transaction,long itemId,DateTime effectiveDate,string? expectedCurrency,CancellationToken token)
	{
		token.ThrowIfCancellationRequested();if(itemId<=0)throw new ArgumentOutOfRangeException(nameof(itemId));var profile=transaction is null?await _costs.GetProfileAsync(itemId,token):await _costs.GetProfileAsync(transaction,itemId,token);if(profile is null)return Fail(itemId,effectiveDate,"No item cost profile exists. Define the purchase-price currency before calculating cost.");var currency=profile.Currency.Trim().ToUpperInvariant();if(!string.IsNullOrWhiteSpace(expectedCurrency)&&!string.Equals(currency,expectedCurrency.Trim(),StringComparison.OrdinalIgnoreCase))return Fail(itemId,effectiveDate,$"Cost currency {currency} does not match target currency {expectedCurrency.Trim().ToUpperInvariant()}.",currency,profile.BaseCostSource);
		var bases=transaction is null?await _costs.GetPreferredBaseValuesAsync(itemId,token):await _costs.GetPreferredBaseValuesAsync(transaction,itemId,token);if(bases.Count==0)return Fail(itemId,effectiveDate,"No active preferred supplier purchase price is available.",currency,profile.BaseCostSource);if(bases.Count!=1)return Fail(itemId,effectiveDate,"Multiple active preferred supplier purchase prices exist. Resolve supplier master-data ambiguity first.",currency,profile.BaseCostSource);var baseValue=bases[0];if(baseValue.Amount<0m)return Fail(itemId,effectiveDate,"Preferred supplier purchase price is invalid.",currency,profile.BaseCostSource);
		var baseCost=CurrencyRounding.Round(baseValue.Amount,currency);var running=baseCost;var all=transaction is null?await _costs.ListComponentsAsync(itemId,token):await _costs.ListComponentsAsync(transaction,itemId,token);var date=effectiveDate.Date;var active=all.Where(c=>c.IsActive&&(c.ValidFrom is null||c.ValidFrom.Value.Date<=date)&&(c.ValidUntil is null||c.ValidUntil.Value.Date>=date)).OrderBy(c=>c.Sequence).ThenBy(c=>c.Id).ToArray();var results=new List<ItemCostComponentResult>(active.Length);
		foreach(var component in active){token.ThrowIfCancellationRequested();if(component.Value<0m)return Fail(itemId,effectiveDate,$"Cost component '{component.Name}' has an invalid negative value.",currency,profile.BaseCostSource);var basis=component.CalculationType==ItemCostCalculationType.Percentage?(component.CalculationBase==ItemCostCalculationBase.BaseCost?baseCost:running):0m;var applied=component.CalculationType==ItemCostCalculationType.Absolute?component.Value:basis*component.Value/100m;applied=CurrencyRounding.Round(applied,currency);running=CurrencyRounding.Round(running+applied,currency);results.Add(new(component.Id,component.Name,component.CalculationType,component.CalculationBase,component.Value,basis,applied,running,component.Sequence,component.Version));}
		var evidence=string.Join('|',new[]{$"p:{profile.Id}:{profile.Version}",$"b:{baseValue.SupplierItemId}:{baseValue.Version}"}.Concat(results.Select(c=>$"c:{c.ComponentId}:{c.Version}")));return new ItemCostCalculationResult{ItemId=itemId,IsSuccess=true,BaseCost=baseCost,CalculatedCost=running,Currency=currency,CalculationDate=date,BaseCostSource=profile.BaseCostSource,EvidenceVersion=evidence,Components=results};
	}
	private static ItemCostCalculationResult Fail(long itemId,DateTime date,string error,string currency="",ItemCostBaseSource source=ItemCostBaseSource.PreferredSupplierPurchasePrice)=>new(){ItemId=itemId,IsSuccess=false,Error=error,Currency=currency,CalculationDate=date.Date,BaseCostSource=source};
	private void RequireManage(){if(!CanManage)throw new UnauthorizedAccessException("Managing item costs requires item edit permission.");}
	private static void NormalizeProfile(ItemCostProfile value){ArgumentNullException.ThrowIfNull(value);if(value.ItemId<=0)throw new ArgumentOutOfRangeException(nameof(value.ItemId));if(!Enum.IsDefined(value.BaseCostSource))throw new ArgumentOutOfRangeException(nameof(value.BaseCostSource));value.Currency=value.Currency.Trim().ToUpperInvariant();if(value.Currency.Length!=3||!value.Currency.All(char.IsLetter))throw new ArgumentException("Item cost currency must be a three-letter ISO currency code.");}
	private static void NormalizeComponent(ItemCostComponent value){ArgumentNullException.ThrowIfNull(value);if(value.ItemId<=0)throw new ArgumentOutOfRangeException(nameof(value.ItemId));value.Name=value.Name.Trim();if(value.Name.Length is 0 or >200)throw new ArgumentException("Cost component name is required and limited to 200 characters.");if(!Enum.IsDefined(value.CalculationType)||!Enum.IsDefined(value.CalculationBase))throw new ArgumentException("Valid calculation type and base are required.");if(value.Value<0m)throw new ArgumentException("Cost component value cannot be negative.");if(value.Sequence<0)throw new ArgumentException("Cost component sequence cannot be negative.");if(value.ValidFrom is not null&&value.ValidUntil is not null&&value.ValidUntil.Value.Date<value.ValidFrom.Value.Date)throw new ArgumentException("Valid-until cannot be before valid-from.");if(value.CalculationType==ItemCostCalculationType.Absolute)value.CalculationBase=ItemCostCalculationBase.BaseCost;}
}
