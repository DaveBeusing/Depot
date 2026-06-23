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

		using var workbook =
			new XLWorkbook(filePath);

		var worksheet =
			workbook.Worksheet(1);

		var row = 2;

		while (!worksheet.Cell(row, 5).IsEmpty())
		{
			var partNumber =
				worksheet.Cell(row, 5).GetString().Trim();

			var description =
				worksheet.Cell(row, 6).GetString().Trim();

			var manufacturer =
				worksheet.Cell(row, 4).GetString().Trim();

			var category =
				worksheet.Cell(row, 3).GetString().Trim();

			var quantity =
				worksheet.Cell(row, 15).GetValue<int>();

			var unitPrice =
				worksheet.Cell(row, 11).GetValue<decimal>();

			var existingItem =
				_itemRepository.GetByPartNumber(
					partNumber);

			items.Add(
				new ImportPreviewItem
				{
					PartNumber = partNumber,

					Description = description,

					Manufacturer =
						string.IsNullOrWhiteSpace(manufacturer)
							? null
							: manufacturer,

					Category =
						string.IsNullOrWhiteSpace(category)
							? null
							: category,

					Quantity = quantity,

					UnitPrice = unitPrice,

					ItemAlreadyExists =
						existingItem is not null
				});

			row++;
		}

		return new ImportPreview
		{
			Items = items
		};
	}

	public ImportResult ExecuteImport(
		ImportPreview preview)
	{
		throw new NotImplementedException();
	}
}