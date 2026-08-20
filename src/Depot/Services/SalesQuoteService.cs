// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesQuoteService
{
	private readonly SalesQuoteRepository _quotes;
	private readonly CustomerRepository _customers;
	private readonly SalesOrderService _orders;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public SalesQuoteService(SalesQuoteRepository quotes, CustomerRepository customers, SalesOrderService orders, AuditService audit, IAuthorizationService authorization)
	{
		_quotes=quotes; _customers=customers; _orders=orders; _audit=audit; _authorization=authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.SalesQuotesView);
	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.SalesQuotesCreate);
	public bool CanEdit => _authorization.HasPermission(ApplicationPermission.SalesQuotesEdit);
	public bool CanSend => _authorization.HasPermission(ApplicationPermission.SalesQuotesSend);
	public bool CanConvert => _authorization.HasPermission(ApplicationPermission.SalesQuotesConvert);

	public Task<PageResult<SalesQuote>> SearchAsync(string? searchText,SalesQuoteStatus? status,int pageNumber=1,int pageSize=100,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.SalesQuotesView);return _quotes.SearchAsync(searchText,status,pageNumber,pageSize,token);}
	public Task<SalesQuote?> GetByIdAsync(long id,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.SalesQuotesView);return _quotes.GetByIdAsync(id,token);}

	public async Task<SalesQuote> SaveDraftAsync(SalesQuote quote,CancellationToken token=default)
	{
		_authorization.RequirePermission(quote.Id==0?ApplicationPermission.SalesQuotesCreate:ApplicationPermission.SalesQuotesEdit);
		if(quote.Id!=0&&quote.Status!=SalesQuoteStatus.Draft)throw new InvalidOperationException("Only draft quotes can be edited.");
		if(quote.CustomerId<=0||quote.Lines.Count==0||quote.Lines.Any(l=>l.Quantity<=0||l.UnitPrice<0))throw new InvalidOperationException("A quote requires a customer and at least one valid line.");
		var customer=await _customers.GetByIdAsync(quote.CustomerId,token)??throw new InvalidOperationException("Customer was not found.");
		quote.BillingAddress??=customer.Addresses.FirstOrDefault(a=>a.Type==CustomerAddressType.Billing&&a.IsDefault)?.Address??customer.BillingAddress;
		quote.ShippingAddress??=customer.Addresses.FirstOrDefault(a=>a.Type==CustomerAddressType.Shipping&&a.IsDefault)?.Address??customer.ShippingAddress;
		quote.Currency=string.IsNullOrWhiteSpace(quote.Currency)?customer.Currency:quote.Currency.Trim().ToUpperInvariant();
		if(quote.ValidUntil<quote.QuoteDate)throw new InvalidOperationException("Quote validity must not end before the quote date.");
		if(quote.Id==0){quote.CreatedByUserId=RequireUser().Id;quote.CreatedAtUtc=DateTime.UtcNow;}
		var saved=await _quotes.SaveDraftAsync(quote,token); await _audit.RecordUpdatedAsync(saved.Id,saved,saved,token); return saved;
	}

	public Task<SalesQuote> MarkSentAsync(long id,long version,CancellationToken token=default)=>ChangeStatusAsync(id,version,SalesQuoteStatus.Draft,SalesQuoteStatus.Sent,ApplicationPermission.SalesQuotesSend,token);
	public Task<SalesQuote> AcceptAsync(long id,long version,CancellationToken token=default)=>ChangeStatusAsync(id,version,SalesQuoteStatus.Sent,SalesQuoteStatus.Accepted,ApplicationPermission.SalesQuotesEdit,token);
	public Task<SalesQuote> RejectAsync(long id,long version,CancellationToken token=default)=>ChangeStatusAsync(id,version,SalesQuoteStatus.Sent,SalesQuoteStatus.Rejected,ApplicationPermission.SalesQuotesEdit,token);

	public async Task<SalesOrder> ConvertToSalesOrderAsync(long id,long version,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesQuotesConvert);
		var quote=await _quotes.GetByIdAsync(id,token)??throw new InvalidOperationException("Quote was not found.");
		if(quote.Version!=version)throw new ConcurrencyConflictException("sales quote");
		if(quote.Status is not (SalesQuoteStatus.Accepted or SalesQuoteStatus.Sent))throw new InvalidOperationException("Only a sent or accepted quote can be converted.");
		if(quote.ValidUntil<DateTime.Today)throw new InvalidOperationException("The quote has expired.");
		var order=await _orders.SaveDraftAsync(new SalesOrder{CustomerId=quote.CustomerId,BillingAddress=quote.BillingAddress,ShippingAddress=quote.ShippingAddress,OrderDate=DateTime.Today,Currency=quote.Currency,CustomerReference=quote.CustomerReference,Notes=$"Converted from {quote.QuoteNumber}{(string.IsNullOrWhiteSpace(quote.Notes)?string.Empty:$" · {quote.Notes}")}",Lines=quote.Lines.Select(l=>new SalesOrderLine{ItemId=l.ItemId,PartNumber=l.PartNumber,Description=l.Description,Quantity=l.Quantity,UnitPrice=l.UnitPrice,DiscountPercent=l.DiscountPercent,TaxRate=l.TaxRate}).ToArray()},token);
		if(!await _quotes.MarkConvertedAsync(id,version,order.Id,DateTime.UtcNow,token))throw new ConcurrencyConflictException("sales quote");
		return order;
	}

	private async Task<SalesQuote> ChangeStatusAsync(long id,long version,SalesQuoteStatus expected,SalesQuoteStatus target,ApplicationPermission permission,CancellationToken token)
	{
		_authorization.RequirePermission(permission);
		var before=await _quotes.GetByIdAsync(id,token)??throw new InvalidOperationException("Quote was not found.");
		if(before.Version!=version||before.Status!=expected)throw new ConcurrencyConflictException("sales quote");
		if(!await _quotes.SetStatusAsync(id,version,expected,target,token))throw new ConcurrencyConflictException("sales quote");
		return await _quotes.GetByIdAsync(id,token)??throw new InvalidOperationException("Quote could not be reloaded.");
	}
	private User RequireUser()=>_authorization.CurrentUser is {IsActive:true} user?user:throw new UnauthorizedAccessException("An active signed-in user is required for quotes.");
}
