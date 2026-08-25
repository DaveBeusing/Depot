// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ItemService
{
	private readonly ItemRepository _itemRepository;
	private readonly AuditService _auditService;
	private readonly IItemReferenceDataService _manufacturerService;
	private readonly IItemReferenceDataService _categoryService;
	private readonly IItemReferenceDataService _unitOfMeasureService;
	private readonly IItemReferenceDataService _packagingService;
	private readonly SupplierItemRepository _supplierItems;

	public ItemService(
		ItemRepository itemRepository,
		AuditService auditService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService,
		SupplierItemRepository supplierItems)
	{
		_itemRepository = itemRepository;
		_auditService = auditService;
		_manufacturerService = manufacturerService;
		_categoryService = categoryService;
		_unitOfMeasureService = unitOfMeasureService;
		_packagingService = packagingService;
		_supplierItems = supplierItems;
	}

	public IReadOnlyList<Item> SearchItems(string? searchText)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsView);
		return _itemRepository.SearchActive(searchText);
	}

	public Item CreateItem(string partNumber, string description, string? manufacturer, string? category)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsCreate);
		(partNumber, description, manufacturer, category) = Normalize(partNumber, description, manufacturer, category);
		Validate(partNumber, description);
		if (_itemRepository.GetByPartNumber(partNumber) is not null)
			throw new InvalidOperationException($"Item '{partNumber}' already exists.");
		var item = new Item
		{
			PartNumber = partNumber,
			Description = description,
			ManufacturerId = ResolveSync(_manufacturerService, manufacturer),
			CategoryId = ResolveSync(_categoryService, category),
			IsActive = true
		};
		item.Id = _itemRepository.Create(item);
		_auditService.RecordCreated(item.Id, item);
		return item;
	}

	public Item UpdateItem(long id, long expectedVersion, string description, string? manufacturer, string? category)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsEdit);
		description = description.Trim();
		manufacturer = NormalizeOptional(manufacturer);
		category = NormalizeOptional(category);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
		var item = _itemRepository.GetById(id) ?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (item.Version != expectedVersion) throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.Description = description;
		item.Manufacturer = manufacturer;
		item.Category = category;
		if (!_itemRepository.Update(item)) throw new ConcurrencyConflictException("item");
		item.Version++;
		_auditService.RecordUpdated(item.Id, before, item);
		return item;
	}

	public void DeactivateItem(long id, long expectedVersion)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsManage);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var item = _itemRepository.GetById(id) ?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (_supplierItems.HasActiveForItem(id))
			throw new InvalidOperationException($"Item '{item.PartNumber}' has active supplier assignments and cannot be deactivated.");
		if (item.Version != expectedVersion || !_itemRepository.Deactivate(id, expectedVersion))
			throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.IsActive = false;
		item.Version++;
		_auditService.RecordDeactivated(item.Id, before, item);
	}

	public Task<PageResult<Item>> SearchItemsAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsView);
		return _itemRepository.SearchPageAsync(searchText, pageNumber, pageSize, cancellationToken);
	}

	public Task<PageResult<Item>> SearchItemsAsync(string? searchText, bool? isActive, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsView);
		return _itemRepository.SearchPageAsync(searchText, isActive, pageNumber, pageSize, cancellationToken);
	}

	public Task<PageResult<Item>> SearchItemMasterDataAsync(string? searchText, bool? isActive, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsView);
		return _itemRepository.SearchMasterDataPageAsync(searchText, isActive, pageNumber, pageSize, cancellationToken);
	}

	public async Task<Item> CreateItemAsync(string partNumber, string description, string? manufacturer, string? category, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsCreate);
		(partNumber, description, manufacturer, category) = Normalize(partNumber, description, manufacturer, category);
		Validate(partNumber, description);
		if (await _itemRepository.GetByPartNumberAsync(partNumber, cancellationToken) is not null)
			throw new InvalidOperationException($"Item '{partNumber}' already exists.");
		var manufacturerValue = await _manufacturerService.GetOrCreateAsync(manufacturer, cancellationToken);
		var categoryValue = await _categoryService.GetOrCreateAsync(category, cancellationToken);
		var item = new Item
		{
			PartNumber = partNumber,
			Description = description,
			ManufacturerId = manufacturerValue?.Id,
			CategoryId = categoryValue?.Id,
			IsActive = true
		};
		item.Id = await _itemRepository.CreateAsync(item, cancellationToken);
		await _auditService.RecordCreatedAsync(item.Id, item, cancellationToken);
		return item;
	}

	public async Task<Item> UpdateItemAsync(long id, long expectedVersion, string description, string? manufacturer, string? category, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsEdit);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var normalized = Normalize(string.Empty, description, manufacturer, category);
		description = normalized.Description;
		manufacturer = normalized.Manufacturer;
		category = normalized.Category;
		if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
		var item = await _itemRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (item.Version != expectedVersion) throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.Description = description;
		item.ManufacturerId = (await _manufacturerService.GetOrCreateAsync(manufacturer, cancellationToken))?.Id;
		item.CategoryId = (await _categoryService.GetOrCreateAsync(category, cancellationToken))?.Id;
		if (!await _itemRepository.UpdateAsync(item, cancellationToken)) throw new ConcurrencyConflictException("item");
		item.Version++;
		await _auditService.RecordUpdatedAsync(item.Id, before, item, cancellationToken);
		return item;
	}

	public async Task<Item> CreateItemWithReferencesAsync(string partNumber, string description, long? manufacturerId, long? categoryId, long? unitOfMeasureId, long? packagingId, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsCreate);
		partNumber = partNumber.Trim();
		description = description.Trim();
		Validate(partNumber, description);
		await ValidateReferencesAsync(manufacturerId, categoryId, unitOfMeasureId, packagingId, cancellationToken);
		if (await _itemRepository.GetByPartNumberAsync(partNumber, cancellationToken) is not null)
			throw new InvalidOperationException($"Item '{partNumber}' already exists.");
		var item = new Item
		{
			PartNumber = partNumber,
			Description = description,
			ManufacturerId = manufacturerId,
			CategoryId = categoryId,
			UnitOfMeasureId = unitOfMeasureId,
			PackagingId = packagingId,
			IsActive = true
		};
		item.Id = await _itemRepository.CreateAsync(item, cancellationToken);
		await _auditService.RecordCreatedAsync(item.Id, item, cancellationToken);
		return await _itemRepository.GetByIdAsync(item.Id, cancellationToken) ?? item;
	}

	public async Task<Item> UpdateItemWithReferencesAsync(long id, long expectedVersion, string description, long? manufacturerId, long? categoryId, long? unitOfMeasureId, long? packagingId, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsEdit);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		description = description.Trim();
		if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
		await ValidateReferencesAsync(manufacturerId, categoryId, unitOfMeasureId, packagingId, cancellationToken);
		var item = await _itemRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (item.Version != expectedVersion) throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.Description = description;
		item.ManufacturerId = manufacturerId;
		item.CategoryId = categoryId;
		item.UnitOfMeasureId = unitOfMeasureId;
		item.PackagingId = packagingId;
		if (!await _itemRepository.UpdateAsync(item, cancellationToken)) throw new ConcurrencyConflictException("item");
		item.Version++;
		await _auditService.RecordUpdatedAsync(item.Id, before, item, cancellationToken);
		return await _itemRepository.GetByIdAsync(item.Id, cancellationToken) ?? item;
	}

	public async Task<Item> CreateItemMasterDataAsync(
		string partNumber,
		string description,
		long? manufacturerId,
		long? categoryId,
		long? unitOfMeasureId,
		long? packagingId,
		ItemMasterDataInput masterData,
		CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsCreate);
		ArgumentNullException.ThrowIfNull(masterData);
		partNumber = partNumber.Trim();
		description = description.Trim();
		Validate(partNumber, description);
		await ValidateReferencesAsync(manufacturerId, categoryId, unitOfMeasureId, packagingId, cancellationToken);
		if (await _itemRepository.GetByPartNumberAsync(partNumber, cancellationToken) is not null)
			throw new InvalidOperationException($"Item '{partNumber}' already exists.");
		NormalizeAndValidateMasterData(masterData);
		await ValidateReplacementAsync(0, masterData.ReplacementItemId, cancellationToken);
		var item = new Item
		{
			PartNumber = partNumber,
			Description = description,
			ManufacturerId = manufacturerId,
			CategoryId = categoryId,
			UnitOfMeasureId = unitOfMeasureId,
			PackagingId = packagingId,
			IsActive = true
		};
		ApplyMasterData(item, masterData);
		item.Id = await _itemRepository.CreateMasterDataAsync(item, cancellationToken);
		await _auditService.RecordCreatedAsync(item.Id, item, cancellationToken);
		return await _itemRepository.GetMasterDataByIdAsync(item.Id, cancellationToken) ?? item;
	}

	public async Task<Item> UpdateItemMasterDataAsync(
		long id,
		long expectedVersion,
		string description,
		long? manufacturerId,
		long? categoryId,
		long? unitOfMeasureId,
		long? packagingId,
		ItemMasterDataInput masterData,
		CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsEdit);
		ArgumentNullException.ThrowIfNull(masterData);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		description = description.Trim();
		if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
		await ValidateReferencesAsync(manufacturerId, categoryId, unitOfMeasureId, packagingId, cancellationToken);
		NormalizeAndValidateMasterData(masterData);
		await ValidateReplacementAsync(id, masterData.ReplacementItemId, cancellationToken);
		var item = await _itemRepository.GetMasterDataByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (item.Version != expectedVersion) throw new ConcurrencyConflictException("item");
		var before = CopyMasterData(item);
		item.Description = description;
		item.ManufacturerId = manufacturerId;
		item.CategoryId = categoryId;
		item.UnitOfMeasureId = unitOfMeasureId;
		item.PackagingId = packagingId;
		ApplyMasterData(item, masterData);
		if (!await _itemRepository.UpdateMasterDataAsync(item, cancellationToken)) throw new ConcurrencyConflictException("item");
		item.Version++;
		await _auditService.RecordUpdatedAsync(item.Id, before, item, cancellationToken);
		return await _itemRepository.GetMasterDataByIdAsync(item.Id, cancellationToken) ?? item;
	}

	public async Task DeactivateItemAsync(long id, long expectedVersion, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsManage);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var item = await _itemRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (await _supplierItems.HasActiveForItemAsync(id, cancellationToken))
			throw new InvalidOperationException($"Item '{item.PartNumber}' has active supplier assignments and cannot be deactivated.");
		if (item.Version != expectedVersion || !await _itemRepository.DeactivateAsync(id, expectedVersion, cancellationToken))
			throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.IsActive = false;
		item.Version++;
		await _auditService.RecordDeactivatedAsync(item.Id, before, item, cancellationToken);
	}

	public async Task<Item> SetItemActiveAsync(long id, long expectedVersion, bool isActive, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsManage);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var item = await _itemRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (!isActive && await _supplierItems.HasActiveForItemAsync(id, cancellationToken))
			throw new InvalidOperationException($"Item '{item.PartNumber}' has active supplier assignments and cannot be deactivated.");
		if (item.Version != expectedVersion || !await _itemRepository.SetActiveAsync(id, expectedVersion, isActive, cancellationToken))
			throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.IsActive = isActive;
		item.Version++;
		if (isActive) await _auditService.RecordUpdatedAsync(item.Id, before, item, cancellationToken);
		else await _auditService.RecordDeactivatedAsync(item.Id, before, item, cancellationToken);
		return item;
	}

	private static Item Copy(Item item) =>
		new()
		{
			Id = item.Id,
			PartNumber = item.PartNumber,
			Description = item.Description,
			Manufacturer = item.Manufacturer,
			Category = item.Category,
			UnitOfMeasure = item.UnitOfMeasure,
			Packaging = item.Packaging,
			ManufacturerId = item.ManufacturerId,
			CategoryId = item.CategoryId,
			UnitOfMeasureId = item.UnitOfMeasureId,
			PackagingId = item.PackagingId,
			IsActive = item.IsActive,
			Version = item.Version
		};

	private static Item CopyMasterData(Item item)
	{
		var copy = Copy(item);
		copy.Gtin = item.Gtin;
		copy.ItemType = item.ItemType;
		copy.LifecycleStatus = item.LifecycleStatus;
		copy.CountryOfOrigin = item.CountryOfOrigin;
		copy.CustomsTariffNumber = item.CustomsTariffNumber;
		copy.TrackingMode = item.TrackingMode;
		copy.NetWeight = item.NetWeight;
		copy.Length = item.Length;
		copy.Width = item.Width;
		copy.Height = item.Height;
		copy.ReplacementItemId = item.ReplacementItemId;
		copy.Notes = item.Notes;
		return copy;
	}

	private static void ApplyMasterData(Item item, ItemMasterDataInput masterData)
	{
		item.Gtin = masterData.Gtin;
		item.ItemType = masterData.ItemType;
		item.LifecycleStatus = masterData.LifecycleStatus;
		item.CountryOfOrigin = masterData.CountryOfOrigin;
		item.CustomsTariffNumber = masterData.CustomsTariffNumber;
		item.TrackingMode = masterData.TrackingMode;
		item.NetWeight = masterData.NetWeight;
		item.Length = masterData.Length;
		item.Width = masterData.Width;
		item.Height = masterData.Height;
		item.ReplacementItemId = masterData.ReplacementItemId;
		item.Notes = masterData.Notes;
	}

	private static void NormalizeAndValidateMasterData(ItemMasterDataInput masterData)
	{
		masterData.Gtin = NormalizeOptional(masterData.Gtin);
		masterData.CountryOfOrigin = NormalizeOptional(masterData.CountryOfOrigin)?.ToUpperInvariant();
		masterData.CustomsTariffNumber = NormalizeOptional(masterData.CustomsTariffNumber);
		masterData.Notes = NormalizeOptional(masterData.Notes);
		if (!Enum.IsDefined(masterData.ItemType)) throw new ArgumentOutOfRangeException(nameof(masterData.ItemType));
		if (!Enum.IsDefined(masterData.LifecycleStatus)) throw new ArgumentOutOfRangeException(nameof(masterData.LifecycleStatus));
		if (!Enum.IsDefined(masterData.TrackingMode)) throw new ArgumentOutOfRangeException(nameof(masterData.TrackingMode));
		if (masterData.Gtin is not null && !IsValidGtin(masterData.Gtin)) throw new ArgumentException("GTIN must be a valid GTIN-8, GTIN-12, GTIN-13, or GTIN-14.", nameof(masterData.Gtin));
		if (masterData.CountryOfOrigin is not null && !IsIsoAlpha2Syntax(masterData.CountryOfOrigin)) throw new ArgumentException("Country of origin must use two ASCII letters (ISO 3166-1 alpha-2 syntax).", nameof(masterData.CountryOfOrigin));
		if (masterData.CustomsTariffNumber?.Length > 32) throw new ArgumentException("Customs tariff number must not exceed 32 characters.", nameof(masterData.CustomsTariffNumber));
		if (masterData.Notes?.Length > 4000) throw new ArgumentException("Notes must not exceed 4000 characters.", nameof(masterData.Notes));
		ValidateNonNegative(masterData.NetWeight, nameof(masterData.NetWeight));
		ValidateNonNegative(masterData.Length, nameof(masterData.Length));
		ValidateNonNegative(masterData.Width, nameof(masterData.Width));
		ValidateNonNegative(masterData.Height, nameof(masterData.Height));
	}

	private async Task ValidateReplacementAsync(long itemId, long? replacementItemId, CancellationToken cancellationToken)
	{
		if (replacementItemId is null) return;
		if (replacementItemId <= 0) throw new ArgumentException("Replacement item id must be positive.", nameof(replacementItemId));
		if (itemId > 0 && replacementItemId == itemId) throw new ArgumentException("An item cannot replace itself.", nameof(replacementItemId));
		if (await _itemRepository.GetByIdAsync(replacementItemId.Value, cancellationToken) is null)
			throw new ArgumentException("Replacement item was not found.", nameof(replacementItemId));
	}

	private static bool IsValidGtin(string value)
	{
		if (value.Length is not (8 or 12 or 13 or 14) || value.Any(character => character is < '0' or > '9')) return false;
		var sum = 0;
		var weight = 3;
		for (var index = value.Length - 2; index >= 0; index--)
		{
			sum += (value[index] - '0') * weight;
			weight = weight == 3 ? 1 : 3;
		}
		var checkDigit = (10 - (sum % 10)) % 10;
		return checkDigit == value[^1] - '0';
	}

	private static bool IsIsoAlpha2Syntax(string value) =>
		value.Length == 2 && value.All(character => character is >= 'A' and <= 'Z');

	private static void ValidateNonNegative(decimal? value, string parameterName)
	{
		if (value < 0) throw new ArgumentOutOfRangeException(parameterName, "Value must not be negative.");
	}

	private static (string PartNumber, string Description, string? Manufacturer, string? Category) Normalize(string partNumber, string description, string? manufacturer, string? category) =>
		(partNumber.Trim(), description.Trim(), NormalizeOptional(manufacturer), NormalizeOptional(category));

	private static string? NormalizeOptional(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static void Validate(string partNumber, string description)
	{
		if (string.IsNullOrWhiteSpace(partNumber)) throw new ArgumentException("Part number is required.", nameof(partNumber));
		if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
	}

	private static long? ResolveSync(IItemReferenceDataService service, string? name) =>
		service.GetOrCreateAsync(name).GetAwaiter().GetResult()?.Id;

	private async Task ValidateReferencesAsync(long? manufacturerId, long? categoryId, long? unitOfMeasureId, long? packagingId, CancellationToken cancellationToken)
	{
		await Task.WhenAll(
			_manufacturerService.ValidateSelectionAsync(manufacturerId, cancellationToken),
			_categoryService.ValidateSelectionAsync(categoryId, cancellationToken),
			_unitOfMeasureService.ValidateSelectionAsync(unitOfMeasureId, cancellationToken),
			_packagingService.ValidateSelectionAsync(packagingId, cancellationToken));
	}
}
