// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Repositories;

namespace Depot.ViewModels;

public sealed class MovementsViewModel
	: BaseViewModel
{
	private readonly ItemRepository _itemRepository;
	private readonly StockMovementRepository _stockMovementRepository;

	public MovementsViewModel(
		ItemRepository itemRepository,
		StockMovementRepository stockMovementRepository)
	{
		_itemRepository = itemRepository;
		_stockMovementRepository = stockMovementRepository;

		Load();
	}

	public ObservableCollection<MovementOverviewItemViewModel> Items { get; }
		= new();

	public void Load()
	{
		Items.Clear();

		var items =
			_itemRepository
				.GetAll()
				.ToDictionary(
					x => x.Id);

		foreach (var movement in _stockMovementRepository.GetAll())
		{
			if (!items.TryGetValue(
				movement.ItemId,
				out var item))
			{
				continue;
			}

			var overview =
				new MovementOverviewItem
				{
					MovementId =
						movement.Id,

					TimestampUtc =
						movement.TimestampUtc,

					ItemId =
						item.Id,

					PartNumber =
						item.PartNumber,

					Description =
						item.Description,

					MovementType =
						movement.MovementType,

					Quantity =
						movement.Quantity,

					UnitPrice =
						movement.UnitPrice,

					Reference =
						movement.Reference,

					Notes =
						movement.Notes
				};

			Items.Add(
				new MovementOverviewItemViewModel(
					overview));
		}
	}
}