// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ItemType
{
	StockItem = 1,
	NonStockItem = 2,
	Service = 3
}

public enum ItemLifecycleStatus
{
	New = 1,
	Active = 2,
	EndOfLife = 3,
	Discontinued = 4,
	Obsolete = 5
}

public enum ItemTrackingMode
{
	None = 0,
	SerialNumber = 1,
	Lot = 2
}

public sealed class ItemMasterDataInput
{
	public string? Gtin { get; set; }
	public ItemType ItemType { get; set; } = ItemType.StockItem;
	public ItemLifecycleStatus LifecycleStatus { get; set; } = ItemLifecycleStatus.Active;
	public string? CountryOfOrigin { get; set; }
	public string? CustomsTariffNumber { get; set; }
	public ItemTrackingMode TrackingMode { get; set; } = ItemTrackingMode.None;
	public decimal? NetWeight { get; set; }
	public decimal? Length { get; set; }
	public decimal? Width { get; set; }
	public decimal? Height { get; set; }
	public long? ReplacementItemId { get; set; }
	public string? Notes { get; set; }
}
