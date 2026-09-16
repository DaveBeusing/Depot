// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Models;

namespace Depot.Services;

internal static class GlobalSearchRanking
{
	public static int Calculate(string query, params string?[] values)
	{
		foreach (var value in values)
			if (string.Equals(value?.Trim(), query, StringComparison.OrdinalIgnoreCase)) return 0;
		foreach (var value in values)
			if (value?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true) return 10;
		foreach (var value in values)
			if (value?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Any(term => term.StartsWith(query, StringComparison.OrdinalIgnoreCase)) == true) return 20;
		foreach (var value in values)
			if (value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return 30;
		return 40;
	}

	public static bool Matches(string query, params string?[] values) =>
		values.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
}

internal sealed class ItemGlobalSearchProvider : IGlobalSearchProvider
{
	private readonly ItemService _items;
	private readonly IAuthorizationService _authorization;

	public ItemGlobalSearchProvider(ItemService items, IAuthorizationService authorization)
	{
		_items = items;
		_authorization = authorization;
	}

	public string Id => "items";

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!_authorization.HasPermission(ApplicationPermission.ItemsView)) return [];
		var page = await _items.SearchItemMasterDataAsync(query, null, 1, maxResults, cancellationToken);
		return page.Items.Select(item => new GlobalSearchResult(
			$"item:{item.Id}",
			GlobalSearchResultKind.Item,
			item.Id,
			item.PartNumber,
			item.Description,
			"Items",
			"ITEM",
			GlobalSearchRanking.Calculate(query, item.PartNumber, item.Description)))
			.ToArray();
	}
}

internal sealed class SupplierGlobalSearchProvider : IGlobalSearchProvider
{
	private readonly SupplierService _suppliers;
	private readonly IAuthorizationService _authorization;

	public SupplierGlobalSearchProvider(SupplierService suppliers, IAuthorizationService authorization)
	{
		_suppliers = suppliers;
		_authorization = authorization;
	}

	public string Id => "suppliers";

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!_authorization.HasPermission(ApplicationPermission.SuppliersView)) return [];
		var suppliers = await _suppliers.SearchActiveAsync(query, maxResults, cancellationToken);
		return suppliers.Select(supplier =>
		{
			var account = supplier.AccountNumber > 0 ? supplier.AccountNumber.ToString(CultureInfo.InvariantCulture) : null;
			var subtitle = account is null ? supplier.CustomerNumber ?? "Supplier" : $"Account {account}";
			return new GlobalSearchResult(
				$"supplier:{supplier.Id}",
				GlobalSearchResultKind.Supplier,
				supplier.Id,
				supplier.Name,
				subtitle,
				"Suppliers",
				"SUPPLIER",
				GlobalSearchRanking.Calculate(query, supplier.Name, account, supplier.CustomerNumber));
		}).ToArray();
	}
}

internal sealed class PurchaseOrderGlobalSearchProvider : IGlobalSearchProvider
{
	private readonly PurchaseOrderService _orders;
	private readonly IAuthorizationService _authorization;

	public PurchaseOrderGlobalSearchProvider(PurchaseOrderService orders, IAuthorizationService authorization)
	{
		_orders = orders;
		_authorization = authorization;
	}

	public string Id => "purchase-orders";

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!_authorization.HasPermission(ApplicationPermission.PurchaseOrdersView)) return [];
		var page = await _orders.SearchAsync(query, null, 1, maxResults, cancellationToken);
		return page.Items.Select(order => new GlobalSearchResult(
			$"purchase-order:{order.Id}",
			GlobalSearchResultKind.PurchaseOrder,
			order.Id,
			order.OrderNumber,
			order.SupplierName ?? "Purchase order",
			"Purchase Orders",
			"PO",
			GlobalSearchRanking.Calculate(query, order.OrderNumber, order.SupplierName)))
			.ToArray();
	}
}

internal sealed class SalesGlobalSearchProvider : IGlobalSearchProvider
{
	private readonly SalesServices _sales;
	private readonly IAuthorizationService _authorization;

	public SalesGlobalSearchProvider(SalesServices sales, IAuthorizationService authorization)
	{
		_sales = sales;
		_authorization = authorization;
	}

