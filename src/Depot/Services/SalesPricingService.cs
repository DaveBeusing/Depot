// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesPricingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SalesPriceListRepository _prices;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public SalesPricingService(IDatabaseTransactionRunner transactions,SalesPriceListRepository prices,AuditRepository auditEntries,AuditService audit,IAuthorizationService authorization)
	{_transactions=transactions;_prices=prices;_auditEntries=auditEntries;_audit=audit;_authorization=authorization;}

	public bool CanView=>_authorization.HasPermission(ApplicationPermission.SalesPricingView);
	public bool CanManage=>_authorization.HasPermission(ApplicationPermission.SalesPricingManage);
	public Task<IReadOnlyList<SalesPriceList>> ListAsync(CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.SalesPricingView);return _prices.ListAsync(token);}
	public Task<IReadOnlyList<SalesRegion>> ListRegionsAsync(CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.SalesPricingView);return _prices.ListRegionsAsync(token);}
	public Task<CustomerPriceListAssignment?> GetCustomerAssignmentAsync(long customerId,CancellationToken token=default)=>_prices.GetCustomerAssignmentAsync(customerId,token);

	public async Task<SalesPriceResult?> ResolveAsync(long customerId,long itemId,DateTime effectiveDate,CancellationToken token=default)
	{
		var context=await _prices.GetCustomerPricingContextAsync(customerId,token);return context is null or {IsActive:false}?null:await ResolveAsync(customerId,itemId,1,effectiveDate,context.Currency,token);
	}
	public async Task<SalesPriceResult?> ResolveAsync(long customerId,long itemId,int quantity,DateTime effectiveDate,string currency,CancellationToken token=default)
	{
		if(customerId<=0)throw new ArgumentOutOfRangeException(nameof(customerId));if(itemId<=0)throw new ArgumentOutOfRangeException(nameof(itemId));if(quantity<=0)throw new ArgumentOutOfRangeException(nameof(quantity));var normalizedCurrency=currency.Trim().ToUpperInvariant();if(normalizedCurrency.Length!=3)throw new ArgumentException("Currency must be a three-letter code.",nameof(currency));return await _prices.ResolveAsync(customerId,itemId,effectiveDate.Date,normalizedCurrency,token);
	}

	public async Task<SalesPriceList> SaveAsync(SalesPriceList value,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);NormalizeAndValidate(value);return await _transactions.ExecuteAsync((transaction,ct)=>SaveInTransactionAsync(transaction,value,ct),token);
	}

	internal async Task<SalesPriceList> SaveInTransactionAsync(DatabaseTransactionContext transaction,SalesPriceList value,CancellationToken token)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);NormalizeAndValidate(value);var before=value.Id==0?null:await _prices.GetByIdAsync(transaction,value.Id,token)??throw new InvalidOperationException("Sales price list was not found.");
		if(value.Scope==SalesPriceListScope.Region){var region=await _prices.GetRegionAsync(transaction,value.RegionId!.Value,token)??throw new InvalidOperationException("Sales region was not found.");if(!region.IsActive)throw new InvalidOperationException("An inactive sales region cannot own a regional price list.");value.RegionName=region.Name;}
		if(value.Scope!=SalesPriceListScope.Customer&&value.Id>0&&await _prices.HasCustomerAssignmentsAsync(transaction,value.Id,token))throw new InvalidOperationException("Clear all customer assignments before changing a customer price list to a default scope.");
		if(value.IsActive&&value.Scope==SalesPriceListScope.Customer&&(value.Id==0||!await _prices.HasCustomerAssignmentsAsync(transaction,value.Id,token)))throw new InvalidOperationException("An active customer price list requires at least one customer assignment. Save it as inactive, assign a customer, and then activate it.");
		if(value.IsActive&&value.Scope is SalesPriceListScope.Global or SalesPriceListScope.Region){var conflict=await _prices.FindActiveDefaultAsync(transaction,value.Scope,value.RegionId,value.Id,token);if(conflict is not null){var scope=value.Scope==SalesPriceListScope.Global?"global":$"regional for {value.RegionName??value.RegionId?.ToString()}";throw new InvalidOperationException($"Price list '{conflict.Name}' is already the active {scope} default.");}}
		await _prices.SaveAsync(transaction,value,token);var saved=await _prices.GetByIdAsync(transaction,value.Id,token)??value;await _auditEntries.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),token);return saved;
	}

	public async Task<SalesPriceListItem> SaveItemAsync(SalesPriceListItem item,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);if(item.SalesPriceListId<=0||item.ItemId<=0||item.UnitPrice<0||item.DiscountPercent is <0 or >100)throw new ArgumentException("A valid price-list item is required.");
		return await _transactions.ExecuteAsync(async(transaction,ct)=>{_ = await _prices.GetByIdAsync(transaction,item.SalesPriceListId,ct)??throw new InvalidOperationException("Sales price list was not found.");var before=await _prices.GetItemAsync(transaction,item.SalesPriceListId,item.ItemId,ct);if(before is not null&&item.Id==0){item.Id=before.Id;item.Version=before.Version;}await _prices.SaveItemAsync(transaction,item,ct);var saved=await _prices.GetItemAsync(transaction,item.SalesPriceListId,item.ItemId,ct)??item;await _auditEntries.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),ct);return saved;},token);
	}
	public async Task DeleteItemAsync(SalesPriceListItem item,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);ArgumentNullException.ThrowIfNull(item);if(item.Id<=0||item.SalesPriceListId<=0||item.ItemId<=0||item.Version<=0)throw new ArgumentException("A persisted price-list item is required.",nameof(item));await _transactions.ExecuteAsync(async(transaction,ct)=>{var before=await _prices.GetItemAsync(transaction,item.SalesPriceListId,item.ItemId,ct)??throw new InvalidOperationException("Sales price-list item was not found.");if(before.Id!=item.Id)throw new ConcurrencyConflictException("sales price list item");await _prices.DeleteItemAsync(transaction,item.Id,item.Version,ct);await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(item.Id,"Removed",before,(SalesPriceListItem?)null),ct);return true;},token);
	}
	public async Task<SalesRegion> SaveRegionAsync(SalesRegion value,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);value.Code=value.Code.Trim().ToUpperInvariant();value.Name=value.Name.Trim();if(value.Code.Length==0||value.Code.Length>50||value.Name.Length==0||value.Name.Length>200)throw new ArgumentException("A region code and name are required.");return await _transactions.ExecuteAsync(async(transaction,ct)=>{var before=value.Id==0?null:await _prices.GetRegionAsync(transaction,value.Id,ct)??throw new InvalidOperationException("Sales region was not found.");if(!value.IsActive&&await _prices.FindActiveDefaultAsync(transaction,SalesPriceListScope.Region,value.Id,0,ct)is not null)throw new InvalidOperationException("Deactivate the regional default price list before deactivating its sales region.");var saved=await _prices.SaveRegionAsync(transaction,value,ct);await _auditEntries.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),ct);return saved;},token);
	}
	public async Task AssignCustomerAsync(long customerId,long? priceListId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);if(customerId<=0)throw new ArgumentOutOfRangeException(nameof(customerId));await _transactions.ExecuteAsync(async(transaction,ct)=>{var customer=await _prices.GetCustomerPricingContextAsync(customerId,ct)??throw new InvalidOperationException("Customer was not found.");if(!customer.IsActive)throw new InvalidOperationException("An inactive customer cannot receive a price-list assignment.");var before=await _prices.GetCustomerAssignmentAsync(transaction,customerId,ct);SalesPriceList? list=null;if(priceListId is >0){list=await _prices.GetByIdAsync(transaction,priceListId.Value,ct)??throw new InvalidOperationException("Sales price list was not found.");if(list.Scope!=SalesPriceListScope.Customer)throw new InvalidOperationException("Only customer-scoped price lists can be assigned to a customer.");}await _prices.AssignCustomerAsync(transaction,customerId,list?.Id,ct);if(before is not null&&before.SalesPriceListId!=list?.Id&&!await _prices.HasCustomerAssignmentsAsync(transaction,before.SalesPriceListId,ct)){var orphaned=await _prices.GetByIdAsync(transaction,before.SalesPriceListId,ct);if(orphaned is {IsActive:true}){var deactivated=Copy(orphaned);deactivated.IsActive=false;await _prices.SaveAsync(transaction,deactivated,ct);await _auditEntries.CreateAsync(transaction,_audit.CreateUpdatedEntry(deactivated.Id,orphaned,deactivated),ct);}}var after=list is null?null:new CustomerPriceListAssignment{CustomerId=customerId,SalesPriceListId=list.Id,PriceListName=list.Name,IsActive=list.IsActive};await _auditEntries.CreateAsync(transaction,_audit.CreateActionEntry(customerId,"Assigned",before,after),ct);return true;},token);
	}
	private static SalesPriceList Copy(SalesPriceList value)=>new(){Id=value.Id,Code=value.Code,Name=value.Name,Scope=value.Scope,RegionId=value.RegionId,RegionName=value.RegionName,Currency=value.Currency,ValidFrom=value.ValidFrom,ValidTo=value.ValidTo,IsActive=value.IsActive,Version=value.Version,Items=value.Items};
	internal static void NormalizeAndValidate(SalesPriceList value){value.Code=value.Code.Trim().ToUpperInvariant();value.Name=value.Name.Trim();value.Currency=value.Currency.Trim().ToUpperInvariant();if(value.Code.Length==0||value.Code.Length>100||value.Name.Length==0||value.Name.Length>250||value.Currency.Length!=3)throw new ArgumentException("Code, name and a three-letter currency are required.");if(!Enum.IsDefined(value.Scope))throw new ArgumentOutOfRangeException(nameof(value.Scope));if(value.Scope==SalesPriceListScope.Region&&value.RegionId is not >0)throw new ArgumentException("A regional price list requires a sales region.");if(value.Scope!=SalesPriceListScope.Region&&value.RegionId is not null)throw new ArgumentException("Only a regional price list can reference a sales region.");if(value.ValidFrom is not null&&value.ValidTo is not null&&value.ValidTo<value.ValidFrom)throw new ArgumentException("Valid-to must not be before valid-from.");}
}
