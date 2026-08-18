// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum CustomerAddressType
{
	Billing = 1,
	Shipping = 2,
	Other = 3
}

public sealed class CustomerAddress
{
	public long Id { get; set; }
	public long CustomerId { get; set; }
	public CustomerAddressType Type { get; set; }
	public string? Name { get; set; }
	public string Address { get; set; } = string.Empty;
	public bool IsDefault { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
}

public sealed class Customer
{
	public long Id { get; set; }
	public string CustomerNumber { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? BillingAddress { get; set; }
	public string? ShippingAddress { get; set; }
	public string? ContactName { get; set; }
	public string? Email { get; set; }
	public string? Phone { get; set; }
	public string? TaxId { get; set; }
	public int PaymentTermsDays { get; set; } = 30;
	public string Currency { get; set; } = "EUR";
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
	public IReadOnlyList<CustomerAddress> Addresses { get; set; } = [];
}
