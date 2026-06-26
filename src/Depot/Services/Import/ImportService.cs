// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using ClosedXML.Excel;

using Depot.Models;
using Depot.Repositories;
using Depot.Services;

namespace Depot.Services.Import;

public sealed class ImportService
{
	private readonly ItemRepository _itemRepository;
	private readonly ItemService _itemService;
	private readonly StockService _stockService;

	public ImportService(
		ItemRepository itemRepository,
		ItemService itemService,
		StockService stockService)
	{
		_itemRepository = itemRepository;
		_itemService = itemService;
		_stockService = stockService;
	}

	public ImportPreview CreatePreview(
		string filePath)
	{
		var itemsByPartNumber =
			new Dictionary<string, ImportPreviewAccumulator>(
				StringComparer.OrdinalIgnoreCase);

		var warnings =
			new List<ImportWarning>();

		using var workbook =
			new XLWorkbook(filePath);

		var worksheet =
			workbook.Worksheet(1);

		var columns =
			ReadColumns(worksheet);

		var lastRow =
			worksheet.LastRowUsed()?.RowNumber() ?? 1;

		for (var row = 2; row <= lastRow; row++)
		{
			try
			{
				var partNumber =
					GetString(
						worksheet,
						row,
						columns,
						"P/N");

				if (string.IsNullOrWhiteSpace(partNumber))
				{
					warnings.Add(
						new ImportWarning
						{
							RowNumber = row,
							Message = "Part number is missing."
						});

					continue;
				}

				var description =
					GetString(
						worksheet,
						row,
						columns,
						"Item Description");

				if (string.IsNullOrWhiteSpace(description))
				{
					warnings.Add(
						new ImportWarning
						{
							RowNumber = row,
							Message = $"Description is missing for '{partNumber}'."
						});

					continue;
				}

				var manufacturer =
					GetString(
						worksheet,
						row,
						columns,
						"Manufacturer");

				var category =
					GetString(
						worksheet,
						row,
						columns,
						"Item Category");

				var quantity =
					GetInt(
						worksheet,
						row,
						columns,
						"Current Inventory");

				var unitPrice =
					GetDecimal(
						worksheet,
						row,
						columns,
						"Unit Price");

				if (!itemsByPartNumber.TryGetValue(
					partNumber,
					out var accumulator))
				{
					accumulator =
						new ImportPreviewAccumulator
						{
							PartNumber = partNumber,
							Description = description,
							Manufacturer = string.IsNullOrWhiteSpace(manufacturer)
								? null
								: manufacturer,
							Category = string.IsNullOrWhiteSpace(category)
								? null
								: category
						};

					itemsByPartNumber.Add(
						partNumber,
						accumulator);
				}

				accumulator.Quantity += quantity;
				accumulator.TotalValue += quantity * unitPrice;
			}
			catch (Exception ex)
			{
				warnings.Add(
					new ImportWarning
					{
						RowNumber = row,
						Message = ex.Message
					});
			}
		}

		var items =
			itemsByPartNumber
				.Values
				.Select(
					x =>
					{
						var existingItem =
							_itemRepository.GetByPartNumber(
								x.PartNumber);

						var unitPrice =
							x.Quantity == 0
								? 0m
								: x.TotalValue / x.Quantity;

						return new ImportPreviewItem
						{
							PartNumber = x.PartNumber,
							Description = x.Description,
							Manufacturer = x.Manufacturer,
							Category = x.Category,
							Quantity = x.Quantity,
							UnitPrice = unitPrice,
							TotalValue = x.TotalValue,
							ItemAlreadyExists = existingItem is not null
						};
					})
				.OrderBy(
					x => x.PartNumber)
				.ToList();

		return new ImportPreview
		{
			Items = items,
			Warnings = warnings
		};
	}

	public ImportResult ExecuteImport(
		ImportPreview preview)
	{
		var importedItems = 0;
		var importedMovements = 0;
		var skippedItems = 0;

		foreach (var previewItem in preview.Items)
		{
			var existingItem =
				_itemRepository.GetByPartNumber(
					previewItem.PartNumber);

			if (existingItem is not null)
			{
				skippedItems++;
				continue;
			}

			var item =
				_itemService.CreateItem(
					previewItem.PartNumber,
					previewItem.Description,
					previewItem.Manufacturer,
					previewItem.Category);

			importedItems++;

			if (previewItem.Quantity > 0)
			{
				_stockService.AddOpeningBalance(
					item.Id,
					previewItem.Quantity,
					previewItem.UnitPrice,
					"Imported from Excel");

				importedMovements++;
			}
		}

		return new ImportResult
		{
			ImportedItems = importedItems,
			ImportedMovements = importedMovements,
			SkippedItems = skippedItems
		};
	}

	private static Dictionary<string, int> ReadColumns(
		IXLWorksheet worksheet)
	{
		var result =
			new Dictionary<string, int>(
				StringComparer.OrdinalIgnoreCase);

		var lastColumn =
			worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

		for (var column = 1; column <= lastColumn; column++)
		{
			var header =
				worksheet
					.Cell(1, column)
					.GetString()
					.Trim();

			if (!string.IsNullOrWhiteSpace(header))
			{
				result[header] = column;
			}
		}

		return result;
	}

	private static string GetString(
		IXLWorksheet worksheet,
		int row,
		IReadOnlyDictionary<string, int> columns,
		string header)
	{
		var cell =
			GetCell(
				worksheet,
				row,
				columns,
				header);

		return cell
			.GetString()
			.Trim();
	}

	private static int GetInt(
		IXLWorksheet worksheet,
		int row,
		IReadOnlyDictionary<string, int> columns,
		string header)
	{
		var cell =
			GetCell(
				worksheet,
				row,
				columns,
				header);

		if (cell.IsEmpty())
		{
			return 0;
		}

		if (cell.TryGetValue<decimal>(
			out var decimalValue))
		{
			return Convert.ToInt32(decimalValue);
		}

		var text =
			cell
				.GetString()
				.Trim();

		if (string.IsNullOrWhiteSpace(text))
		{
			return 0;
		}

		return Convert.ToInt32(
			decimal.Parse(
				text,
				System.Globalization.CultureInfo.InvariantCulture));
	}

	private static decimal GetDecimal(
		IXLWorksheet worksheet,
		int row,
		IReadOnlyDictionary<string, int> columns,
		string header)
	{
		var cell =
			GetCell(
				worksheet,
				row,
				columns,
				header);

		if (cell.IsEmpty())
		{
			return 0m;
		}

		if (cell.TryGetValue<decimal>(
			out var decimalValue))
		{
			return decimalValue;
		}

		var text =
			cell
				.GetString()
				.Trim();

		if (string.IsNullOrWhiteSpace(text))
		{
			return 0m;
		}

		return decimal.Parse(
			text,
			System.Globalization.CultureInfo.InvariantCulture);
	}

	private static IXLCell GetCell(
		IXLWorksheet worksheet,
		int row,
		IReadOnlyDictionary<string, int> columns,
		string header)
	{
		if (!columns.TryGetValue(
			header,
			out var column))
		{
			throw new InvalidOperationException(
				$"Column '{header}' was not found.");
		}

		return worksheet.Cell(
			row,
			column);
	}

	private sealed class ImportPreviewAccumulator
	{
		public string PartNumber { get; init; } = string.Empty;

		public string Description { get; init; } = string.Empty;

		public string? Manufacturer { get; init; }

		public string? Category { get; init; }

		public int Quantity { get; set; }

		public decimal TotalValue { get; set; }
	}
}