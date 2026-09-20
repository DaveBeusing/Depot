// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum PricingStrategyNodeKind
{
	GlobalGroup,
	GlobalPriceList,
	RegionGroup,
	Region,
	RegionalPriceList,
	CustomerGroup,
	CustomerPriceList,
	CustomerAssignment
}

public sealed record PricingStrategyNode(
	string Key,
	PricingStrategyNodeKind Kind,
	string Title,
	string Subtitle,
	bool IsActive,
	long? PriceListId = null,
	long? RegionId = null,
	long? CustomerId = null,
	SalesPriceListScope? Scope = null,
	IReadOnlyList<PricingStrategyNode>? Children = null)
{
	public IReadOnlyList<PricingStrategyNode> ChildNodes { get; } = Children ?? [];
	public string State => IsActive ? "Active" : "Inactive";
}

public sealed record PricingStrategyRow(PricingStrategyNode Node, int Depth)
{
	public string Key => Node.Key;
	public PricingStrategyNodeKind Kind => Node.Kind;
	public string Title => Node.Title;
	public string Subtitle => Node.Subtitle;
	public string State => Node.State;
	public bool IsActive => Node.IsActive;
	public long? PriceListId => Node.PriceListId;
	public long? RegionId => Node.RegionId;
	public long? CustomerId => Node.CustomerId;
	public SalesPriceListScope? Scope => Node.Scope;
	public double Indent => Depth * 16d;
}

public sealed record PricingStrategyProjection(
	IReadOnlyList<PricingStrategyNode> Roots,
	IReadOnlyList<PricingStrategyRow> Rows)
{
	public int NodeCount => Rows.Count;
}

public static class PricingStrategyProjector
{
	public static PricingStrategyProjection Project(
		IEnumerable<SalesPriceList> priceLists,
		IEnumerable<SalesRegion> regions,
		IEnumerable<Customer> customers,
		IEnumerable<CustomerPriceListAssignment> assignments)
	{
		ArgumentNullException.ThrowIfNull(priceLists);
		ArgumentNullException.ThrowIfNull(regions);
		ArgumentNullException.ThrowIfNull(customers);
		ArgumentNullException.ThrowIfNull(assignments);

		var lists = priceLists.OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Code, StringComparer.OrdinalIgnoreCase).ToArray();
		var regionValues = regions.OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(value => value.Code, StringComparer.OrdinalIgnoreCase).ToArray();
		var customerValues = customers.ToDictionary(value => value.Id);
		var assignmentValues = assignments
			.GroupBy(value => value.SalesPriceListId)
			.ToDictionary(group => group.Key, group => group.OrderBy(value => CustomerName(value.CustomerId, customerValues), StringComparer.CurrentCultureIgnoreCase).ToArray());

		var globalLists = lists
			.Where(value => value.Scope == SalesPriceListScope.Global)
			.Select(PriceListNode)
			.ToArray();

		var regionNodes = regionValues
			.Select(region =>
			{
				var regionalLists = lists
					.Where(value => value.Scope == SalesPriceListScope.Region && value.RegionId == region.Id)
					.Select(PriceListNode)
					.ToArray();
				return new PricingStrategyNode(
					$"region:{region.Id}",
					PricingStrategyNodeKind.Region,
					region.Name,
					$"{region.Code} · {regionalLists.Length} price list{(regionalLists.Length == 1 ? string.Empty : "s")}",
					region.IsActive,
					RegionId: region.Id,
					Children: regionalLists);
			})
			.ToArray();

