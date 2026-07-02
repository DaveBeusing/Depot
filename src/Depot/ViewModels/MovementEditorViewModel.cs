// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class MovementEditorViewModel
	: BaseViewModel
{
	private long _inventoryId;

	private StockMovementType _movementType =
		StockMovementType.Purchase;

	private int _quantity;

	private decimal _unitPrice;

	private string? _reference;

	private string? _notes;

	public long InventoryId
	{
		get => _inventoryId;

		set
		{
			_inventoryId = value;

			OnPropertyChanged();
		}
	}

	public StockMovementType MovementType
	{
		get => _movementType;

		set
		{
			_movementType = value;

			OnPropertyChanged();

			OnPropertyChanged(
				nameof(RequiresUnitPrice));
		}
	}

	public bool RequiresUnitPrice =>
		MovementType ==
		StockMovementType.Purchase;

	public int Quantity
	{
		get => _quantity;

		set
		{
			_quantity = value;

			OnPropertyChanged();
		}
	}

	public decimal UnitPrice
	{
		get => _unitPrice;

		set
		{
			_unitPrice = value;

			OnPropertyChanged();
		}
	}

	public string? Reference
	{
		get => _reference;

		set
		{
			_reference = value;

			OnPropertyChanged();
		}
	}

	public string? Notes
	{
		get => _notes;

		set
		{
			_notes = value;

			OnPropertyChanged();
		}
	}

	public void Clear()
	{
		InventoryId = 0;

		MovementType =
			StockMovementType.Purchase;

		Quantity = 0;

		UnitPrice = 0m;

		Reference = null;

		Notes = null;
	}
}
