// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ItemService
{
	private readonly ItemRepository _itemRepository;

	public ItemService(
		ItemRepository itemRepository)
	{
		_itemRepository = itemRepository;
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

		return item;
	}

	public Item UpdateItem(
		long id,
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

		item.Description = description;
		item.Manufacturer = manufacturer;
		item.Category = category;

		_itemRepository.Update(
			item);

		return item;
	}

	public void DeactivateItem(
		long id)
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

		_itemRepository.Deactivate(
			id);
	}
}