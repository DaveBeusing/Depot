// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public sealed class AuthorizationService
{
	private static readonly IReadOnlySet<ApplicationPermission> PurchasingPermissions = new HashSet<ApplicationPermission>
	{
		ApplicationPermission.PurchaseOrdersCreate,
		ApplicationPermission.PurchaseOrdersEdit,
		ApplicationPermission.PurchaseOrdersSubmit,
		ApplicationPermission.PurchaseOrdersOrder,
		ApplicationPermission.PurchaseOrdersClose
	};
	private static readonly IReadOnlySet<ApplicationPermission> ApproverPermissions = new HashSet<ApplicationPermission>
	{
		ApplicationPermission.PurchaseOrdersApprove
	};
	private static readonly IReadOnlySet<ApplicationPermission> WarehousePermissions = new HashSet<ApplicationPermission>
	{
		ApplicationPermission.MaterialIssuesCreate,
		ApplicationPermission.MaterialIssuesPost,
		ApplicationPermission.MaterialIssuesReverse,
		ApplicationPermission.MaterialReturnsCreate,
		ApplicationPermission.MaterialReturnsPost,
		ApplicationPermission.SupplierReturnsCreate,
		ApplicationPermission.SupplierReturnsPost
	};

	public User? CurrentUser { get; private set; }
	public bool IsLoggedIn => CurrentUser is not null;

	public void SignIn(User user) => CurrentUser = user;
	public void SignOut() => CurrentUser = null;

	public bool HasPermission(ApplicationPermission permission)
	{
		if (CurrentUser is not { IsActive: true } user) return false;
		return EffectiveRole(user) switch
		{
			UserRole.Administrator => true,
			UserRole.Purchasing => PurchasingPermissions.Contains(permission),
			UserRole.Approver => ApproverPermissions.Contains(permission),
			UserRole.WarehouseOperator => WarehousePermissions.Contains(permission),
			_ => false
		};
	}

	public void RequirePermission(ApplicationPermission permission)
	{
		if (!HasPermission(permission))
			throw new UnauthorizedAccessException($"The current user does not have the '{PermissionCode(permission)}' permission.");
	}

	public bool HasAnyPermission(params ApplicationPermission[] permissions) => permissions.Any(HasPermission);
	public bool CanManageUsers() => EffectiveRole(CurrentUser) == UserRole.Administrator;
	public bool CanImport() => CanManageUsers();
	public bool CanManageMasterData() => CanManageUsers();
	public bool CanManageDatabase() => CanManageUsers();
	public bool CanOpenSettings() => CanManageUsers();
	public bool CanViewAuditLog() => CanManageUsers();
	public bool CanApprovePurchaseOrders() => HasPermission(ApplicationPermission.PurchaseOrdersApprove);

	public static UserRole EffectiveRole(User? user)
	{
		if (user is null) return UserRole.User;
		if (user.IsAdministrator) return UserRole.Administrator;
		if (user.Role != UserRole.User) return user.Role;
		return user.CanApprovePurchaseOrders ? UserRole.Approver : UserRole.User;
	}

	public static string PermissionCode(ApplicationPermission permission) => permission switch
	{
		ApplicationPermission.PurchaseOrdersCreate => "PurchaseOrders.Create",
		ApplicationPermission.PurchaseOrdersEdit => "PurchaseOrders.Edit",
		ApplicationPermission.PurchaseOrdersSubmit => "PurchaseOrders.Submit",
		ApplicationPermission.PurchaseOrdersApprove => "PurchaseOrders.Approve",
		ApplicationPermission.PurchaseOrdersOrder => "PurchaseOrders.Order",
		ApplicationPermission.PurchaseOrdersClose => "PurchaseOrders.Close",
		ApplicationPermission.MaterialIssuesCreate => "MaterialIssues.Create",
		ApplicationPermission.MaterialIssuesPost => "MaterialIssues.Post",
		ApplicationPermission.MaterialIssuesReverse => "MaterialIssues.Reverse",
		ApplicationPermission.MaterialReturnsCreate => "MaterialReturns.Create",
		ApplicationPermission.MaterialReturnsPost => "MaterialReturns.Post",
		ApplicationPermission.SupplierReturnsCreate => "SupplierReturns.Create",
		ApplicationPermission.SupplierReturnsPost => "SupplierReturns.Post",
		_ => permission.ToString()
	};
}
