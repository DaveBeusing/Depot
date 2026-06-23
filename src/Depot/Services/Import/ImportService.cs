// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using ClosedXML.Excel;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services.Import;

public sealed class ImportService
{
	private readonly ItemRepository _itemRepository;

	public ImportService(
		ItemRepository itemRepository)
	{
		_itemRepository = itemRepository;
	}

	public ImportPreview CreatePreview(
		string filePath)
	{
		var items =
			new List<ImportPreviewItem>();

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
					continue;
				}

				var description =
					GetString(
						worksheet,
						row,
						columns,
						"Item Description");

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

				var existingItem =
					_itemRepository.GetByPartNumber(
						partNumber);

				items.Add(
					new ImportPreviewItem
					{
						PartNumber =
							partNumber,

						Description =
							description,

						Manufacturer =
							string.IsNullOrWhiteSpace(manufacturer)
								? null
								: manufacturer,

						Category =
							string.IsNullOrWhiteSpace(category)
								? null
								: category,

						Quantity =
							quantity,

						UnitPrice =
							unitPrice,

						ItemAlreadyExists =
							existingItem is not null
					});
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

		return new ImportPreview
		{
			Items = items,
			Warnings = warnings
		};
	}

	public ImportResult ExecuteImport(
		ImportPreview preview)
	{
		throw new NotImplementedException();
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
}