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

public enum ItemComplianceStatus
{
	Unknown = 0,
	Compliant = 1,
	NonCompliant = 2,
	NotApplicable = 3
}

public sealed class ItemMasterDataInput
{
	public string? Gtin { get; set; }
	public ItemType ItemType { get; set; } = ItemType.StockItem;
	public ItemLifecycleStatus LifecycleStatus { get; set; } = ItemLifecycleStatus.Active;
	public string? Revision { get; set; }
	public string? Model { get; set; }
	public string? ProductFamily { get; set; }
	public string? CountryOfOrigin { get; set; }
	public string? CustomsTariffNumber { get; set; }
	public string? Eccn { get; set; }
	public ItemTrackingMode TrackingMode { get; set; } = ItemTrackingMode.None;
	public decimal? NetWeightKg { get; set; }
	public decimal? GrossWeightKg { get; set; }
	public decimal? LengthMm { get; set; }
	public decimal? WidthMm { get; set; }
	public decimal? HeightMm { get; set; }
	public bool IsDangerousGoods { get; set; }
	public string? UnNumber { get; set; }
	public bool ContainsBattery { get; set; }
	public ItemComplianceStatus RohsStatus { get; set; } = ItemComplianceStatus.Unknown;
	public ItemComplianceStatus ReachStatus { get; set; } = ItemComplianceStatus.Unknown;
	public DateTime? IntroductionDate { get; set; }
	public DateTime? EndOfLifeDate { get; set; }
	public DateTime? LastBuyDate { get; set; }
	public DateTime? EndOfSupportDate { get; set; }
	public long? ReplacementItemId { get; set; }
	public string? Notes { get; set; }
}
