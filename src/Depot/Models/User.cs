// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class User
{
	public long Id { get; set; }

	public string Email { get; set; }
		= string.Empty;

	public string DisplayName { get; set; }
		= string.Empty;

	public bool IsAdministrator { get; set; }

	public bool CanApprovePurchaseOrders { get; set; }

	public UserRole Role { get; set; }

	public IReadOnlyList<Role> Roles { get; set; } = [];

	public IReadOnlySet<ApplicationPermission> EffectivePermissions { get; set; }
		= new HashSet<ApplicationPermission>();

	public bool IsActive { get; set; }

	public DateTime CreatedUtc { get; set; }

	public long Version { get; set; } = 1;
}
