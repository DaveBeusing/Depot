// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;

namespace Depot.ViewModels;

public sealed class InventoryDetailsViewModel
	: BaseViewModel
{
	private long _inventoryId;
	private long _itemId;
	private string _partNumber = string.Empty;
	private string _description = string.Empty;
	private string? _manufacturer;
	private string? _category;
	private string _purposeName = string.Empty;
	private string _warehouseName = string.Empty;
	private string _locationName = string.Empty;
	private int _currentStock;
	private decimal _averageCost;
	private decimal _inventoryValue;

	public long InventoryId
	{
		get => _inventoryId;
		private set
		{
			_inventoryId = value;
			OnPropertyChanged();
		}
	}

	public long ItemId
	{
		get => _itemId;
		private set
		{
			_itemId = value;
			OnPropertyChanged();
		}
	}

	public string PartNumber
	{
		get => _partNumber;
		private set
		{
			_partNumber = value;
			OnPropertyChanged();
		}
	}

	public string Description
	{
		get => _description;
		private set
		{
			_description = value;
			OnPropertyChanged();
		}
	}

	public string? Manufacturer
	{
		get => _manufacturer;
		private set
		{
			_manufacturer = value;
			OnPropertyChanged();
		}
	}

	public string? Category
	{
		get => _category;
		private set
		{
			_category = value;
			OnPropertyChanged();
		}
	}

	public string PurposeName
	{
		get => _purposeName;
		private set
		{
			_purposeName = value;
			OnPropertyChanged();
		}
	}

	public string LocationName
	{
		get => _locationName;
		private set
		{
			_locationName = value;
			OnPropertyChanged();
		}
	}

	public string WarehouseName
	{
		get => _warehouseName;
		private set
		{
			_warehouseName = value;
			OnPropertyChanged();
		}
	}

	public int CurrentStock
	{
		get => _currentStock;
		private set
		{
			_currentStock = value;
			OnPropertyChanged();
		}
	}

	public decimal AverageCost
	{
		get => _averageCost;
		private set
		{
			_averageCost = value;
			OnPropertyChanged();
		}
	}

	public decimal InventoryValue
	{
		get => _inventoryValue;
		private set
		{
			_inventoryValue = value;
			OnPropertyChanged();
		}
	}

	public ObservableCollection<InventoryRecentMovementViewModel> RecentMovements { get; }
		= new();

	public bool HasRecentMovements => RecentMovements.Count > 0;

	public bool HasNoRecentMovements => !HasRecentMovements;

	public void Load(
		InventoryDetails details)
	{
		InventoryId = details.InventoryId;
		ItemId = details.ItemId;
		PartNumber = details.PartNumber;
		Description = details.Description;
		Manufacturer = details.Manufacturer;
		Category = details.Category;
		PurposeName = details.PurposeName;
		WarehouseName = details.WarehouseName;
		LocationName = details.LocationName;
		CurrentStock = details.CurrentStock;
		AverageCost = details.AverageCost;
		InventoryValue = details.InventoryValue;

		RecentMovements.Clear();

		foreach (var movement in details.RecentMovements)
		{
			RecentMovements.Add(
				new InventoryRecentMovementViewModel(
					movement));
		}

		OnPropertyChanged(nameof(HasRecentMovements));
		OnPropertyChanged(nameof(HasNoRecentMovements));
	}

	public void Clear()
	{
		InventoryId = 0;
		ItemId = 0;
		PartNumber = string.Empty;
		Description = string.Empty;
		Manufacturer = null;
		Category = null;
		PurposeName = string.Empty;
		WarehouseName = string.Empty;
		LocationName = string.Empty;
		CurrentStock = 0;
		AverageCost = 0m;
		InventoryValue = 0m;
		RecentMovements.Clear();
		OnPropertyChanged(nameof(HasRecentMovements));
		OnPropertyChanged(nameof(HasNoRecentMovements));
	}
}
