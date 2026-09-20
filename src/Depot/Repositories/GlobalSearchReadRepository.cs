// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;

namespace Depot.Repositories;

internal sealed record GlobalSearchReadRow(long Id, string Primary, string? Secondary, int Rank);

internal sealed class GlobalSearchReadRepository : DatabaseRepository
{
	public GlobalSearchReadRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchItemsAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "i.PartNumber", "i.Description" };
		var containsColumns = new[] { "i.PartNumber", "i.Description", "m.Name", "c.Name", "i.Gtin", "i.Revision", "i.Model", "i.ProductFamily" };
		var predicate = search.BuildPredicate(prefixColumns, containsColumns);
		if (search.AllowContains)
			predicate = $"({predicate} OR EXISTS (SELECT 1 FROM SupplierItems si INNER JOIN Suppliers s ON s.Id = si.SupplierId WHERE si.ItemId = i.Id AND si.IsActive = 1 AND (s.Name LIKE $SearchContains OR si.SupplierPartNumber LIKE $SearchContains)))";
		var rank = search.BuildRankExpression(prefixColumns, ["i.Description", "m.Name", "c.Name"]);
		return Database.QuerySliceAsync(
			$"""
			SELECT i.Id, i.PartNumber, i.Description, {rank} AS SearchRank
			FROM Items i
			LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId
			LEFT JOIN Categories c ON c.Id = i.CategoryId
			WHERE {predicate}
			ORDER BY SearchRank, i.PartNumber, i.Id
			""",
			ReadRow,
			0,
			count,
			cancellationToken,
			SearchParameters(search));
	}

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchSuppliersAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "s.CustomerNumber", "s.Name" };
		var predicate = search.BuildPredicate(prefixColumns, ["s.CustomerNumber", "s.Name", "s.Contact", "s.Email", "s.Phone", "s.VatNumber"]);
		var rank = search.BuildRankExpression(prefixColumns, ["s.Name"]);
		var parameters = SearchParameters(search).ToList();
		if (long.TryParse(search.Query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var accountNumber))
		{
			predicate = $"(s.AccountNumber = $AccountNumber OR {predicate})";
			rank = $"CASE WHEN s.AccountNumber = $AccountNumber THEN 0 ELSE ({rank}) END";
			parameters.Add(Parameter("$AccountNumber", accountNumber));
		}
		return Database.QuerySliceAsync(
			$"SELECT s.Id, s.Name, s.AccountNumber, s.CustomerNumber, {rank} AS SearchRank FROM Suppliers s WHERE s.IsActive = 1 AND {predicate} ORDER BY SearchRank, s.Name, s.Id",
			ReadSupplierRow,
			0,
			count,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchCustomersAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "CustomerNumber", "Name" };
		var predicate = search.BuildPredicate(prefixColumns, ["CustomerNumber", "Name", "Email", "TaxId", "VatId"]);
		var rank = search.BuildRankExpression(prefixColumns, ["Name"]);
		return Database.QuerySliceAsync(
			$"SELECT Id, Name, CustomerNumber, {rank} AS SearchRank FROM Customers WHERE IsActive = 1 AND {predicate} ORDER BY SearchRank, Name, Id",
			ReadRow,
			0,
			count,
			cancellationToken,
			SearchParameters(search));
	}

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchPurchaseOrdersAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "po.OrderNumber", "s.Name" };
		var predicate = search.BuildPredicate(prefixColumns, ["po.OrderNumber", "s.Name", "po.Notes"]);
		var rank = search.BuildRankExpression(prefixColumns, ["s.Name", "po.Notes"]);
		return Database.QuerySliceAsync(
			$"SELECT po.Id, po.OrderNumber, s.Name, {rank} AS SearchRank FROM PurchaseOrders po INNER JOIN Suppliers s ON s.Id = po.SupplierId WHERE {predicate} ORDER BY SearchRank, po.OrderDate DESC, po.Id DESC",
			ReadRow,
			0,
			count,
			cancellationToken,
			SearchParameters(search));
	}

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchSalesOrdersAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "so.OrderNumber", "c.Name" };
		var predicate = search.BuildPredicate(prefixColumns, ["so.OrderNumber", "c.Name", "so.CustomerReference"]);
		var rank = search.BuildRankExpression(prefixColumns, ["c.Name", "so.CustomerReference"]);
		return Database.QuerySliceAsync(
			$"SELECT so.Id, so.OrderNumber, c.Name, {rank} AS SearchRank FROM SalesOrders so INNER JOIN Customers c ON c.Id = so.CustomerId WHERE {predicate} ORDER BY SearchRank, so.OrderDate DESC, so.Id DESC",
			ReadRow,
			0,
			count,
			cancellationToken,
			SearchParameters(search));
	}

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchInvoicesAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "si.InvoiceNumber", "so.OrderNumber", "sh.ShipmentNumber", "c.Name" };
		var predicate = search.BuildPredicate(prefixColumns, prefixColumns);
		var rank = search.BuildRankExpression(prefixColumns, ["c.Name"]);
		return Database.QuerySliceAsync(
			$"""
			SELECT si.Id, si.InvoiceNumber, c.Name, {rank} AS SearchRank
			FROM SalesInvoices si
			INNER JOIN Customers c ON c.Id = si.CustomerId
			INNER JOIN SalesOrders so ON so.Id = si.SalesOrderId
			INNER JOIN Shipments sh ON sh.Id = si.ShipmentId
			WHERE {predicate}
			ORDER BY SearchRank, si.InvoiceDate DESC, si.Id DESC
			""",
			ReadRow,
			0,
			count,
			cancellationToken,
			SearchParameters(search));
	}

	public Task<IReadOnlyList<GlobalSearchReadRow>> SearchFinanceJournalsAsync(string query, int count, CancellationToken cancellationToken)
	{
		var search = RequirePlan(query);
		var prefixColumns = new[] { "EntryNumber", "SourceId", "SourceReference", "Description" };
		var predicate = search.BuildPredicate(prefixColumns, prefixColumns);
		var rank = search.BuildRankExpression(prefixColumns, ["Description"]);
		var parameters = SearchParameters(search).ToList();
		if (long.TryParse(search.Query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0)
		{
			predicate = $"(Id = $EntryId OR {predicate})";
			rank = $"CASE WHEN Id = $EntryId THEN 0 ELSE ({rank}) END";
			parameters.Add(Parameter("$EntryId", entryId));
		}
		return Database.QuerySliceAsync(
			$"SELECT Id, EntryNumber, PostingDate, Description, {rank} AS SearchRank FROM FinanceJournalEntries WHERE {predicate} ORDER BY SearchRank, PostingDate DESC, Id DESC",
			ReadFinanceRow,
			0,
			count,
			cancellationToken,
			parameters.ToArray());
	}

	private static GlobalSearchReadRow ReadRow(DbDataReader reader) =>
		new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture));

	private static GlobalSearchReadRow ReadSupplierRow(DbDataReader reader)
	{
		var accountNumber = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture);
		var customerNumber = reader.IsDBNull(3) ? null : reader.GetString(3);
		var subtitle = accountNumber > 0 ? $"Account {accountNumber.ToString(CultureInfo.InvariantCulture)}" : customerNumber;
		return new(reader.GetInt64(0), reader.GetString(1), subtitle, Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture));
	}

	private static GlobalSearchReadRow ReadFinanceRow(DbDataReader reader)
	{
		var date = reader.GetValue(2) is DateTime value
			? DateOnly.FromDateTime(value)
			: DateOnly.Parse(Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
		return new(
			reader.GetInt64(0),
			reader.GetString(1),
			$"{date:yyyy-MM-dd} · {reader.GetString(3)}",
			Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture));
	}

	private static SearchQueryPlan RequirePlan(string query) =>
		SearchQueryPlan.Create(query) ?? throw new ArgumentException("A search query is required.", nameof(query));

	private static DatabaseParameter[] SearchParameters(SearchQueryPlan search)
	{
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$SearchExact", search.Exact),
			Parameter("$SearchPrefix", search.Prefix)
		};
		if (search.AllowContains)
		{
			parameters.Add(Parameter("$SearchWordPrefix", search.WordPrefix));
			parameters.Add(Parameter("$SearchContains", search.Contains));
		}
		return parameters.ToArray();
	}
}
