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

	public Task<IReadOnlyList<Item>> GetReplacementCandidatesAsync(CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsView);
		return _itemRepository.GetActiveItemsAsync(cancellationToken);
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
		await ValidateUniqueGtinAsync(0, masterData.Gtin, cancellationToken);
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
		await ValidateUniqueGtinAsync(id, masterData.Gtin, cancellationToken);
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
		var item = await _itemRepository.GetMasterDataByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (await _supplierItems.HasActiveForItemAsync(id, cancellationToken))
			throw new InvalidOperationException($"Item '{item.PartNumber}' has active supplier assignments and cannot be deactivated.");
		if (item.Version != expectedVersion || !await _itemRepository.DeactivateAsync(id, expectedVersion, cancellationToken))
			throw new ConcurrencyConflictException("item");
		var before = CopyMasterData(item);
		item.IsActive = false;
		item.Version++;
		await _auditService.RecordDeactivatedAsync(item.Id, before, item, cancellationToken);
	}

	public async Task<Item> SetItemActiveAsync(long id, long expectedVersion, bool isActive, CancellationToken cancellationToken)
	{
		_auditService.RequirePermission(ApplicationPermission.ItemsManage);
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var item = await _itemRepository.GetMasterDataByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (!isActive && await _supplierItems.HasActiveForItemAsync(id, cancellationToken))
			throw new InvalidOperationException($"Item '{item.PartNumber}' has active supplier assignments and cannot be deactivated.");
		if (item.Version != expectedVersion || !await _itemRepository.SetActiveAsync(id, expectedVersion, isActive, cancellationToken))
			throw new ConcurrencyConflictException("item");
		var before = CopyMasterData(item);
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
		copy.Revision = item.Revision;
		copy.Model = item.Model;
		copy.ProductFamily = item.ProductFamily;
		copy.CountryOfOrigin = item.CountryOfOrigin;
		copy.CustomsTariffNumber = item.CustomsTariffNumber;
		copy.Eccn = item.Eccn;
		copy.TrackingMode = item.TrackingMode;
		copy.NetWeightKg = item.NetWeightKg;
		copy.GrossWeightKg = item.GrossWeightKg;
		copy.LengthMm = item.LengthMm;
		copy.WidthMm = item.WidthMm;
		copy.HeightMm = item.HeightMm;
		copy.IsDangerousGoods = item.IsDangerousGoods;
		copy.UnNumber = item.UnNumber;
		copy.ContainsBattery = item.ContainsBattery;
		copy.RohsStatus = item.RohsStatus;
		copy.ReachStatus = item.ReachStatus;
		copy.IntroductionDate = item.IntroductionDate;
		copy.EndOfLifeDate = item.EndOfLifeDate;
		copy.LastBuyDate = item.LastBuyDate;
		copy.EndOfSupportDate = item.EndOfSupportDate;
		copy.ReplacementItemId = item.ReplacementItemId;
		copy.Notes = item.Notes;
		return copy;
	}

	private static void ApplyMasterData(Item item, ItemMasterDataInput masterData)
	{
		item.Gtin = masterData.Gtin;
		item.ItemType = masterData.ItemType;
		item.LifecycleStatus = masterData.LifecycleStatus;
		item.Revision = masterData.Revision;
		item.Model = masterData.Model;
		item.ProductFamily = masterData.ProductFamily;
		item.CountryOfOrigin = masterData.CountryOfOrigin;
		item.CustomsTariffNumber = masterData.CustomsTariffNumber;
		item.Eccn = masterData.Eccn;
		item.TrackingMode = masterData.TrackingMode;
		item.NetWeightKg = masterData.NetWeightKg;
		item.GrossWeightKg = masterData.GrossWeightKg;
		item.LengthMm = masterData.LengthMm;
		item.WidthMm = masterData.WidthMm;
		item.HeightMm = masterData.HeightMm;
		item.IsDangerousGoods = masterData.IsDangerousGoods;
		item.UnNumber = masterData.UnNumber;
		item.ContainsBattery = masterData.ContainsBattery;
		item.RohsStatus = masterData.RohsStatus;
		item.ReachStatus = masterData.ReachStatus;
		item.IntroductionDate = masterData.IntroductionDate;
		item.EndOfLifeDate = masterData.EndOfLifeDate;
		item.LastBuyDate = masterData.LastBuyDate;
		item.EndOfSupportDate = masterData.EndOfSupportDate;
		item.ReplacementItemId = masterData.ReplacementItemId;
		item.Notes = masterData.Notes;
	}

	private static void NormalizeAndValidateMasterData(ItemMasterDataInput masterData)
	{
		masterData.Gtin = NormalizeOptional(masterData.Gtin);
		masterData.Revision = NormalizeOptional(masterData.Revision);
		masterData.Model = NormalizeOptional(masterData.Model);
		masterData.ProductFamily = NormalizeOptional(masterData.ProductFamily);
		masterData.CountryOfOrigin = NormalizeOptional(masterData.CountryOfOrigin)?.ToUpperInvariant();
		masterData.CustomsTariffNumber = NormalizeOptional(masterData.CustomsTariffNumber);
		masterData.Eccn = NormalizeOptional(masterData.Eccn)?.ToUpperInvariant();
		masterData.UnNumber = NormalizeUnNumber(masterData.UnNumber);
		masterData.Notes = NormalizeOptional(masterData.Notes);
		masterData.IntroductionDate = masterData.IntroductionDate?.Date;
		masterData.EndOfLifeDate = masterData.EndOfLifeDate?.Date;
		masterData.LastBuyDate = masterData.LastBuyDate?.Date;
		masterData.EndOfSupportDate = masterData.EndOfSupportDate?.Date;

		if (!Enum.IsDefined(masterData.ItemType)) throw new ArgumentOutOfRangeException(nameof(masterData.ItemType));
		if (!Enum.IsDefined(masterData.LifecycleStatus)) throw new ArgumentOutOfRangeException(nameof(masterData.LifecycleStatus));
		if (!Enum.IsDefined(masterData.TrackingMode)) throw new ArgumentOutOfRangeException(nameof(masterData.TrackingMode));
		if (!Enum.IsDefined(masterData.RohsStatus)) throw new ArgumentOutOfRangeException(nameof(masterData.RohsStatus));
		if (!Enum.IsDefined(masterData.ReachStatus)) throw new ArgumentOutOfRangeException(nameof(masterData.ReachStatus));
		if (masterData.Gtin is not null && !IsValidGtin(masterData.Gtin)) throw new ArgumentException("GTIN must be a valid GTIN-8, GTIN-12, GTIN-13, or GTIN-14.", nameof(masterData.Gtin));
		if (masterData.CountryOfOrigin is not null && !IsIsoAlpha2Syntax(masterData.CountryOfOrigin)) throw new ArgumentException("Country of origin must use two ASCII letters (ISO 3166-1 alpha-2 syntax).", nameof(masterData.CountryOfOrigin));
		ValidateLength(masterData.Revision, 64, nameof(masterData.Revision));
		ValidateLength(masterData.Model, 128, nameof(masterData.Model));
		ValidateLength(masterData.ProductFamily, 128, nameof(masterData.ProductFamily));
		ValidateLength(masterData.CustomsTariffNumber, 32, nameof(masterData.CustomsTariffNumber));
		ValidateLength(masterData.Eccn, 32, nameof(masterData.Eccn));
		ValidateLength(masterData.Notes, 4000, nameof(masterData.Notes));
		ValidateNonNegative(masterData.NetWeightKg, nameof(masterData.NetWeightKg));
		ValidateNonNegative(masterData.GrossWeightKg, nameof(masterData.GrossWeightKg));
		ValidateNonNegative(masterData.LengthMm, nameof(masterData.LengthMm));
		ValidateNonNegative(masterData.WidthMm, nameof(masterData.WidthMm));
		ValidateNonNegative(masterData.HeightMm, nameof(masterData.HeightMm));
		if (masterData.NetWeightKg is not null && masterData.GrossWeightKg is not null && masterData.GrossWeightKg < masterData.NetWeightKg)
			throw new ArgumentException("Gross weight must not be lower than net weight.", nameof(masterData.GrossWeightKg));
		if (masterData.IsDangerousGoods && masterData.UnNumber is null)
			throw new ArgumentException("Dangerous goods require a UN number.", nameof(masterData.UnNumber));
		if (masterData.IntroductionDate is not null && masterData.EndOfLifeDate is not null && masterData.EndOfLifeDate < masterData.IntroductionDate)
			throw new ArgumentException("End-of-life date must not be before introduction date.", nameof(masterData.EndOfLifeDate));
		if (masterData.IntroductionDate is not null && masterData.LastBuyDate is not null && masterData.LastBuyDate < masterData.IntroductionDate)
			throw new ArgumentException("Last-buy date must not be before introduction date.", nameof(masterData.LastBuyDate));
		if (masterData.EndOfLifeDate is not null && masterData.EndOfSupportDate is not null && masterData.EndOfSupportDate < masterData.EndOfLifeDate)
			throw new ArgumentException("End-of-support date must not be before end-of-life date.", nameof(masterData.EndOfSupportDate));
	}

	private async Task ValidateUniqueGtinAsync(long itemId, string? gtin, CancellationToken cancellationToken)
	{
		if (gtin is null) return;
		var existing = await _itemRepository.GetByGtinAsync(gtin, cancellationToken);
		if (existing is not null && existing.Id != itemId)
			throw new InvalidOperationException($"GTIN '{gtin}' is already assigned to item '{existing.PartNumber}'.");
	}

	private async Task ValidateReplacementAsync(long itemId, long? replacementItemId, CancellationToken cancellationToken)
	{
		if (replacementItemId is null) return;
		if (replacementItemId <= 0) throw new ArgumentException("Replacement item id must be positive.", nameof(replacementItemId));
		if (itemId > 0 && replacementItemId == itemId) throw new ArgumentException("An item cannot replace itself.", nameof(replacementItemId));
		var replacement = await _itemRepository.GetByIdAsync(replacementItemId.Value, cancellationToken);
		if (replacement is null) throw new ArgumentException("Replacement item was not found.", nameof(replacementItemId));
		if (!replacement.IsActive) throw new ArgumentException("Replacement item must be active.", nameof(replacementItemId));
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

	private static string? NormalizeUnNumber(string? value)
	{
		value = NormalizeOptional(value)?.ToUpperInvariant();
		if (value is null) return null;
		if (value.Length == 4 && value.All(char.IsDigit)) value = $"UN{value}";
		if (value.Length != 6 || !value.StartsWith("UN", StringComparison.Ordinal) || !value.AsSpan(2).ToArray().All(char.IsDigit))
			throw new ArgumentException("UN number must use the format UN1234.", nameof(value));
		return value;
	}

	private static void ValidateLength(string? value, int maximumLength, string parameterName)
	{
		if (value?.Length > maximumLength) throw new ArgumentException($"Value must not exceed {maximumLength} characters.", parameterName);
	}

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
