// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesPricingService
{
	private readonly SalesPriceListRepository _prices;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public SalesPricingService(SalesPriceListRepository prices, AuditService audit, IAuthorizationService authorization)
	{
		_prices = prices;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.SalesPricingView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.SalesPricingManage);
	public Task<IReadOnlyList<SalesPriceList>> ListAsync(CancellationToken token = default) { _authorization.RequirePermission(ApplicationPermission.SalesPricingView); return _prices.ListAsync(token); }
	public Task<SalesPriceResult?> ResolveAsync(long customerId,long itemId,DateTime date,CancellationToken token=default) => _prices.ResolveAsync(customerId,itemId,date,token);
	public Task<CustomerPriceListAssignment?> GetCustomerAssignmentAsync(long customerId,CancellationToken token=default) => _prices.GetCustomerAssignmentAsync(customerId,token);

	public async Task<SalesPriceList> SaveAsync(SalesPriceList value,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);
		value.Code=value.Code.Trim().ToUpperInvariant(); value.Name=value.Name.Trim(); value.Currency=value.Currency.Trim().ToUpperInvariant();
		if(value.Code.Length==0||value.Name.Length==0||value.Currency.Length!=3)throw new ArgumentException("Code, name and a three-letter currency are required.");
		if(value.ValidFrom is not null&&value.ValidTo is not null&&value.ValidTo<value.ValidFrom)throw new ArgumentException("Valid-to must not be before valid-from.");
		var saved=await _prices.SaveAsync(value,token); await _audit.RecordUpdatedAsync(saved.Id,saved,saved,token); return saved;
	}

	public async Task<SalesPriceListItem> SaveItemAsync(SalesPriceListItem item,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);
		if(item.SalesPriceListId<=0||item.ItemId<=0||item.UnitPrice<0||item.DiscountPercent<0||item.DiscountPercent>100)throw new ArgumentException("A valid price-list item is required.");
		var saved=await _prices.SaveItemAsync(item,token); await _audit.RecordUpdatedAsync(saved.Id,saved,saved,token); return saved;
	}

	public async Task AssignCustomerAsync(long customerId,long? priceListId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesPricingManage);
		await _prices.AssignCustomerAsync(customerId,priceListId,token);
	}
}
