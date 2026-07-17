// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using ClosedXML.Excel;

using Depot.Models;

namespace Depot.Services;

public sealed class ReportService
{
	private const string EuroCurrencyFormat = "#,##0.00 [$€-407]";

	private readonly StockService _stockService;

	public ReportService(
		StockService stockService)
	{
		_stockService =
			stockService;
	}

	public async Task<InventoryValueReport> GetInventoryValueReportAsync(
		string? searchText,
		CancellationToken cancellationToken)
	{
		var rows = new List<InventoryValueReportItem>();
		var itemIds = new HashSet<long>();
		var totalStock = 0;
		var totalValue = 0m;
		await foreach (var item in _stockService.StreamInventoryOverviewAsync(searchText, cancellationToken))
		{
			rows.Add(new InventoryValueReportItem
			{
				InventoryId = item.InventoryId,
				ItemId = item.ItemId,
				PartNumber = item.PartNumber,
				Description = item.Description,
				Manufacturer = item.Manufacturer,
				Category = item.Category,
				PurposeName = item.PurposeName,
				WarehouseName = item.WarehouseName,
				LocationName = item.LocationName,
				CurrentStock = item.CurrentStock,
				AverageCost = item.AverageCost,
				InventoryValue = item.InventoryValue
			});
			itemIds.Add(item.ItemId);
			totalStock += item.CurrentStock;
			totalValue += item.InventoryValue;
		}
		return new InventoryValueReport
		{
			Items = rows,
			TotalInventoryRows = rows.Count,
			TotalItems = itemIds.Count,
			TotalStockQuantity = totalStock,
			TotalInventoryValue = totalValue
		};
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

							WarehouseName =
								x.WarehouseName,

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

	public GroupedInventoryReport GetGroupedInventoryReport(
		string? searchText,
		GroupedInventoryReportType reportType)
	{
		var definition =
			GetGroupedInventoryReportDefinition(
				reportType);

		return BuildGroupedInventoryReport(
			searchText,
			definition.GroupSelector);
	}

	public async Task<GroupedInventoryReport> GetGroupedInventoryReportAsync(
		string? searchText,
		GroupedInventoryReportType reportType,
		CancellationToken cancellationToken)
	{
		var definition = GetGroupedInventoryReportDefinition(reportType);
		var groups = new Dictionary<string, GroupAccumulator>(StringComparer.OrdinalIgnoreCase);
		var allItems = new HashSet<long>();
		var totalRows = 0;
		var totalStock = 0;
		var totalValue = 0m;
		await foreach (var item in _stockService.StreamInventoryOverviewAsync(searchText, cancellationToken))
		{
			var groupName = definition.GroupSelector(item);
			if (!groups.TryGetValue(groupName, out var group))
			{
				group = new GroupAccumulator();
				groups.Add(groupName, group);
			}
			group.InventoryRows++;
			group.ItemIds.Add(item.ItemId);
			group.TotalStockQuantity += item.CurrentStock;
			group.InventoryValue += item.InventoryValue;
			allItems.Add(item.ItemId);
			totalRows++;
			totalStock += item.CurrentStock;
			totalValue += item.InventoryValue;
		}

		return new GroupedInventoryReport
		{
			Items = groups
				.Select(pair => new GroupedInventoryReportItem
				{
					GroupName = pair.Key,
					InventoryRows = pair.Value.InventoryRows,
					TotalItems = pair.Value.ItemIds.Count,
					TotalStockQuantity = pair.Value.TotalStockQuantity,
					InventoryValue = pair.Value.InventoryValue
				})
				.OrderBy(item => item.GroupName)
				.ToList(),
			TotalInventoryRows = totalRows,
			TotalItems = allItems.Count,
			TotalStockQuantity = totalStock,
			TotalInventoryValue = totalValue
		};
	}

	public void ExportInventoryValueReport(
		string? searchText,
		string filePath)
	{
		var report =
			GetInventoryValueReport(
				searchText);

		ExportInventoryValueWorkbook(report, filePath);
	}

	public async Task ExportInventoryValueReportAsync(
		string? searchText,
		string filePath,
		CancellationToken cancellationToken = default)
	{
		var report = await GetInventoryValueReportAsync(searchText, cancellationToken);
		await Task.Run(
			() => ExportInventoryValueWorkbook(report, filePath),
			cancellationToken);
	}

	private static void ExportInventoryValueWorkbook(
		InventoryValueReport report,
		string filePath)
	{

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
			10)
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
					item.WarehouseName;

			worksheet.Cell(
				row,
				7)
				.Value =
					item.LocationName;

			worksheet.Cell(
				row,
				8)
				.Value =
					item.CurrentStock;

			worksheet.Cell(
				row,
				9)
				.Value =
					item.AverageCost;

			worksheet.Cell(
				row,
				10)
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

	public void ExportGroupedInventoryReport(
		string? searchText,
		GroupedInventoryReportType reportType,
		string filePath)
	{
		var definition =
			GetGroupedInventoryReportDefinition(
				reportType);

		var report =
			BuildGroupedInventoryReport(
				searchText,
				definition.GroupSelector);

		ExportGroupedInventoryWorkbook(
			definition.Title,
			definition.GroupHeader,
			report.Items,
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue,
			filePath);
	}

	public async Task ExportGroupedInventoryReportAsync(
		string? searchText,
		GroupedInventoryReportType reportType,
		string filePath,
		CancellationToken cancellationToken = default)
	{
		var definition = GetGroupedInventoryReportDefinition(reportType);
		var report = await GetGroupedInventoryReportAsync(
			searchText,
			reportType,
			cancellationToken);

		await Task.Run(
			() => ExportGroupedInventoryWorkbook(
				definition.Title,
				definition.GroupHeader,
				report.Items,
				report.TotalInventoryRows,
				report.TotalItems,
				report.TotalStockQuantity,
				report.TotalInventoryValue,
				filePath),
			cancellationToken);
	}

	private GroupedInventoryReport BuildGroupedInventoryReport(
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

	private static GroupedInventoryReportDefinition GetGroupedInventoryReportDefinition(
		GroupedInventoryReportType reportType)
	{
		return reportType switch
		{
			GroupedInventoryReportType.Location =>
				new GroupedInventoryReportDefinition(
					"Stock by Storage Location",
					"Storage Location",
					x => x.LocationName),

			GroupedInventoryReportType.Warehouse =>
				new GroupedInventoryReportDefinition(
					"Stock by Warehouse",
					"Warehouse",
					x => x.WarehouseName),

			GroupedInventoryReportType.Purpose =>
				new GroupedInventoryReportDefinition(
					"Stock by Purpose",
					"Purpose",
					x => x.PurposeName),

			GroupedInventoryReportType.Category =>
				new GroupedInventoryReportDefinition(
					"Stock by Category",
					"Category",
					x => string.IsNullOrWhiteSpace(
						x.Category)
						? "Uncategorized"
						: x.Category),

			GroupedInventoryReportType.Manufacturer =>
				new GroupedInventoryReportDefinition(
					"Stock by Manufacturer",
					"Manufacturer",
					x => string.IsNullOrWhiteSpace(
						x.Manufacturer)
						? "Unspecified Manufacturer"
						: x.Manufacturer),

			_ =>
				throw new ArgumentOutOfRangeException(
					nameof(reportType),
					reportType,
					"Unknown grouped inventory report type.")
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
				"Warehouse";

		worksheet.Cell(
			row,
			7)
			.Value =
				"Storage Location";

		worksheet.Cell(
			row,
			8)
			.Value =
				"Stock";

		worksheet.Cell(
			row,
			9)
			.Value =
				"Average Cost";

		worksheet.Cell(
			row,
			10)
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
				EuroCurrencyFormat;

		var headerRange =
			worksheet.Range(
				headerRow,
				1,
				headerRow,
				10);

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
			10)
			.SetAutoFilter();

		if (lastDataRow > headerRow)
		{
			worksheet.Range(
				headerRow + 1,
				9,
				lastDataRow,
				9)
				.Style.NumberFormat.Format =
					EuroCurrencyFormat;

			worksheet.Range(
				headerRow + 1,
				10,
				lastDataRow,
				10)
				.Style.NumberFormat.Format =
					EuroCurrencyFormat;
		}

		worksheet.SheetView.FreezeRows(
			headerRow);

		worksheet.Columns()
			.AdjustToContents();
	}

	private static void ExportGroupedInventoryWorkbook(
		string title,
		string groupHeader,
		IReadOnlyList<GroupedInventoryReportItem> items,
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
				EuroCurrencyFormat;

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
					EuroCurrencyFormat;
		}

		worksheet.SheetView.FreezeRows(
			headerRow);

		worksheet.Columns()
			.AdjustToContents();
	}

	private sealed class GroupedInventoryReportDefinition
	{
		public GroupedInventoryReportDefinition(
			string title,
			string groupHeader,
			Func<InventoryOverviewItem, string> groupSelector)
		{
			Title =
				title;

			GroupHeader =
				groupHeader;

			GroupSelector =
				groupSelector;
		}

		public string Title { get; }

		public string GroupHeader { get; }

		public Func<InventoryOverviewItem, string> GroupSelector { get; }
	}

	private sealed class GroupAccumulator
	{
		public int InventoryRows { get; set; }
		public HashSet<long> ItemIds { get; } = new();
		public int TotalStockQuantity { get; set; }
		public decimal InventoryValue { get; set; }
	}
}
