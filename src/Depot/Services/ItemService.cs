// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ItemService
{
	private readonly ItemRepository _itemRepository;
	private readonly AuditService _auditService;

	public ItemService(
		ItemRepository itemRepository,
		AuditService auditService)
	{
		_itemRepository = itemRepository;
		_auditService = auditService;
	}

	public IReadOnlyList<Item> GetItems()
	{
		return _itemRepository.GetAll();
	}

	public IReadOnlyList<Item> SearchItems(
		string? searchText)
	{
		return _itemRepository.SearchActive(
			searchText);
	}

	public Item CreateItem(
		string partNumber,
		string description,
		string? manufacturer,
		string? category)
	{
		partNumber = partNumber.Trim();
		description = description.Trim();
		manufacturer = string.IsNullOrWhiteSpace(manufacturer)
			? null
			: manufacturer.Trim();
		category = string.IsNullOrWhiteSpace(category)
			? null
			: category.Trim();

		if (string.IsNullOrWhiteSpace(partNumber))
		{
			throw new ArgumentException(
				"Part number is required.",
				nameof(partNumber));
		}

		if (string.IsNullOrWhiteSpace(description))
		{
			throw new ArgumentException(
				"Description is required.",
				nameof(description));
		}

		var existingItem =
			_itemRepository.GetByPartNumber(
				partNumber);

		if (existingItem is not null)
		{
			throw new InvalidOperationException(
				$"Item '{partNumber}' already exists.");
		}

		var item =
			new Item
			{
				PartNumber = partNumber,
				Description = description,
				Manufacturer = manufacturer,
				Category = category,
				IsActive = true
			};

		item.Id =
			_itemRepository.Create(
				item);

		_auditService.RecordCreated(item.Id, item);

		return item;
	}

	public Item UpdateItem(
		long id,
		long expectedVersion,
		string description,
		string? manufacturer,
		string? category)
	{
		description = description.Trim();
		manufacturer = string.IsNullOrWhiteSpace(manufacturer)
			? null
			: manufacturer.Trim();
		category = string.IsNullOrWhiteSpace(category)
			? null
			: category.Trim();

		if (id <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(id));
		}

		if (string.IsNullOrWhiteSpace(description))
		{
			throw new ArgumentException(
				"Description is required.",
				nameof(description));
		}

		var item =
			_itemRepository.GetById(
				id);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{id}' was not found.");
		}

		if (item.Version != expectedVersion)
		{
			throw new ConcurrencyConflictException("item");
		}

		var before = Copy(item);

		item.Description = description;
		item.Manufacturer = manufacturer;
		item.Category = category;

		if (!_itemRepository.Update(item))
		{
			throw new ConcurrencyConflictException("item");
		}

		item.Version++;
		_auditService.RecordUpdated(item.Id, before, item);

		return item;
	}

	public void DeactivateItem(
		long id,
		long expectedVersion)
	{
		if (id <= 0)
		{
			throw new ArgumentException(
				"Item id is required.",
				nameof(id));
		}

		var item =
			_itemRepository.GetById(
				id);

		if (item is null)
		{
			throw new InvalidOperationException(
				$"Item with id '{id}' was not found.");
		}

		if (item.Version != expectedVersion ||
			!_itemRepository.Deactivate(id, expectedVersion))
		{
			throw new ConcurrencyConflictException("item");
		}

		var before = Copy(item);
		item.IsActive = false;
		item.Version++;
		_auditService.RecordDeactivated(item.Id, before, item);
	}

	public Task<PageResult<Item>> SearchItemsAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken) =>
		_itemRepository.SearchPageAsync(searchText, pageNumber, pageSize, cancellationToken);

	public async Task<Item> CreateItemAsync(
		string partNumber,
		string description,
		string? manufacturer,
		string? category,
		CancellationToken cancellationToken)
	{
		(partNumber, description, manufacturer, category) = Normalize(
			partNumber,
			description,
			manufacturer,
			category);
		Validate(partNumber, description);
		if (await _itemRepository.GetByPartNumberAsync(partNumber, cancellationToken) is not null)
		{
			throw new InvalidOperationException($"Item '{partNumber}' already exists.");
		}

		var item = new Item
		{
			PartNumber = partNumber,
			Description = description,
			Manufacturer = manufacturer,
			Category = category,
			IsActive = true
		};
		item.Id = await _itemRepository.CreateAsync(item, cancellationToken);
		await _auditService.RecordCreatedAsync(item.Id, item, cancellationToken);
		return item;
	}

	public async Task<Item> UpdateItemAsync(
		long id,
		long expectedVersion,
		string description,
		string? manufacturer,
		string? category,
		CancellationToken cancellationToken)
	{
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var normalized = Normalize(string.Empty, description, manufacturer, category);
		description = normalized.Description;
		manufacturer = normalized.Manufacturer;
		category = normalized.Category;
		if (string.IsNullOrWhiteSpace(description))
		{
			throw new ArgumentException("Description is required.", nameof(description));
		}

		var item = await _itemRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (item.Version != expectedVersion) throw new ConcurrencyConflictException("item");
		var before = Copy(item);
		item.Description = description;
		item.Manufacturer = manufacturer;
		item.Category = category;
		if (!await _itemRepository.UpdateAsync(item, cancellationToken))
		{
			throw new ConcurrencyConflictException("item");
		}
		item.Version++;
		await _auditService.RecordUpdatedAsync(item.Id, before, item, cancellationToken);
		return item;
	}

	public async Task DeactivateItemAsync(
		long id,
		long expectedVersion,
		CancellationToken cancellationToken)
	{
		if (id <= 0) throw new ArgumentException("Item id is required.", nameof(id));
		var item = await _itemRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Item with id '{id}' was not found.");
		if (item.Version != expectedVersion ||
			!await _itemRepository.DeactivateAsync(id, expectedVersion, cancellationToken))
		{
			throw new ConcurrencyConflictException("item");
		}
		var before = Copy(item);
		item.IsActive = false;
		item.Version++;
		await _auditService.RecordDeactivatedAsync(item.Id, before, item, cancellationToken);
	}

	private static Item Copy(Item item) =>
		new()
		{
			Id = item.Id,
			PartNumber = item.PartNumber,
			Description = item.Description,
			Manufacturer = item.Manufacturer,
			Category = item.Category,
			IsActive = item.IsActive,
			Version = item.Version
		};

	private static (string PartNumber, string Description, string? Manufacturer, string? Category) Normalize(
		string partNumber,
		string description,
		string? manufacturer,
		string? category) =>
		(
			partNumber.Trim(),
			description.Trim(),
			string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer.Trim(),
			string.IsNullOrWhiteSpace(category) ? null : category.Trim()
		);

	private static void Validate(string partNumber, string description)
	{
		if (string.IsNullOrWhiteSpace(partNumber))
			throw new ArgumentException("Part number is required.", nameof(partNumber));
		if (string.IsNullOrWhiteSpace(description))
			throw new ArgumentException("Description is required.", nameof(description));
	}
}
