// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public sealed class ReportService
{
	private readonly StockService _stockService;

	public ReportService(
		StockService stockService)
	{
		_stockService =
			stockService;
	}

	public InventoryValueReport GetInventoryValueReport(
		string? searchText)
	{
		var rows =
			_stockService
				.GetInventoryOverview()
				.Where(
					x =>
						MatchesSearch(
							x,
							searchText))
				.Select(
					x =>
						new InventoryValueReportItem
						{
							InventoryId =
								x.InventoryId,

							ItemId =
								x.ItemId,

							PartNumber =
								x.PartNumber,

							Description =
								x.Description,

							Manufacturer =
								x.Manufacturer,

							Category =
								x.Category,

							PurposeName =
								x.PurposeName,

							LocationName =
								x.LocationName,

							CurrentStock =
								x.CurrentStock,

							AverageCost =
								x.AverageCost,

							InventoryValue =
								x.InventoryValue
						})
				.ToList();

		return new InventoryValueReport
		{
			Items =
				rows,

			TotalInventoryRows =
				rows.Count,

			TotalItems =
				rows
					.Select(
						x => x.ItemId)
					.Distinct()
					.Count(),

			TotalStockQuantity =
				rows.Sum(
					x => x.CurrentStock),

			TotalInventoryValue =
				rows.Sum(
					x => x.InventoryValue)
		};
	}

	private static bool MatchesSearch(
		InventoryOverviewItem item,
		string? searchText)
	{
		if (string.IsNullOrWhiteSpace(
			searchText))
		{
			return true;
		}

		var search =
			searchText.Trim();

		return
			Contains(
				item.PartNumber,
				search) ||
			Contains(
				item.Description,
				search) ||
			Contains(
				item.Manufacturer,
				search) ||
			Contains(
				item.Category,
				search) ||
			Contains(
				item.PurposeName,
				search) ||
			Contains(
				item.LocationName,
				search);
	}

	private static bool Contains(
		string? value,
		string searchText)
	{
		return
			!string.IsNullOrWhiteSpace(
				value) &&
			value.Contains(
				searchText,
				StringComparison.OrdinalIgnoreCase);
	}
}
