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
	private readonly PurposeService _purposeService;
	private readonly WarehouseService _warehouseService;
	private readonly StorageLocationService _storageLocationService;
	private readonly InventoryManagementService _inventoryManagementService;
	private readonly MovementService _movementService;

	public ImportService(
		ItemRepository itemRepository,
		ItemService itemService,
		PurposeService purposeService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		InventoryManagementService inventoryManagementService,
		MovementService movementService)
	{
		_itemRepository = itemRepository;
		_itemService = itemService;
		_purposeService = purposeService;
		_warehouseService = warehouseService;
		_storageLocationService = storageLocationService;
		_inventoryManagementService = inventoryManagementService;
		_movementService = movementService;
	}

	public ImportPreview CreatePreview(
		string filePath,
		CancellationToken cancellationToken = default)
	{
		var itemsByKey =
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
			cancellationToken.ThrowIfCancellationRequested();
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

				var purpose =
					GetString(
						worksheet,
						row,
						columns,
						"Purpose");

				if (string.IsNullOrWhiteSpace(purpose))
				{
					warnings.Add(
						new ImportWarning
						{
							RowNumber = row,
							Message = $"Purpose is missing for '{partNumber}'."
						});

					continue;
				}

				var location =
					GetString(
						worksheet,
						row,
						columns,
						"Location");

				if (string.IsNullOrWhiteSpace(location))
				{
					warnings.Add(
						new ImportWarning
						{
							RowNumber = row,
							Message = $"Location is missing for '{partNumber}'."
						});

					continue;
				}

				var warehouse =
					GetOptionalString(
						worksheet,
						row,
						columns,
						"Warehouse",
						"Main Warehouse");

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

				var key =
					$"{partNumber}|{purpose}|{warehouse}|{location}";

				if (!itemsByKey.TryGetValue(
					key,
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
								: category,
							Purpose = purpose,
							Warehouse = warehouse,
							Location = location
						};

					itemsByKey.Add(
						key,
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
			itemsByKey
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
							Purpose = x.Purpose,
							Warehouse = x.Warehouse,
							Location = x.Location,
							Quantity = x.Quantity,
							UnitPrice = unitPrice,
							TotalValue = x.TotalValue,
							ItemAlreadyExists = existingItem is not null
						};
					})
				.OrderBy(
					x => x.PartNumber)
				.ThenBy(
					x => x.Purpose)
				.ThenBy(
					x => x.Warehouse)
				.ThenBy(
					x => x.Location)
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
			var item =
				_itemRepository.GetByPartNumber(
					previewItem.PartNumber);

			if (item is null)
			{
				item =
					_itemService.CreateItem(
						previewItem.PartNumber,
						previewItem.Description,
						previewItem.Manufacturer,
						previewItem.Category);

				importedItems++;
			}

			var purpose =
				_purposeService.GetOrCreatePurpose(
					previewItem.Purpose);

			var warehouse = _warehouseService.GetOrCreateAsync(previewItem.Warehouse).GetAwaiter().GetResult();
			var location = _storageLocationService.GetOrCreateAsync(warehouse.Id, previewItem.Location).GetAwaiter().GetResult();

			var inventory =
				_inventoryManagementService.GetOrCreateInventory(
					item.Id,
					purpose.Id,
					location.Id);

			if (previewItem.Quantity <= 0)
			{
				skippedItems++;
				continue;
			}

			_movementService.AddOpeningBalance(
				inventory.Id,
				previewItem.Quantity,
				previewItem.UnitPrice,
				"Imported from Excel");

			importedMovements++;
		}

		return new ImportResult
		{
			ImportedItems = importedItems,
			ImportedMovements = importedMovements,
			SkippedItems = skippedItems
		};
	}

	public async Task<ImportResult> ExecuteImportAsync(
		ImportPreview preview,
		CancellationToken cancellationToken = default)
	{
		var importedItems = 0;
		var importedMovements = 0;
		var skippedItems = 0;

		foreach (var previewItem in preview.Items)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var item = await _itemRepository.GetByPartNumberAsync(
				previewItem.PartNumber,
				cancellationToken);

			if (item is null)
			{
				item = await _itemService.CreateItemAsync(
					previewItem.PartNumber,
					previewItem.Description,
					previewItem.Manufacturer,
					previewItem.Category,
					cancellationToken);
				importedItems++;
			}

			var purpose = await _purposeService.GetOrCreatePurposeAsync(
				previewItem.Purpose,
				cancellationToken);
			var warehouse = await _warehouseService.GetOrCreateAsync(previewItem.Warehouse, cancellationToken);
			var location = await _storageLocationService.GetOrCreateAsync(
				warehouse.Id,
				previewItem.Location,
				cancellationToken);
			var inventory = await _inventoryManagementService.GetOrCreateInventoryAsync(
				item.Id,
				purpose.Id,
				location.Id,
				cancellationToken);

			if (previewItem.Quantity <= 0)
			{
				skippedItems++;
				continue;
			}

			await _movementService.AddOpeningBalanceAsync(
				inventory.Id,
				previewItem.Quantity,
				previewItem.UnitPrice,
				"Imported from Excel",
				cancellationToken);
			importedMovements++;
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
			return Convert.ToInt32(
				decimalValue);
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

	private static string GetOptionalString(
		IXLWorksheet worksheet,
		int row,
		IReadOnlyDictionary<string, int> columns,
		string header,
		string defaultValue)
	{
		if (!columns.TryGetValue(header, out var column))
		{
			return defaultValue;
		}

		var value = worksheet.Cell(row, column).GetString().Trim();
		return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
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

		public string Purpose { get; init; } = string.Empty;

		public string Warehouse { get; init; } = string.Empty;

		public string Location { get; init; } = string.Empty;

		public int Quantity { get; set; }

		public decimal TotalValue { get; set; }
	}
}