		var customerLists = lists
			.Where(value => value.Scope == SalesPriceListScope.Customer)
			.Select(list =>
			{
				assignmentValues.TryGetValue(list.Id, out var listAssignments);
				listAssignments ??= [];
				var assignmentNodes = listAssignments.Select(assignment =>
				{
					customerValues.TryGetValue(assignment.CustomerId, out var customer);
					var subtitle = customer is null
						? $"Customer #{assignment.CustomerId}"
						: string.IsNullOrWhiteSpace(customer.SalesRegionName)
							? customer.CustomerNumber
							: $"{customer.CustomerNumber} · {customer.SalesRegionName}";
					return new PricingStrategyNode(
						$"assignment:{assignment.CustomerId}:{list.Id}",
						PricingStrategyNodeKind.CustomerAssignment,
						customer?.Name ?? $"Customer #{assignment.CustomerId}",
						subtitle,
						assignment.IsActive && (customer?.IsActive ?? true),
						PriceListId: list.Id,
						RegionId: customer?.SalesRegionId,
						CustomerId: assignment.CustomerId,
						Scope: SalesPriceListScope.Customer);
				}).ToArray();

				return new PricingStrategyNode(
					$"list:{list.Id}",
					PricingStrategyNodeKind.CustomerPriceList,
					list.Name,
					$"{list.Code} · {list.Currency} · {assignmentNodes.Length} assignment{(assignmentNodes.Length == 1 ? string.Empty : "s")}",
					list.IsActive,
					PriceListId: list.Id,
					Scope: list.Scope,
					Children: assignmentNodes);
			})
			.ToArray();

		var roots = new PricingStrategyNode[]
		{
			new("group:global", PricingStrategyNodeKind.GlobalGroup, "Global pricing", $"{globalLists.Length} global price list{(globalLists.Length == 1 ? string.Empty : "s")}", true, Scope: SalesPriceListScope.Global, Children: globalLists),
			new("group:regions", PricingStrategyNodeKind.RegionGroup, "Regional pricing", $"{regionNodes.Length} sales region{(regionNodes.Length == 1 ? string.Empty : "s")}", true, Scope: SalesPriceListScope.Region, Children: regionNodes),
			new("group:customers", PricingStrategyNodeKind.CustomerGroup, "Customer pricing", $"{customerLists.Length} customer price list{(customerLists.Length == 1 ? string.Empty : "s")}", true, Scope: SalesPriceListScope.Customer, Children: customerLists)
		};
		var rows = new List<PricingStrategyRow>();
		foreach (var root in roots) Flatten(root, 0, rows);
		return new PricingStrategyProjection(roots, rows);
	}

	private static PricingStrategyNode PriceListNode(SalesPriceList list) =>
		new(
			$"list:{list.Id}",
			list.Scope == SalesPriceListScope.Global ? PricingStrategyNodeKind.GlobalPriceList : PricingStrategyNodeKind.RegionalPriceList,
			list.Name,
			list.Scope == SalesPriceListScope.Region
				? $"{list.Code} · {list.Currency} · {list.RegionName ?? "Region"}"
				: $"{list.Code} · {list.Currency}",
			list.IsActive,
			PriceListId: list.Id,
			RegionId: list.RegionId,
			Scope: list.Scope);

	private static void Flatten(PricingStrategyNode node, int depth, ICollection<PricingStrategyRow> rows)
	{
		rows.Add(new PricingStrategyRow(node, depth));
		foreach (var child in node.ChildNodes) Flatten(child, depth + 1, rows);
	}

	private static string CustomerName(long customerId, IReadOnlyDictionary<long, Customer> customers) =>
		customers.TryGetValue(customerId, out var customer) ? customer.Name : $"Customer #{customerId}";
}


public sealed record SalesPriceResolutionPreview(
	long? CustomerId,
	long ItemId,
	DateTime EffectiveDate,
	string Currency,
	IReadOnlyList<SalesPriceResult> Candidates)
{
	public SalesPriceResult? Effective => Candidates.FirstOrDefault();
	public SalesPriceResult? Customer => Candidates.FirstOrDefault(value => value.Scope == SalesPriceListScope.Customer);
	public SalesPriceResult? Region => Candidates.FirstOrDefault(value => value.Scope == SalesPriceListScope.Region);
	public SalesPriceResult? Global => Candidates.FirstOrDefault(value => value.Scope == SalesPriceListScope.Global);
}

public sealed record PricingResolutionPreviewRow(
	string Layer,
	string Source,
	decimal? Amount,
	string Currency,
	string Detail,
	bool IsAvailable,
	bool IsEffective);
