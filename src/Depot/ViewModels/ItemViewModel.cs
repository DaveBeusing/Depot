// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ItemViewModel : BaseViewModel
{
	public ItemViewModel(Item item)
	{
		Id = item.Id;
		PartNumber = item.PartNumber;
		Description = item.Description;
		Manufacturer = item.Manufacturer;
		Category = item.Category;
		UnitOfMeasure = item.UnitOfMeasure;
		Packaging = item.Packaging;
		ManufacturerId = item.ManufacturerId;
		CategoryId = item.CategoryId;
		UnitOfMeasureId = item.UnitOfMeasureId;
		PackagingId = item.PackagingId;
		Gtin = item.Gtin;
		ItemType = item.ItemType;
		LifecycleStatus = item.LifecycleStatus;
		Revision = item.Revision;
		Model = item.Model;
		ProductFamily = item.ProductFamily;
		CountryOfOrigin = item.CountryOfOrigin;
		CustomsTariffNumber = item.CustomsTariffNumber;
		Eccn = item.Eccn;
		TrackingMode = item.TrackingMode;
		NetWeightKg = item.NetWeightKg;
		GrossWeightKg = item.GrossWeightKg;
		LengthMm = item.LengthMm;
		WidthMm = item.WidthMm;
		HeightMm = item.HeightMm;
		IsDangerousGoods = item.IsDangerousGoods;
		UnNumber = item.UnNumber;
		ContainsBattery = item.ContainsBattery;
		RohsStatus = item.RohsStatus;
		ReachStatus = item.ReachStatus;
		IntroductionDate = item.IntroductionDate;
		EndOfLifeDate = item.EndOfLifeDate;
		LastBuyDate = item.LastBuyDate;
		EndOfSupportDate = item.EndOfSupportDate;
		ReplacementItemId = item.ReplacementItemId;
		Notes = item.Notes;
		IsActive = item.IsActive;
		Version = item.Version;
	}

	public long Id { get; }
	public string PartNumber { get; }
	public string Description { get; }
	public string? Manufacturer { get; }
	public string? Category { get; }
	public string? UnitOfMeasure { get; }
	public string? Packaging { get; }
	public long? ManufacturerId { get; }
	public long? CategoryId { get; }
	public long? UnitOfMeasureId { get; }
	public long? PackagingId { get; }
	public string? Gtin { get; }
	public ItemType ItemType { get; }
	public ItemLifecycleStatus LifecycleStatus { get; }
	public string? Revision { get; }
	public string? Model { get; }
	public string? ProductFamily { get; }
	public string? CountryOfOrigin { get; }
	public string? CustomsTariffNumber { get; }
	public string? Eccn { get; }
	public ItemTrackingMode TrackingMode { get; }
	public decimal? NetWeightKg { get; }
	public decimal? GrossWeightKg { get; }
	public decimal? LengthMm { get; }
	public decimal? WidthMm { get; }
	public decimal? HeightMm { get; }
	public bool IsDangerousGoods { get; }
	public string? UnNumber { get; }
	public bool ContainsBattery { get; }
	public ItemComplianceStatus RohsStatus { get; }
	public ItemComplianceStatus ReachStatus { get; }
	public DateTime? IntroductionDate { get; }
	public DateTime? EndOfLifeDate { get; }
	public DateTime? LastBuyDate { get; }
	public DateTime? EndOfSupportDate { get; }
	public long? ReplacementItemId { get; }
	public string? Notes { get; }
	public bool IsActive { get; }
	public long Version { get; }
}
