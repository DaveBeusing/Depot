// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class Item
{
	public long Id { get; set; }

	public string PartNumber { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string? Manufacturer { get; set; }

	public string? Category { get; set; }

	public string? UnitOfMeasure { get; set; }

	public string? Packaging { get; set; }

	public long? ManufacturerId { get; set; }

	public long? CategoryId { get; set; }

	public long? UnitOfMeasureId { get; set; }

	public long? PackagingId { get; set; }

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

	public bool IsActive { get; set; } = true;

	public long Version { get; set; } = 1;
}
