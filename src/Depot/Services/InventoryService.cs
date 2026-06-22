// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class InventoryService
{
	private readonly ItemRepository _itemRepository;

	public InventoryService(ItemRepository itemRepository)
	{
		_itemRepository = itemRepository;
	}

	public IReadOnlyList<Item> GetItems()
	{
		return _itemRepository.GetAll();
	}

	public Item CreateItem(
		string partNumber,
		string description,
		string? manufacturer,
		string? category)
	{
		partNumber = partNumber.Trim();
		description = description.Trim();

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
			_itemRepository.GetByPartNumber(partNumber);

		if (existingItem is not null)
		{
			throw new InvalidOperationException(
				$"Item '{partNumber}' already exists.");
		}

		var item = new Item
		{
			PartNumber = partNumber,
			Description = description,
			Manufacturer = manufacturer,
			Category = category,
			IsActive = true
		};

		item.Id =
			_itemRepository.Create(item);

		return item;
	}

}