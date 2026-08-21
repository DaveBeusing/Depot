// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public readonly record struct ShellRoute
{
	public ShellRoute(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A shell route is required.", nameof(value));
		Value = value.Trim().ToLowerInvariant();
	}

	public string Value { get; }
	public override string ToString() => Value;

	public static ShellRoute FromName(string name) => new(Normalize(name));

	private static string Normalize(string value)
	{
		var chars = value.Trim().ToLowerInvariant()
			.Select(character => char.IsLetterOrDigit(character) ? character : '-')
			.ToArray();
		return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
	}
}

public static class ShellRoutes
{
	public static readonly ShellRoute Dashboard = new("dashboard");

	public static class Inventory
	{
		public static readonly ShellRoute Module = new("inventory");
		public static readonly ShellRoute Overview = new("inventory.overview");
		public static readonly ShellRoute Items = new("inventory.items");
		public static readonly ShellRoute Movements = new("inventory.movements");
	}

	public static class Warehouse
	{
		public static readonly ShellRoute Module = new("warehouse");
		public static readonly ShellRoute Transfers = new("warehouse.transfers");
		public static readonly ShellRoute InventoryCounts = new("warehouse.inventory-counts");
		public static readonly ShellRoute MaterialIssues = new("warehouse.material-issues");
		public static readonly ShellRoute MaterialReturns = new("warehouse.material-returns");
		public static readonly ShellRoute Shipping = new("sales.shipping");
	}

	public static class Purchasing
	{
		public static readonly ShellRoute Module = new("purchasing");
		public static readonly ShellRoute PurchaseOrders = new("purchasing.purchase-orders");
		public static readonly ShellRoute GoodsReceipts = new("purchasing.goods-receipts");
		public static readonly ShellRoute SupplierReturns = new("purchasing.supplier-returns");
	}

	public static class Sales
	{
		public static readonly ShellRoute Module = new("sales");
		public static readonly ShellRoute Overview = new("sales.overview");
		public static readonly ShellRoute Quotes = new("sales.quotes");
		public static readonly ShellRoute Pricing = new("sales.pricing");
		public static readonly ShellRoute Customers = new("sales.customers");
		public static readonly ShellRoute Orders = new("sales.orders");
		public static readonly ShellRoute Invoices = new("sales.invoices");
	}

	public static class Approvals
	{
		public static readonly ShellRoute Module = new("approvals");
		public static readonly ShellRoute Purchasing = new("approvals.purchase");
		public static readonly ShellRoute Sales = new("approvals.sales");
	}

	public static readonly ShellRoute Reports = new("reports.overview");
	public static readonly ShellRoute Administration = new("administration");
}