	public string Id => "sales";

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		var perType = Math.Clamp((maxResults + 2) / 3, 2, 6);
		var tasks = new[]
		{
			SearchCustomersAsync(query, perType, cancellationToken),
			SearchOrdersAsync(query, perType, cancellationToken),
			SearchInvoicesAsync(query, perType, cancellationToken)
		};
		var results = await Task.WhenAll(tasks);
		return results.SelectMany(value => value)
			.OrderBy(value => value.Rank)
			.ThenBy(value => (int)value.Kind)
			.ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
			.Take(maxResults)
			.ToArray();
	}

	private async Task<IReadOnlyList<GlobalSearchResult>> SearchCustomersAsync(string query, int maxResults, CancellationToken cancellationToken)
	{
		if (!_authorization.HasPermission(ApplicationPermission.CustomersView)) return [];
		var page = await _sales.Customers.SearchAsync(query, false, 1, maxResults, cancellationToken);
		return page.Items.Select(customer => new GlobalSearchResult(
			$"customer:{customer.Id}",
			GlobalSearchResultKind.Customer,
			customer.Id,
			customer.Name,
			customer.CustomerNumber,
			"Customers",
			"CUSTOMER",
			GlobalSearchRanking.Calculate(query, customer.CustomerNumber, customer.Name)))
			.ToArray();
	}

	private async Task<IReadOnlyList<GlobalSearchResult>> SearchOrdersAsync(string query, int maxResults, CancellationToken cancellationToken)
	{
		if (!_authorization.HasPermission(ApplicationPermission.SalesOrdersView)) return [];
		var page = await _sales.Orders.SearchAsync(query, null, 1, maxResults, cancellationToken);
		return page.Items.Select(order => new GlobalSearchResult(
			$"sales-order:{order.Id}",
			GlobalSearchResultKind.SalesOrder,
			order.Id,
			order.OrderNumber,
			order.CustomerName ?? "Sales order",
			"Sales Orders",
			"SO",
			GlobalSearchRanking.Calculate(query, order.OrderNumber, order.CustomerName)))
			.ToArray();
	}

	private async Task<IReadOnlyList<GlobalSearchResult>> SearchInvoicesAsync(string query, int maxResults, CancellationToken cancellationToken)
	{
		if (!_authorization.HasPermission(ApplicationPermission.SalesInvoicesView)) return [];
		var page = await _sales.Invoices.SearchAsync(query, null, 1, maxResults, cancellationToken);
		return page.Items.Select(invoice => new GlobalSearchResult(
			$"invoice:{invoice.Id}",
			GlobalSearchResultKind.Invoice,
			invoice.Id,
			invoice.InvoiceNumber,
			invoice.CustomerName ?? "Sales invoice",
			"Sales Invoices",
			"INVOICE",
			GlobalSearchRanking.Calculate(query, invoice.InvoiceNumber, invoice.CustomerName)))
			.ToArray();
	}
}

internal sealed class FinanceJournalGlobalSearchProvider : IGlobalSearchProvider
{
	private readonly FinanceGeneralLedgerService _ledger;
	private readonly IAuthorizationService _authorization;

	public FinanceJournalGlobalSearchProvider(FinanceGeneralLedgerService ledger, IAuthorizationService authorization)
	{
		_ledger = ledger;
		_authorization = authorization;
	}

	public string Id => "finance-journals";

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!_authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerView) ||
			!_authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView)) return [];

		if (long.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0)
		{
			var entry = await _ledger.GetByIdAsync(entryId, cancellationToken);
			if (entry is not null)
				return [CreateResult(query, entry.Id, entry.EntryNumber, entry.PostingDate, entry.Description, entry.SourceId, entry.SourceReference, 0)];
		}

		var searchWindow = Math.Clamp(maxResults * 8, 24, 100);
		var page = await _ledger.SearchAsync(pageNumber: 1, pageSize: searchWindow, cancellationToken: cancellationToken);
		return page.Items
			.Where(entry => GlobalSearchRanking.Matches(query, entry.EntryNumber, entry.SourceId, entry.SourceReference, entry.Description))
			.Select(entry => CreateResult(
				query,
				entry.Id,
				entry.EntryNumber,
				entry.PostingDate,
				entry.Description,
				entry.SourceId,
				entry.SourceReference,
				GlobalSearchRanking.Calculate(query, entry.EntryNumber, entry.SourceReference, entry.SourceId, entry.Description)))
			.OrderBy(entry => entry.Rank)
			.ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
			.Take(maxResults)
			.ToArray();
	}

	private static GlobalSearchResult CreateResult(
		string query,
		long id,
		string entryNumber,
		DateOnly postingDate,
		string description,
		string sourceId,
		string? sourceReference,
		int rank) =>
		new(
			$"journal-entry:{id}",
			GlobalSearchResultKind.JournalEntry,
			id,
			entryNumber,
			$"{postingDate:yyyy-MM-dd} · {description}",
			"Journal Entries",
			"JOURNAL",
			rank);
}
