// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

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
}

internal abstract class GlobalSearchProviderBase(GlobalSearchReadRepository read, IAuthorizationService authorization)
{
	protected GlobalSearchReadRepository Read { get; } = read;
	protected IAuthorizationService Authorization { get; } = authorization;

	protected static GlobalSearchResult Result(
		GlobalSearchReadRow row,
		GlobalSearchResultKind kind,
		string group,
		string typeLabel,
		string stablePrefix) =>
		new($"{stablePrefix}:{row.Id}", kind, row.Id, row.Primary, row.Secondary ?? group, group, typeLabel, row.Rank);
}

internal sealed class ItemGlobalSearchProvider(GlobalSearchReadRepository read, IAuthorizationService authorization)
	: GlobalSearchProviderBase(read, authorization), IGlobalSearchProvider
{
	public string Id => "items";
	public bool CanSearch => Authorization.HasPermission(ApplicationPermission.ItemsView);

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!CanSearch) return [];
		var rows = await Read.SearchItemsAsync(query, maxResults, cancellationToken);
		return rows.Select(row => Result(row, GlobalSearchResultKind.Item, "Items", "ITEM", "item")).ToArray();
	}
}

internal sealed class SupplierGlobalSearchProvider(GlobalSearchReadRepository read, IAuthorizationService authorization)
	: GlobalSearchProviderBase(read, authorization), IGlobalSearchProvider
{
	public string Id => "suppliers";
	public bool CanSearch => Authorization.HasPermission(ApplicationPermission.SuppliersView);

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!CanSearch) return [];
		var rows = await Read.SearchSuppliersAsync(query, maxResults, cancellationToken);
		return rows.Select(row => Result(row, GlobalSearchResultKind.Supplier, "Suppliers", "SUPPLIER", "supplier")).ToArray();
	}
}

internal sealed class PurchaseOrderGlobalSearchProvider(GlobalSearchReadRepository read, IAuthorizationService authorization)
	: GlobalSearchProviderBase(read, authorization), IGlobalSearchProvider
{
	public string Id => "purchase-orders";
	public bool CanSearch => Authorization.HasPermission(ApplicationPermission.PurchaseOrdersView);

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!CanSearch) return [];
		var rows = await Read.SearchPurchaseOrdersAsync(query, maxResults, cancellationToken);
		return rows.Select(row => Result(row, GlobalSearchResultKind.PurchaseOrder, "Purchase Orders", "PO", "purchase-order")).ToArray();
	}
}

internal sealed class SalesGlobalSearchProvider(GlobalSearchReadRepository read, IAuthorizationService authorization)
	: GlobalSearchProviderBase(read, authorization), IGlobalSearchProvider
{
	public string Id => "sales";
	public bool CanSearch => Authorization.HasAnyPermission(ApplicationPermission.CustomersView, ApplicationPermission.SalesOrdersView, ApplicationPermission.SalesInvoicesView);

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!CanSearch) return [];
		var perType = Math.Clamp((maxResults + 2) / 3, 2, 6);
		var customerTask = Authorization.HasPermission(ApplicationPermission.CustomersView)
			? Read.SearchCustomersAsync(query, perType, cancellationToken)
			: Task.FromResult<IReadOnlyList<GlobalSearchReadRow>>([]);
		var orderTask = Authorization.HasPermission(ApplicationPermission.SalesOrdersView)
			? Read.SearchSalesOrdersAsync(query, perType, cancellationToken)
			: Task.FromResult<IReadOnlyList<GlobalSearchReadRow>>([]);
		var invoiceTask = Authorization.HasPermission(ApplicationPermission.SalesInvoicesView)
			? Read.SearchInvoicesAsync(query, perType, cancellationToken)
			: Task.FromResult<IReadOnlyList<GlobalSearchReadRow>>([]);
		await Task.WhenAll(customerTask, orderTask, invoiceTask);

		return (await customerTask).Select(row => Result(row, GlobalSearchResultKind.Customer, "Customers", "CUSTOMER", "customer"))
			.Concat((await orderTask).Select(row => Result(row, GlobalSearchResultKind.SalesOrder, "Sales Orders", "SO", "sales-order")))
			.Concat((await invoiceTask).Select(row => Result(row, GlobalSearchResultKind.Invoice, "Sales Invoices", "INVOICE", "invoice")))
			.OrderBy(value => value.Rank)
			.ThenBy(value => (int)value.Kind)
			.ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
			.Take(maxResults)
			.ToArray();
	}
}

internal sealed class SalesCrmGlobalSearchProvider(SalesCrmService crm, IAuthorizationService authorization) : IGlobalSearchProvider
{
	public string Id => "sales-crm";
	public bool CanSearch => authorization.HasPermission(ApplicationPermission.SalesCrmView);

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!CanSearch) return [];
		var perType = Math.Clamp((maxResults + 1) / 2, 2, 8);
		var leadsTask = crm.SearchLeadsAsync(query, pageNumber: 1, pageSize: perType, cancellationToken: cancellationToken);
		var opportunitiesTask = crm.SearchOpportunitiesAsync(query, pageNumber: 1, pageSize: perType, cancellationToken: cancellationToken);
		await Task.WhenAll(leadsTask, opportunitiesTask);

		var leads = leadsTask.Result.Items.Select(value => new GlobalSearchResult(
			$"sales-lead:{value.Id}",
			GlobalSearchResultKind.Lead,
			value.Id,
			value.LeadNumber,
			value.DisplayName,
			"Sales Leads",
			"LEAD",
			GlobalSearchRanking.Calculate(query, value.LeadNumber, value.CompanyName, value.PersonName, value.Email, value.Phone)));
		var opportunities = opportunitiesTask.Result.Items.Select(value => new GlobalSearchResult(
			$"sales-opportunity:{value.Id}",
			GlobalSearchResultKind.Opportunity,
			value.Id,
			value.OpportunityNumber,
			value.CustomerName,
			"Sales Opportunities",
			"OPPORTUNITY",
			GlobalSearchRanking.Calculate(query, value.OpportunityNumber, value.CustomerName, value.StageName)));

		return leads.Concat(opportunities)
			.OrderBy(value => value.Rank)
			.ThenBy(value => (int)value.Kind)
			.ThenBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
			.Take(maxResults)
			.ToArray();
	}
}

internal sealed class FinanceJournalGlobalSearchProvider(GlobalSearchReadRepository read, IAuthorizationService authorization)
	: GlobalSearchProviderBase(read, authorization), IGlobalSearchProvider
{
	public string Id => "finance-journals";
	public bool CanSearch =>
		Authorization.HasPermission(ApplicationPermission.FinanceGeneralLedgerView) &&
		Authorization.HasPermission(ApplicationPermission.FinanceFinancialReportingView);

	public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
	{
		if (!CanSearch) return [];
		var rows = await Read.SearchFinanceJournalsAsync(query, maxResults, cancellationToken);
		return rows.Select(row => Result(row, GlobalSearchResultKind.JournalEntry, "Journal Entries", "JOURNAL", "journal-entry")).ToArray();
	}
}
