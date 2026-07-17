// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class Supplier
{
	public long Id { get; set; }
	public long AccountNumber { get; set; }
	public string? CustomerNumber { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Contact { get; set; }
	public string? Email { get; set; }
	public string? Phone { get; set; }
	public string? Address { get; set; }
	public string? RmaTerms { get; set; }
	public string? Url { get; set; }
	public string? PaymentTerm { get; set; }
	public string? Iban { get; set; }
	public string? AccountName { get; set; }
	public string? SepaMandate { get; set; }
	public string? VatNumber { get; set; }
	public long? SupplierCategoryId { get; set; }
	public string? SupplierCategoryName { get; set; }
	public int Loyalty { get; set; } = 100;
	public int Quality { get; set; } = 100;
	public string? Notes { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
}
