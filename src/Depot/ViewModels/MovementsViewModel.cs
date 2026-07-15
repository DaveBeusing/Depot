// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class MovementsViewModel
	: BaseViewModel
{
	private readonly MovementService _movementService;

	private InventoryLookupViewModel? _selectedInventory;
	private string? _errorMessage;
	private string _searchText = string.Empty;

	public MovementsViewModel(
		MovementService movementService)
	{
		_movementService = movementService;

		Editor =
			new MovementEditorViewModel();

		CreateMovementCommand =
			new RelayCommand(
				CreateMovement);

		Load();
	}

	public ObservableCollection<InventoryLookupViewModel> AvailableInventories { get; }
		= new();

	public ObservableCollection<MovementOverviewItemViewModel> Items { get; }
		= new();

	public IReadOnlyList<StockMovementType> MovementTypes { get; } =
	[
		StockMovementType.Purchase,
		StockMovementType.Withdrawal,
		StockMovementType.Correction
	];

	public bool HasItems => Items.Count > 0;

	public bool HasNoItems => !HasItems;

	public MovementEditorViewModel Editor { get; }

	public RelayCommand CreateMovementCommand { get; }

	public string SearchText
	{
		get => _searchText;

		set
		{
			if (_searchText == value)
			{
				return;
			}

			_searchText = value;
			OnPropertyChanged();

			LoadMovements();
		}
	}

	public InventoryLookupViewModel? SelectedInventory
	{
		get => _selectedInventory;

		set
		{
			if (_selectedInventory == value)
			{
				return;
			}

			_selectedInventory = value;

			OnPropertyChanged();

			if (value is not null)
			{
				Editor.InventoryId =
					value.Id;
			}
			else
			{
				Editor.InventoryId = 0;
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

	private void LoadInventories()
	{
		AvailableInventories.Clear();

		foreach (var inventory in _movementService.GetAvailableInventories())
		{
			AvailableInventories.Add(
				new InventoryLookupViewModel
				{
					Id =
						inventory.Id,

					ItemId =
						inventory.ItemId,

					PartNumber =
						inventory.PartNumber,

					Description =
						inventory.Description,

					PurposeName =
						inventory.PurposeName,

					LocationName =
						inventory.LocationName
				});
		}
	}

	public void Load()
	{
		LoadInventories();
		LoadMovements();
	}

	private void LoadMovements()
	{

		Items.Clear();

		foreach (var movement in _movementService.Search(SearchText))
		{
			Items.Add(
				new MovementOverviewItemViewModel(
					movement));
		}

		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasNoItems));
	}

	private void CreateMovement()
	{
		try
		{
			ErrorMessage = null;

			switch (Editor.MovementType)
			{
				case StockMovementType.Purchase:

						_movementService.AddPurchase(
						Editor.InventoryId,
						Editor.Quantity,
						Editor.UnitPrice,
						Editor.Reference,
						Editor.Notes);

					break;

				case StockMovementType.Withdrawal:

					_movementService.AddWithdrawal(
						Editor.InventoryId,
						Editor.Quantity,
						Editor.Reference,
						Editor.Notes);

					break;

				case StockMovementType.Correction:

					_movementService.AddCorrection(
						Editor.InventoryId,
						Editor.Quantity,
						Editor.Reference,
						Editor.Notes);

					break;

				default:

					throw new InvalidOperationException(
						$"Movement type '{Editor.MovementType}' is not supported.");
			}

			Editor.Clear();
			SelectedInventory = null;

			Load();
		}
		catch (Exception ex)
		{
			ErrorMessage =
				ex.Message;
		}
	}
}
