// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using ClosedXML.Excel;

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

	public LocationInventoryReport GetLocationInventoryReport(
		string? searchText)
	{
		var report =
			GetGroupedInventoryReport(
				searchText,
				x => x.LocationName);

		return new LocationInventoryReport
		{
			Items =
				report.Items
				.Select(
					x =>
						new LocationInventoryReportItem
						{
							LocationName =
								x.GroupName,

							InventoryRows =
								x.InventoryRows,

							TotalItems =
								x.TotalItems,

							TotalStockQuantity =
								x.TotalStockQuantity,

							InventoryValue =
								x.InventoryValue
						})
				.ToList(),

			TotalInventoryRows =
				report.TotalInventoryRows,

			TotalItems =
				report.TotalItems,

			TotalStockQuantity =
				report.TotalStockQuantity,

			TotalInventoryValue =
				report.TotalInventoryValue
		};
	}

	public PurposeInventoryReport GetPurposeInventoryReport(
		string? searchText)
	{
		var report =
			GetGroupedInventoryReport(
				searchText,
				x => x.PurposeName);

		return new PurposeInventoryReport
		{
			Items =
				report.Items
				.Select(
					x =>
						new PurposeInventoryReportItem
						{
							PurposeName =
								x.GroupName,

							InventoryRows =
								x.InventoryRows,

							TotalItems =
								x.TotalItems,

							TotalStockQuantity =
								x.TotalStockQuantity,

							InventoryValue =
								x.InventoryValue
						})
				.ToList(),

			TotalInventoryRows =
				report.TotalInventoryRows,

			TotalItems =
				report.TotalItems,

			TotalStockQuantity =
				report.TotalStockQuantity,

			TotalInventoryValue =
				report.TotalInventoryValue
		};
	}

	public void ExportInventoryValueReport(
		string? searchText,
		string filePath)
	{
		var report =
			GetInventoryValueReport(
				searchText);

		using var workbook =
			new XLWorkbook();

		var worksheet =
			workbook.Worksheets.Add(
				"Inventory Value");

		worksheet.Cell(
			1,
			1)
			.Value =
				"Inventory Value";

		worksheet.Range(
			1,
			1,
			1,
			9)
			.Merge();

		worksheet.Cell(
			1,
			1)
			.Style.Font.Bold =
				true;

		worksheet.Cell(
			1,
			1)
			.Style.Font.FontSize =
				16;

		WriteSummary(
			worksheet,
			report);

		var headerRow =
			6;

		WriteHeaders(
			worksheet,
			headerRow);

		var row =
			headerRow + 1;

		foreach (var item in report.Items)
		{
			worksheet.Cell(
				row,
				1)
				.Value =
					item.PartNumber;

			worksheet.Cell(
				row,
				2)
				.Value =
					item.Description;

			worksheet.Cell(
				row,
				3)
				.Value =
					item.Manufacturer ?? string.Empty;

			worksheet.Cell(
				row,
				4)
				.Value =
					item.Category ?? string.Empty;

			worksheet.Cell(
				row,
				5)
				.Value =
					item.PurposeName;

			worksheet.Cell(
				row,
				6)
				.Value =
					item.LocationName;

			worksheet.Cell(
				row,
				7)
				.Value =
					item.CurrentStock;

			worksheet.Cell(
				row,
				8)
				.Value =
					item.AverageCost;

			worksheet.Cell(
				row,
				9)
				.Value =
					item.InventoryValue;

			row++;
		}

		FormatWorksheet(
			worksheet,
			headerRow,
			row - 1);

		workbook.SaveAs(
			filePath);
	}

	public void ExportLocationInventoryReport(
		string? searchText,
		string filePath)
	{
		var report =
			GetLocationInventoryReport(
				searchText);

		ExportGroupedInventoryReport(
			"Stock by Location",
			"Location",
			report.Items
				.Select(
					x =>
						new GroupedInventoryExportItem
						{
							GroupName =
								x.LocationName,

							InventoryRows =
								x.InventoryRows,

							TotalItems =
								x.TotalItems,

							TotalStockQuantity =
								x.TotalStockQuantity,

							InventoryValue =
								x.InventoryValue
						})
				.ToList(),
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue,
			filePath);
	}

	public void ExportPurposeInventoryReport(
		string? searchText,
		string filePath)
	{
		var report =
			GetPurposeInventoryReport(
				searchText);

		ExportGroupedInventoryReport(
			"Stock by Purpose",
			"Purpose",
			report.Items
				.Select(
					x =>
						new GroupedInventoryExportItem
						{
							GroupName =
								x.PurposeName,

							InventoryRows =
								x.InventoryRows,

							TotalItems =
								x.TotalItems,

							TotalStockQuantity =
								x.TotalStockQuantity,

							InventoryValue =
								x.InventoryValue
						})
				.ToList(),
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue,
			filePath);
	}

	private GroupedInventoryReport GetGroupedInventoryReport(
		string? searchText,
		Func<InventoryOverviewItem, string> groupSelector)
	{
		var inventoryRows =
			_stockService
				.GetInventoryOverview()
				.Where(
					x =>
						MatchesSearch(
							x,
							searchText))
				.ToList();

		var rows =
			inventoryRows
				.GroupBy(
					groupSelector)
				.Select(
					x =>
						new GroupedInventoryReportItem
						{
							GroupName =
								x.Key,

							InventoryRows =
								x.Count(),

							TotalItems =
								x
									.Select(
										y => y.ItemId)
									.Distinct()
									.Count(),

							TotalStockQuantity =
								x.Sum(
									y => y.CurrentStock),

							InventoryValue =
								x.Sum(
									y => y.InventoryValue)
						})
				.OrderBy(
					x => x.GroupName)
				.ToList();

		return new GroupedInventoryReport
		{
			Items =
				rows,

			TotalInventoryRows =
				inventoryRows.Count,

			TotalItems =
				inventoryRows
					.Select(
						x => x.ItemId)
					.Distinct()
					.Count(),

			TotalStockQuantity =
				inventoryRows.Sum(
					x => x.CurrentStock),

			TotalInventoryValue =
				inventoryRows.Sum(
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

	private static void WriteSummary(
		IXLWorksheet worksheet,
		InventoryValueReport report)
	{
		worksheet.Cell(
			3,
			1)
			.Value =
				"Inventory Rows";

		worksheet.Cell(
			3,
			2)
			.Value =
				report.TotalInventoryRows;

		worksheet.Cell(
			3,
			4)
			.Value =
				"Items";

		worksheet.Cell(
			3,
			5)
			.Value =
				report.TotalItems;

		worksheet.Cell(
			4,
			1)
			.Value =
				"Total Stock";

		worksheet.Cell(
			4,
			2)
			.Value =
				report.TotalStockQuantity;

		worksheet.Cell(
			4,
			4)
			.Value =
				"Inventory Value";

		worksheet.Cell(
			4,
			5)
			.Value =
				report.TotalInventoryValue;
	}

	private static void WriteHeaders(
		IXLWorksheet worksheet,
		int row)
	{
		worksheet.Cell(
			row,
			1)
			.Value =
				"Part Number";

		worksheet.Cell(
			row,
			2)
			.Value =
				"Description";

		worksheet.Cell(
			row,
			3)
			.Value =
				"Manufacturer";

		worksheet.Cell(
			row,
			4)
			.Value =
				"Category";

		worksheet.Cell(
			row,
			5)
			.Value =
				"Purpose";

		worksheet.Cell(
			row,
			6)
			.Value =
				"Location";

		worksheet.Cell(
			row,
			7)
			.Value =
				"Stock";

		worksheet.Cell(
			row,
			8)
			.Value =
				"Average Cost";

		worksheet.Cell(
			row,
			9)
			.Value =
				"Value";
	}

	private static void FormatWorksheet(
		IXLWorksheet worksheet,
		int headerRow,
		int lastDataRow)
	{
		var summaryLabelRange =
			worksheet.Range(
				3,
				1,
				4,
				4);

		summaryLabelRange.Style.Font.Bold =
			true;

		worksheet.Cell(
			4,
			5)
			.Style.NumberFormat.Format =
				"#,##0.00";

		var headerRange =
			worksheet.Range(
				headerRow,
				1,
				headerRow,
				9);

		headerRange.Style.Font.Bold =
			true;

		headerRange.Style.Fill.BackgroundColor =
			XLColor.FromHtml(
				"#E5E7EB");

		var usedLastRow =
			Math.Max(
				headerRow,
				lastDataRow);

		worksheet.Range(
			headerRow,
			1,
			usedLastRow,
			9)
			.SetAutoFilter();

		if (lastDataRow > headerRow)
		{
			worksheet.Range(
				headerRow + 1,
				8,
				lastDataRow,
				8)
				.Style.NumberFormat.Format =
					"#,##0.0000";

			worksheet.Range(
				headerRow + 1,
				9,
				lastDataRow,
				9)
				.Style.NumberFormat.Format =
					"#,##0.00";
		}

		worksheet.SheetView.FreezeRows(
			headerRow);

		worksheet.Columns()
			.AdjustToContents();
	}

	private static void ExportGroupedInventoryReport(
		string title,
		string groupHeader,
		IReadOnlyList<GroupedInventoryExportItem> items,
		int totalInventoryRows,
		int totalItems,
		int totalStockQuantity,
		decimal totalInventoryValue,
		string filePath)
	{
		using var workbook =
			new XLWorkbook();

		var worksheet =
			workbook.Worksheets.Add(
				title);

		worksheet.Cell(
			1,
			1)
			.Value =
				title;

		worksheet.Range(
			1,
			1,
			1,
			5)
			.Merge();

		worksheet.Cell(
			1,
			1)
			.Style.Font.Bold =
				true;

		worksheet.Cell(
			1,
			1)
			.Style.Font.FontSize =
				16;

		WriteGroupedInventorySummary(
			worksheet,
			totalInventoryRows,
			totalItems,
			totalStockQuantity,
			totalInventoryValue);

		var headerRow =
			6;

		WriteGroupedInventoryHeaders(
			worksheet,
			headerRow,
			groupHeader);

		var row =
			headerRow + 1;

		foreach (var item in items)
		{
			worksheet.Cell(
				row,
				1)
				.Value =
					item.GroupName;

			worksheet.Cell(
				row,
				2)
				.Value =
					item.InventoryRows;

			worksheet.Cell(
				row,
				3)
				.Value =
					item.TotalItems;

			worksheet.Cell(
				row,
				4)
				.Value =
					item.TotalStockQuantity;

			worksheet.Cell(
				row,
				5)
				.Value =
					item.InventoryValue;

			row++;
		}

		FormatGroupedInventoryWorksheet(
			worksheet,
			headerRow,
			row - 1);

		workbook.SaveAs(
			filePath);
	}

	private static void WriteGroupedInventorySummary(
		IXLWorksheet worksheet,
		int totalInventoryRows,
		int totalItems,
		int totalStockQuantity,
		decimal totalInventoryValue)
	{
		worksheet.Cell(
			3,
			1)
			.Value =
				"Inventory Rows";

		worksheet.Cell(
			3,
			2)
			.Value =
				totalInventoryRows;

		worksheet.Cell(
			3,
			4)
			.Value =
				"Items";

		worksheet.Cell(
			3,
			5)
			.Value =
				totalItems;

		worksheet.Cell(
			4,
			1)
			.Value =
				"Total Stock";

		worksheet.Cell(
			4,
			2)
			.Value =
				totalStockQuantity;

		worksheet.Cell(
			4,
			4)
			.Value =
				"Inventory Value";

		worksheet.Cell(
			4,
			5)
			.Value =
				totalInventoryValue;
	}

	private static void WriteGroupedInventoryHeaders(
		IXLWorksheet worksheet,
		int row,
		string groupHeader)
	{
		worksheet.Cell(
			row,
			1)
			.Value =
				groupHeader;

		worksheet.Cell(
			row,
			2)
			.Value =
				"Inventory Rows";

		worksheet.Cell(
			row,
			3)
			.Value =
				"Items";

		worksheet.Cell(
			row,
			4)
			.Value =
				"Total Stock";

		worksheet.Cell(
			row,
			5)
			.Value =
				"Inventory Value";
	}

	private static void FormatGroupedInventoryWorksheet(
		IXLWorksheet worksheet,
		int headerRow,
		int lastDataRow)
	{
		var summaryLabelRange =
			worksheet.Range(
				3,
				1,
				4,
				4);

		summaryLabelRange.Style.Font.Bold =
			true;

		worksheet.Cell(
			4,
			5)
			.Style.NumberFormat.Format =
				"#,##0.00";

		var headerRange =
			worksheet.Range(
				headerRow,
				1,
				headerRow,
				5);

		headerRange.Style.Font.Bold =
			true;

		headerRange.Style.Fill.BackgroundColor =
			XLColor.FromHtml(
				"#E5E7EB");

		var usedLastRow =
			Math.Max(
				headerRow,
				lastDataRow);

		worksheet.Range(
			headerRow,
			1,
			usedLastRow,
			5)
			.SetAutoFilter();

		if (lastDataRow > headerRow)
		{
			worksheet.Range(
				headerRow + 1,
				5,
				lastDataRow,
				5)
				.Style.NumberFormat.Format =
					"#,##0.00";
		}

		worksheet.SheetView.FreezeRows(
			headerRow);

		worksheet.Columns()
			.AdjustToContents();
	}

	private sealed class GroupedInventoryExportItem
	{
		public string GroupName { get; init; } = string.Empty;

		public int InventoryRows { get; init; }

		public int TotalItems { get; init; }

		public int TotalStockQuantity { get; init; }

		public decimal InventoryValue { get; init; }
	}

	private sealed class GroupedInventoryReport
	{
		public IReadOnlyList<GroupedInventoryReportItem> Items { get; init; }
			= Array.Empty<GroupedInventoryReportItem>();

		public int TotalInventoryRows { get; init; }

		public int TotalItems { get; init; }

		public int TotalStockQuantity { get; init; }

		public decimal TotalInventoryValue { get; init; }
	}

	private sealed class GroupedInventoryReportItem
	{
		public string GroupName { get; init; } = string.Empty;

		public int InventoryRows { get; init; }

		public int TotalItems { get; init; }

		public int TotalStockQuantity { get; init; }

		public decimal InventoryValue { get; init; }
	}
}
