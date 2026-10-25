// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using ClosedXML.Excel;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Depot.Data;
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
	private readonly IDatabaseTransactionRunner _transactions;

	public ImportService(
		ItemRepository itemRepository,
		ItemService itemService,
		PurposeService purposeService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		InventoryManagementService inventoryManagementService,
		MovementService movementService,
		IAuthorizationService authorization,
		IDatabaseTransactionRunner transactions)
	{
		_itemRepository = itemRepository;
		_itemService = itemService;
		_purposeService = purposeService;
		_warehouseService = warehouseService;
		_storageLocationService = storageLocationService;
		_inventoryManagementService = inventoryManagementService;
		_movementService = movementService;
		_authorization = authorization;
		_transactions = transactions;
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
						BlocksImport = true,
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
						BlocksImport = true,
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
						BlocksImport = true,
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
						BlocksImport = true,
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
					BlocksImport = true,
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
			OperationId = OperationIdFor(GetRequestHash(items)),
			Items = items,
			Warnings = warnings
		};
	}

	public ImportResult ExecuteImport(ImportPreview preview) =>
		Task.Run(() => ExecuteImportAsync(preview)).GetAwaiter().GetResult();

	public async Task<ImportResult> ExecuteImportAsync(
		ImportPreview preview,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ImportManage);
		ArgumentNullException.ThrowIfNull(preview);
		if (preview.OperationId == Guid.Empty) throw new ArgumentException("An import operation ID is required.", nameof(preview));
		if (preview.Items.Count == 0) throw new InvalidOperationException("The import preview is empty.");
		if (preview.Warnings.Any(warning => warning.BlocksImport))
			throw new InvalidOperationException("Resolve the blocking preview errors before importing.");
		var items = preview.Items.ToArray();
		if (items.Any(item => item.UnitPrice < 0))
			throw new InvalidOperationException("Opening balance prices cannot be negative.");
		var requestHash = GetRequestHash(items);
		var result = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (await InventoryImportOperationRepository.IsCompletedAsync(transaction, preview.OperationId, requestHash, token))
				return new ImportResult { AlreadyCompleted = true, SkippedItems = items.Length };

			var importedItems = 0;
			var importedMovements = 0;
			var skippedItems = 0;
			long firstInventoryId = 0;
			foreach (var row in items)
			{
				token.ThrowIfCancellationRequested();
				var (item, created) = await _itemService.GetOrCreateForImportAsync(transaction,
					row.PartNumber, row.Description, row.Manufacturer, row.Category, token);
				if (created) importedItems++;
				var purpose = await _purposeService.GetOrCreateForImportAsync(transaction, row.Purpose, token);
				var warehouse = await _warehouseService.GetOrCreateForImportAsync(transaction, row.Warehouse, token);
				var location = await _storageLocationService.GetOrCreateForImportAsync(transaction, warehouse.Id, row.Location, token);
				var inventory = await _inventoryManagementService.GetOrCreateForImportAsync(transaction,
					item.Id, purpose.Id, location.Id, token);
				if (firstInventoryId == 0) firstInventoryId = inventory.Id;
				if (row.Quantity <= 0)
				{
					skippedItems++;
					continue;
				}
				await _movementService.AddOpeningBalanceInTransactionAsync(transaction, inventory.Id,
					row.Quantity, row.UnitPrice, $"IMPORT:{preview.OperationId:D}", token);
				importedMovements++;
			}
			token.ThrowIfCancellationRequested();
			await InventoryImportOperationRepository.CompleteAsync(transaction, preview.OperationId,
				requestHash, firstInventoryId, token);
			return new ImportResult
			{
				ImportedItems = importedItems,
				ImportedMovements = importedMovements,
				SkippedItems = skippedItems
			};
		}, cancellationToken).ConfigureAwait(false);
		_itemService.InvalidateImportCaches();
		_purposeService.InvalidateImportCache();
		_storageLocationService.InvalidateImportCache();
		return result;
	}

	private static string GetRequestHash(IEnumerable<ImportPreviewItem> items)
	{
		var content = items.Select(item => new
		{
			PartNumber = item.PartNumber.Trim(),
			Description = item.Description.Trim(),
			Manufacturer = string.IsNullOrWhiteSpace(item.Manufacturer) ? null : item.Manufacturer.Trim(),
			Category = string.IsNullOrWhiteSpace(item.Category) ? null : item.Category.Trim(),
			Purpose = item.Purpose.Trim(),
			Warehouse = item.Warehouse.Trim(),
			Location = item.Location.Trim(),
			item.Quantity,
			UnitPrice = item.UnitPrice.ToString("G29", CultureInfo.InvariantCulture)
		}).OrderBy(item => JsonSerializer.Serialize(item), StringComparer.Ordinal).ToArray();
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			"Depot.InventoryImport.v1\n" + JsonSerializer.Serialize(content))));
	}

	private static Guid OperationIdFor(string requestHash) => new(Convert.FromHexString(requestHash).AsSpan(0, 16));

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

