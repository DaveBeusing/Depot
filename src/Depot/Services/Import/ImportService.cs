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

		var row = 2;

		while (!worksheet.Row(row).IsEmpty())
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
					row++;
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

			row++;
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

		var column = 1;

		while (!worksheet.Cell(1, column).IsEmpty())
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

			column++;
		}

		return result;
	}

	private static string GetString(
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

		return worksheet.Cell(row, column).GetString().Trim();
	}

	private static int GetInt(
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

		var value =
			worksheet
				.Cell(row, column)
				.GetString()
				.Trim();

		if (string.IsNullOrWhiteSpace(value))
		{
			return 0;
		}

		return Convert.ToInt32(
			decimal.Parse(value));
	}

	private static decimal GetDecimal(
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

		var value =
			worksheet
				.Cell(row, column)
				.GetString().Trim();

		if (string.IsNullOrWhiteSpace(value))
		{
			return 0m;
		}

		return decimal.Parse(
			value,
			System.Globalization.CultureInfo.InvariantCulture);
	}
}