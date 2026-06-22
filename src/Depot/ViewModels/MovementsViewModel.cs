// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class MovementsViewModel
	: BaseViewModel
{
	private readonly ItemRepository _itemRepository;
	private readonly StockMovementRepository _stockMovementRepository;
	private readonly StockService _stockService;

	private ItemLookupViewModel? _selectedItem;

	private string? _errorMessage;

	public MovementsViewModel(
		ItemRepository itemRepository,
		StockMovementRepository stockMovementRepository,
		StockService stockService)
	{
		_itemRepository = itemRepository;
		_stockMovementRepository = stockMovementRepository;
		_stockService = stockService;

		Editor =
			new MovementEditorViewModel();

		CreateMovementCommand =
			new RelayCommand(
				CreateMovement);

		LoadItems();
		Load();
	}

	public ObservableCollection<ItemLookupViewModel> AvailableItems { get; }
		= new();

	public ObservableCollection<MovementOverviewItemViewModel> Items { get; }
		= new();

	public MovementEditorViewModel Editor { get; }

	public RelayCommand CreateMovementCommand { get; }

	public ItemLookupViewModel? SelectedItem
	{
		get => _selectedItem;

		set
		{
			_selectedItem = value;

			OnPropertyChanged();

			if (value is not null)
			{
				Editor.ItemId =
					value.Id;
			}
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;

		private set
		{
			_errorMessage = value;

			OnPropertyChanged();
			OnPropertyChanged(
				nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage =>
		!string.IsNullOrWhiteSpace(
			ErrorMessage);

	private void LoadItems()
	{
		AvailableItems.Clear();

		foreach (var item in _itemRepository.GetAll())
		{
			AvailableItems.Add(
				new ItemLookupViewModel
				{
					Id = item.Id,
					PartNumber = item.PartNumber,
					Description = item.Description
				});
		}
	}

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

	private void CreateMovement()
	{
		try
		{
			ErrorMessage = null;

			switch (Editor.MovementType)
			{
				case StockMovementType.Purchase:

					_stockService.AddPurchase(
						Editor.ItemId,
						Editor.Quantity,
						Editor.UnitPrice,
						Editor.Reference,
						Editor.Notes);

					break;

				case StockMovementType.Withdrawal:

					_stockService.AddWithdrawal(
						Editor.ItemId,
						Editor.Quantity,
						Editor.Reference,
						Editor.Notes);

					break;

				case StockMovementType.Correction:

					_stockService.AddCorrection(
						Editor.ItemId,
						Editor.Quantity,
						Editor.Reference,
						Editor.Notes);

					break;

				default:

					throw new InvalidOperationException(
						$"Movement type '{Editor.MovementType}' is not supported.");
			}

			Editor.Clear();

			Load();
		}
		catch (Exception ex)
		{
			ErrorMessage =
				ex.Message;
		}
	}
}