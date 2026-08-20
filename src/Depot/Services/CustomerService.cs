// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class CustomerService
{
	private readonly CustomerRepository _customers;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public CustomerService(CustomerRepository customers, AuditService audit, IAuthorizationService authorization)
	{
		_customers = customers;
		_audit = audit;
		_authorization = authorization;
		SalesCommercialContext.ConfigureCustomer(this);
	}

	public bool CanCreate => _authorization.HasPermission(ApplicationPermission.CustomersCreate);
	public bool CanEdit => _authorization.HasPermission(ApplicationPermission.CustomersEdit);

	public Task<PageResult<Customer>> SearchAsync(string? searchText, bool includeInactive = false, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersView);
		return _customers.SearchAsync(searchText, includeInactive, pageNumber, pageSize, cancellationToken);
	}

	public Task<IReadOnlyList<Customer>> ListActiveAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersView);
		return _customers.ListActiveAsync(cancellationToken);
	}

	public Task<Customer?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersView);
		return _customers.GetByIdAsync(id, cancellationToken);
	}

	public Task<IReadOnlyList<CustomerAddress>> ListAddressesAsync(long customerId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersView);
		if (customerId <= 0) return Task.FromResult<IReadOnlyList<CustomerAddress>>([]);
		return _customers.ListAddressesAsync(customerId, cancellationToken);
	}

	public Task<IReadOnlyList<CustomerContact>> ListContactsAsync(long customerId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersView);
		if (customerId <= 0) return Task.FromResult<IReadOnlyList<CustomerContact>>([]);
		return _customers.ListContactsAsync(customerId, cancellationToken);
	}

	public async Task<CustomerAddress> SaveAddressAsync(CustomerAddress address, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersEdit);
		if (address.CustomerId <= 0) throw new ArgumentException("A customer is required.", nameof(address));
		address.Name = Normalize(address.Name);
		address.Address = address.Address.Trim();
		if (address.Address.Length == 0) throw new ArgumentException("An address is required.", nameof(address));
		if (address.Address.Length > 2000) throw new ArgumentException("Customer address must not exceed 2000 characters.", nameof(address));
		if (address.Name?.Length > 250) throw new ArgumentException("Address names must not exceed 250 characters.", nameof(address));
		var saved = await _customers.SaveAddressAsync(address, cancellationToken);
		var customer = await _customers.GetByIdAsync(address.CustomerId, cancellationToken);
		if (customer is not null) await _audit.RecordUpdatedAsync(customer.Id, customer, customer, cancellationToken);
		return saved;
	}

	public async Task<CustomerContact> SaveContactAsync(CustomerContact contact, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.CustomersEdit);
		if (contact.CustomerId <= 0) throw new ArgumentException("A customer is required.", nameof(contact));
		contact.Name = contact.Name.Trim();
		contact.Department = Normalize(contact.Department);
		contact.Email = Normalize(contact.Email);
		contact.Phone = Normalize(contact.Phone);
		contact.Mobile = Normalize(contact.Mobile);
		if (contact.Name.Length == 0 || contact.Name.Length > 250) throw new ArgumentException("A valid contact name is required.");
		if (contact.Department?.Length > 250 || contact.Email?.Length > 250 || contact.Phone?.Length > 100 || contact.Mobile?.Length > 100) throw new ArgumentException("Customer contact data exceeds its maximum length.");
		var saved = await _customers.SaveContactAsync(contact, cancellationToken);
		var customer = await _customers.GetByIdAsync(contact.CustomerId, cancellationToken);
		if (customer is not null) await _audit.RecordUpdatedAsync(customer.Id, customer, customer, cancellationToken);
		return saved;
	}

	public async Task<Customer> SaveAsync(Customer customer, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(customer.Id == 0 ? ApplicationPermission.CustomersCreate : ApplicationPermission.CustomersEdit);
		NormalizeAndValidate(customer);
		var before = customer.Id == 0 ? null : await _customers.GetByIdAsync(customer.Id, cancellationToken)
			?? throw new InvalidOperationException("Customer was not found.");
		var saved = await _customers.SaveAsync(customer, cancellationToken);
		if (before is null) await _audit.RecordCreatedAsync(saved.Id, saved, cancellationToken);
		else await _audit.RecordUpdatedAsync(saved.Id, before, saved, cancellationToken);
		return saved;
	}

	private static void NormalizeAndValidate(Customer customer)
	{
		customer.Name = customer.Name.Trim();
		customer.BillingAddress = Normalize(customer.BillingAddress);
		customer.ShippingAddress = Normalize(customer.ShippingAddress);
		customer.ContactName = Normalize(customer.ContactName);
		customer.Email = Normalize(customer.Email);
		customer.Phone = Normalize(customer.Phone);
		customer.TaxId = Normalize(customer.TaxId);
		customer.Currency = string.IsNullOrWhiteSpace(customer.Currency) ? "EUR" : customer.Currency.Trim().ToUpperInvariant();
		if (customer.Name.Length == 0) throw new ArgumentException("A customer name is required.");
		if (customer.Name.Length > 250) throw new ArgumentException("Customer name must not exceed 250 characters.");
		if (customer.Currency.Length != 3) throw new ArgumentException("Currency must be a three-letter code.");
		if (customer.PaymentTermsDays < 0 || customer.PaymentTermsDays > 3650) throw new ArgumentOutOfRangeException(nameof(customer.PaymentTermsDays));
		if (customer.Email?.Length > 250 || customer.Phone?.Length > 100 || customer.TaxId?.Length > 100) throw new ArgumentException("Customer contact data exceeds its maximum length.");
		if (customer.BillingAddress?.Length > 2000 || customer.ShippingAddress?.Length > 2000) throw new ArgumentException("Customer address exceeds its maximum length.");
	}

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
