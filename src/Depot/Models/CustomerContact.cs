// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum CustomerContactRole
{
	General = 1,
	Commercial = 2,
	Purchasing = 3,
	Logistics = 4,
	Accounting = 5,
	Technical = 6
}

public sealed class CustomerContact
{
	public long Id { get; set; }
	public long CustomerId { get; set; }
	public string Name { get; set; } = string.Empty;
	public CustomerContactRole Role { get; set; } = CustomerContactRole.General;
	public string? Department { get; set; }
	public string? Email { get; set; }
	public string? Phone { get; set; }
	public string? Mobile { get; set; }
	public bool IsPrimary { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
}
