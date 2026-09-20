// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum RolePermissionGroupKind
{
	Sales,
	Purchasing,
	Warehouse,
	Finance,
	AdministrationSecurity
}

public sealed record RolePermissionDescriptor(
	PermissionDefinition Definition,
	RolePermissionGroupKind Group,
	string GroupName,
	string AffectedWorkspace,
	string Description,
	bool IsSensitive)
{
	public ApplicationPermission Permission => Definition.Permission;
	public string Code => Definition.Code;
	public string Module => Definition.Module;
	public string Action => Definition.Action;
	public string Name => Definition.Name;
}

public sealed record RolePermissionMatrixCell(
	Role Role,
	RolePermissionGroupKind Group,
	int DirectPermissionCount,
	int GroupPermissionCount)
{
	public bool IsSystem => Role.IsSystem;
	public bool IsActive => Role.IsActive;
	public bool HasAny => DirectPermissionCount > 0;
	public bool HasAll => GroupPermissionCount > 0 && DirectPermissionCount == GroupPermissionCount;
	public string Summary => $"{DirectPermissionCount}/{GroupPermissionCount}";
	public string AccessibilitySummary => $"{Role.Name}: {DirectPermissionCount} of {GroupPermissionCount} direct permissions in {RolePermissionDesignerProjector.GroupName(Group)}";
}

public sealed record RolePermissionMatrixRow(
	RolePermissionGroupKind Group,
	string GroupName,
	IReadOnlyList<RolePermissionDescriptor> Permissions,
	IReadOnlyList<RolePermissionMatrixCell> Cells);

public sealed record RolePermissionProjection(
	IReadOnlyList<Role> Roles,
	IReadOnlyList<RolePermissionDescriptor> Permissions,
	IReadOnlyList<RolePermissionMatrixRow> MatrixRows);

public sealed record RolePermissionAdvisory(
	string Code,
	string Title,
	string Description,
	ApplicationPermission First,
	ApplicationPermission Second)
{
	public bool IsBlocking => false;
}

public static class RolePermissionDesignerProjector
{
	private static readonly (string Code, string Title, string Description, ApplicationPermission First, ApplicationPermission Second)[] SeparationAdvisories =
	[
		("PURCHASE_CREATE_APPROVE", "Purchase order create / approve", "Creating and approving purchase orders in one role can reduce creator/approver separation.", ApplicationPermission.PurchaseOrdersCreate, ApplicationPermission.PurchaseOrdersApprove),
		("SUPPLIER_INVOICE_CREATE_APPROVE", "Supplier invoice create / approve", "Creating and approving supplier invoices in one role can reduce invoice approval separation.", ApplicationPermission.FinanceSupplierInvoicesCreate, ApplicationPermission.FinanceSupplierInvoicesApprove),
		("PAYMENT_PROPOSAL_CREATE_APPROVE", "Payment proposal create / approve", "Creating and approving payment proposals in one role can reduce Treasury approval separation.", ApplicationPermission.FinancePaymentProposalsCreate, ApplicationPermission.FinancePaymentProposalsApprove),
		("SALES_ORDER_CREATE_APPROVE", "Sales order create / approve", "Creating and approving sales orders in one role can reduce order approval separation.", ApplicationPermission.SalesOrdersCreate, ApplicationPermission.SalesOrdersApprove)
	];

	public static RolePermissionProjection Project(IEnumerable<Role> roles, IEnumerable<PermissionDefinition> definitions)
	{
		ArgumentNullException.ThrowIfNull(roles);
		ArgumentNullException.ThrowIfNull(definitions);
		var roleList = roles
			.OrderByDescending(value => value.IsSystem)
			.ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(value => value.Id)
			.ToArray();
		var descriptors = definitions
			.Select(Describe)
			.OrderBy(value => value.Group)
			.ThenBy(value => value.Module, StringComparer.OrdinalIgnoreCase)
			.ThenBy(value => value.Action, StringComparer.OrdinalIgnoreCase)
			.ThenBy(value => value.Code, StringComparer.Ordinal)
			.ToArray();
		var rows = Enum.GetValues<RolePermissionGroupKind>()
			.Select(group =>
			{
				var groupPermissions = descriptors.Where(value => value.Group == group).ToArray();
				var cells = roleList.Select(role =>
				{
					var selected = role.Permissions.ToHashSet();
					return new RolePermissionMatrixCell(role, group, groupPermissions.Count(value => selected.Contains(value.Permission)), groupPermissions.Length);
				}).ToArray();
				return new RolePermissionMatrixRow(group, GroupName(group), groupPermissions, cells);
			})
			.ToArray();
		return new RolePermissionProjection(roleList, descriptors, rows);
	}

	public static RolePermissionDescriptor Describe(PermissionDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition);
		var group = Group(definition.Permission);
		var sensitive = definition.Action is "Manage" or "Approve" or "Post" or "Reverse" or "Terminate";
		return new RolePermissionDescriptor(
			definition,
			group,
			GroupName(group),
			definition.Module,
			$"{definition.Action} capability for the {definition.Module} workspace.",
			sensitive);
	}

	public static IReadOnlyList<RolePermissionAdvisory> Advisories(IEnumerable<ApplicationPermission> permissions)
	{
		ArgumentNullException.ThrowIfNull(permissions);
		var set = permissions.ToHashSet();
		return SeparationAdvisories
			.Where(value => set.Contains(value.First) && set.Contains(value.Second))
			.Select(value => new RolePermissionAdvisory(value.Code, value.Title, value.Description, value.First, value.Second))
			.ToArray();
	}

	public static RolePermissionGroupKind Group(ApplicationPermission permission)
	{
		var name = permission.ToString();
		if (StartsWithAny(name, "Sales", "Customers", "Shipments", "CustomerReturns", "CreditNotes"))
			return RolePermissionGroupKind.Sales;
		if (StartsWithAny(name, "Purchasing", "PurchaseOrders", "GoodsReceipts", "Suppliers", "SupplierReturns"))
			return RolePermissionGroupKind.Purchasing;
		if (StartsWithAny(name, "Inventory", "Items", "StockMovements", "StockTransfers", "InventoryCounts", "MaterialIssues", "MaterialReturns"))
			return RolePermissionGroupKind.Warehouse;
		if (StartsWithAny(name, "Finance", "Reports"))
			return RolePermissionGroupKind.Finance;
		return RolePermissionGroupKind.AdministrationSecurity;
	}

	public static string GroupName(RolePermissionGroupKind group) => group switch
	{
		RolePermissionGroupKind.Sales => "Sales",
		RolePermissionGroupKind.Purchasing => "Purchasing",
		RolePermissionGroupKind.Warehouse => "Warehouse",
		RolePermissionGroupKind.Finance => "Finance",
		RolePermissionGroupKind.AdministrationSecurity => "Administration / Security",
		_ => throw new ArgumentOutOfRangeException(nameof(group))
	};

	private static bool StartsWithAny(string value, params string[] prefixes) =>
		prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
}


public sealed record RoleEffectivePermissionUserProjection(
	long UserId,
	string DisplayName,
	string Email,
	bool IsActive,
	IReadOnlyList<Role> AssignedRoles,
	IReadOnlySet<ApplicationPermission> EffectivePermissions)
{
	public string AssignedRoleSummary => AssignedRoles.Count == 0 ? "No roles" : string.Join(", ", AssignedRoles.Select(value => value.Name));
	public int EffectivePermissionCount => EffectivePermissions.Count;
	public string Status => IsActive ? "Active" : "Inactive";
}
