// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class ShellArchitectureRegressionTests
{
	[Fact]
	public void PublishedShellRoutesAreUnique()
	{
		var routes = new[]
		{
			ShellRoutes.Dashboard,
			ShellRoutes.Inventory.Module, ShellRoutes.Inventory.Overview, ShellRoutes.Inventory.Items, ShellRoutes.Inventory.Movements,
			ShellRoutes.Warehouse.Module, ShellRoutes.Warehouse.Transfers, ShellRoutes.Warehouse.InventoryCounts, ShellRoutes.Warehouse.MaterialIssues, ShellRoutes.Warehouse.MaterialReturns, ShellRoutes.Warehouse.Shipping,
			ShellRoutes.Purchasing.Module, ShellRoutes.Purchasing.PurchaseOrders, ShellRoutes.Purchasing.GoodsReceipts, ShellRoutes.Purchasing.SupplierReturns,
			ShellRoutes.Sales.Module, ShellRoutes.Sales.Overview, ShellRoutes.Sales.Quotes, ShellRoutes.Sales.Pricing, ShellRoutes.Sales.Customers, ShellRoutes.Sales.Orders, ShellRoutes.Sales.Invoices,
			ShellRoutes.Approvals.Module, ShellRoutes.Approvals.Purchasing, ShellRoutes.Approvals.Sales,
			ShellRoutes.Reports, ShellRoutes.Administration
		};

		Assert.Equal(routes.Length, routes.Select(route => route.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
	}

	[Fact]
	public void SharedDocumentContentIsNotDisposedWhenTabCloses()
	{
		var shared = new DisposableViewModel();
		var document = WorkspaceDocumentFactory.Create(
			new WorkspaceDocumentDescriptor("sales-order:1", "SO-000001", ShellRoutes.Sales.Orders, string.Empty, "sales.orders"),
			() => shared,
			(_, _) => Task.CompletedTask,
			ownsContent: false);

		_ = document.Content;
		document.Dispose();

		Assert.False(shared.IsDisposed);
	}

	[Fact]
	public void DocumentKeysAreStableAcrossRepeatedOpenRequests()
	{
		using var first = WorkspaceDocumentFactory.Create(
			new WorkspaceDocumentDescriptor("customer:17", "Customer", ShellRoutes.Sales.Customers, string.Empty, "sales.customers"),
			() => new DisposableViewModel(),
			(_, _) => Task.CompletedTask);
		using var second = WorkspaceDocumentFactory.Create(
			new WorkspaceDocumentDescriptor("customer:17", "Renamed Customer", ShellRoutes.Sales.Customers, string.Empty, "sales.customers"),
			() => new DisposableViewModel(),
			(_, _) => Task.CompletedTask);

		Assert.Equal(first.TabKey, second.TabKey);
		Assert.Equal(first.Route, second.Route);
	}

	private sealed class DisposableViewModel : BaseViewModel, IDisposable
	{
		public bool IsDisposed { get; private set; }
		public void Dispose() => IsDisposed = true;
	}
}
