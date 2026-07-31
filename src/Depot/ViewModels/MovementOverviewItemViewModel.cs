// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class MovementOverviewItemViewModel
	: BaseViewModel
{
	private bool _isReversed;
	public MovementOverviewItemViewModel(
		MovementOverviewItem item)
	{
		MovementId = item.MovementId;
		TimestampUtc = item.TimestampUtc;
		InventoryId = item.InventoryId;
		ItemId = item.ItemId;
		PartNumber = item.PartNumber;
		Description = item.Description;
		PurposeName = item.PurposeName;
		WarehouseName = item.WarehouseName;
		LocationName = item.LocationName;
		ReasonCodeName = item.ReasonCodeName;
		MovementType = item.MovementType;
		Quantity = item.Quantity;
		UnitPrice = item.UnitPrice;
		Reference = item.Reference;
		Notes = item.Notes;
		ReversalOfMovementId = item.ReversalOfMovementId;
		_isReversed = item.IsReversed;
	}

	public long MovementId { get; }

	public DateTime TimestampUtc { get; }

	public DateTime TimestampLocal =>
		TimestampUtc.ToLocalTime();

	public long InventoryId { get; }

	public long ItemId { get; }

	public string PartNumber { get; }

	public string Description { get; }

	public string PurposeName { get; }

	public string WarehouseName { get; }

	public string LocationName { get; }

	public string? ReasonCodeName { get; }

	public StockMovementType MovementType { get; }

	public int Quantity { get; }

	public decimal? UnitPrice { get; }

	public string? Reference { get; }

	public string? Notes { get; }

	public long? ReversalOfMovementId { get; }

	public bool IsReversed
	{
		get => _isReversed;
		private set
		{
			if (_isReversed == value) return;
			_isReversed = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(CanReverse));
		}
	}

	public bool CanReverse => MovementType == StockMovementType.Withdrawal && ReversalOfMovementId is null && !IsReversed;

	public void MarkReversed() => IsReversed = true;
}
