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
	private readonly IAuthorizationService _authorization;

	public ImportService(
		ItemRepository itemRepository,
		ItemService itemService,
		PurposeService purposeService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		InventoryManagementService inventoryManagementService,
		MovementService movementService,
		IAuthorizationService authorization)
	{
		_itemRepository = itemRepository;
		_itemService = itemService;
		_purposeService = purposeService;
		_warehouseService = warehouseService;
		_storageLocationService = storageLocationService;
		_inventoryManagementService = inventoryManagementService;
		_movementService = movementService;
		_authorization = authorization;
	}

	public ImportMapping InspectMapping(
		string filePath,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ImportManage);
		using var workbook = new XLWorkbook(filePath);
		var worksheet = workbook.Worksheet(1);
		var sourceColumns = ReadSourceColumns(worksheet, cancellationToken);
		return ImportMapping.CreateDefault(sourceColumns);
	}

	public ImportPreview CreatePreview(
		string filePath,
		CancellationToken cancellationToken = default)
	{
		var mapping = InspectMapping(filePath, cancellationToken);
		return CreatePreview(filePath, mapping, cancellationToken);
	}

	public ImportPreview CreatePreview(
		string filePath,
		ImportMapping mapping,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ImportManage);
		ArgumentNullException.ThrowIfNull(mapping);

		if (!mapping.IsValid)
		{
			throw new InvalidOperationException(
				"The import mapping contains blocking validation errors. Resolve them before generating the preview.");
		}

		var itemsByKey = new Dictionary<string, ImportPreviewAccumulator>(
			StringComparer.OrdinalIgnoreCase);
		var warnings = new List<ImportWarning>();

		using var workbook = new XLWorkbook(filePath);
		var worksheet = workbook.Worksheet(1);
		EnsureMappingMatchesWorksheet(worksheet, mapping);

		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

		for (var row = 2; row <= lastRow; row++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				var partNumber = GetString(
					worksheet,
					row,
					mapping,
					ImportTargetField.PartNumber);

				if (string.IsNullOrWhiteSpace(partNumber))
				{
					warnings.Add(new ImportWarning
					{
						RowNumber = row,
						Message = "Part number is missing."
					});
					continue;
				}

				var description = GetString(
					worksheet,
					row,
					mapping,
					ImportTargetField.Description);

				if (string.IsNullOrWhiteSpace(description))
				{
					warnings.Add(new ImportWarning
					{
						RowNumber = row,
						Message = $"Description is missing for '{partNumber}'."
					});
					continue;
				}

				var purpose = GetString(
					worksheet,
					row,
					mapping,
					ImportTargetField.Purpose);

				if (string.IsNullOrWhiteSpace(purpose))
				{
					warnings.Add(new ImportWarning
					{
						RowNumber = row,
						Message = $"Purpose is missing for '{partNumber}'."
					});
					continue;
				}

				var location = GetString(
					worksheet,
					row,
					mapping,
					ImportTargetField.Location);

				if (string.IsNullOrWhiteSpace(location))
				{
					warnings.Add(new ImportWarning
					{
						RowNumber = row,
						Message = $"Location is missing for '{partNumber}'."
					});
					continue;
				}

				var warehouseDefinition = ImportTargetCatalog.Get(ImportTargetField.Warehouse);
				var warehouse = GetOptionalString(
					worksheet,
					row,
					mapping,
					ImportTargetField.Warehouse,
					warehouseDefinition.DefaultValue ?? "Main Warehouse");

				var manufacturer = GetString(
					worksheet,
					row,
					mapping,
					ImportTargetField.Manufacturer);
				var category = GetString(
					worksheet,
					row,
					mapping,
					ImportTargetField.Category);
				var quantity = GetInt(
					worksheet,
					row,
					mapping,
					ImportTargetField.Quantity);
				var unitPrice = GetDecimal(
					worksheet,
					row,
					mapping,
					ImportTargetField.UnitPrice);

				var key = $"{partNumber}|{purpose}|{warehouse}|{location}";

				if (!itemsByKey.TryGetValue(key, out var accumulator))
				{
					accumulator = new ImportPreviewAccumulator
					{
						PartNumber = partNumber,
						Description = description,
						Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer,
						Category = string.IsNullOrWhiteSpace(category) ? null : category,
						Purpose = purpose,
						Warehouse = warehouse,
						Location = location
					};
					itemsByKey.Add(key, accumulator);
				}

				accumulator.Quantity += quantity;
				accumulator.TotalValue += quantity * unitPrice;
			}
			catch (Exception ex)
			{
				warnings.Add(new ImportWarning
				{
					RowNumber = row,
					Message = ex.Message
				});
			}
		}

		var items = itemsByKey
			.Values
			.Select(x =>
			{
				var existingItem = _itemRepository.GetByPartNumber(x.PartNumber);
				var unitPrice = x.Quantity == 0 ? 0m : x.TotalValue / x.Quantity;

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
			.OrderBy(x => x.PartNumber)
			.ThenBy(x => x.Purpose)
			.ThenBy(x => x.Warehouse)
			.ThenBy(x => x.Location)
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
		_authorization.RequirePermission(ApplicationPermission.ImportManage);
		var importedItems = 0;
		var importedMovements = 0;
		var skippedItems = 0;

		foreach (var previewItem in preview.Items)
		{
			var item = _itemRepository.GetByPartNumber(previewItem.PartNumber);

			if (item is null)
			{
				item = _itemService.CreateItem(
					previewItem.PartNumber,
					previewItem.Description,
					previewItem.Manufacturer,
					previewItem.Category);
				importedItems++;
			}

			var purpose = _purposeService.GetOrCreatePurpose(previewItem.Purpose);
			var warehouse = _warehouseService.GetOrCreateAsync(previewItem.Warehouse).GetAwaiter().GetResult();
			var location = _storageLocationService.GetOrCreateAsync(warehouse.Id, previewItem.Location).GetAwaiter().GetResult();
			var inventory = _inventoryManagementService.GetOrCreateInventory(
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
		_authorization.RequirePermission(ApplicationPermission.ImportManage);
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
			var warehouse = await _warehouseService.GetOrCreateAsync(
				previewItem.Warehouse,
				cancellationToken);
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

	private static IReadOnlyList<(int ColumnNumber, string Header)> ReadSourceColumns(
		IXLWorksheet worksheet,
		CancellationToken cancellationToken)
	{
		var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
		var result = new List<(int ColumnNumber, string Header)>(lastColumn);

		for (var column = 1; column <= lastColumn; column++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var header = worksheet.Cell(1, column).GetString().Trim();
			result.Add((column, header));
		}

		return result;
	}

	private static void EnsureMappingMatchesWorksheet(
		IXLWorksheet worksheet,
		ImportMapping mapping)
	{
		var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

		foreach (var source in mapping.Columns)
		{
			if (source.ColumnNumber < 1 || source.ColumnNumber > lastColumn)
			{
				throw new InvalidOperationException(
					"The workbook columns changed after the mapping was loaded. Reload the file before continuing.");
			}

			var currentHeader = worksheet.Cell(1, source.ColumnNumber).GetString().Trim();
			if (!string.Equals(currentHeader, source.Header, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					"The workbook columns changed after the mapping was loaded. Reload the file before continuing.");
			}
		}
	}

	private static string GetString(
		IXLWorksheet worksheet,
		int row,
		ImportMapping mapping,
		ImportTargetField target)
	{
		return GetCell(worksheet, row, mapping, target)
			.GetString()
			.Trim();
	}

	private static int GetInt(
		IXLWorksheet worksheet,
		int row,
		ImportMapping mapping,
		ImportTargetField target)
	{
		var cell = GetCell(worksheet, row, mapping, target);

		if (cell.IsEmpty())
		{
			return 0;
		}

		if (cell.TryGetValue<decimal>(out var decimalValue))
		{
			return Convert.ToInt32(decimalValue);
		}

		var text = cell.GetString().Trim();
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
		ImportMapping mapping,
		ImportTargetField target,
		string defaultValue)
	{
		var column = mapping.GetColumnNumber(target);
		if (column is null)
		{
			return defaultValue;
		}

		var value = worksheet.Cell(row, column.Value).GetString().Trim();
		return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
	}

	private static decimal GetDecimal(
		IXLWorksheet worksheet,
		int row,
		ImportMapping mapping,
		ImportTargetField target)
	{
		var cell = GetCell(worksheet, row, mapping, target);

		if (cell.IsEmpty())
		{
			return 0m;
		}

		if (cell.TryGetValue<decimal>(out var decimalValue))
		{
			return decimalValue;
		}

		var text = cell.GetString().Trim();
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
		ImportMapping mapping,
		ImportTargetField target)
	{
		var column = mapping.GetColumnNumber(target);
		if (column is null)
		{
			var definition = ImportTargetCatalog.Get(target);
			throw new InvalidOperationException(
				$"Target field '{definition.DisplayName}' is not mapped.");
		}

		return worksheet.Cell(row, column.Value);
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
