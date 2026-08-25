// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ItemViewModel
	: BaseViewModel
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
		CountryOfOrigin = item.CountryOfOrigin;
		CustomsTariffNumber = item.CustomsTariffNumber;
		TrackingMode = item.TrackingMode;
		NetWeight = item.NetWeight;
		Length = item.Length;
		Width = item.Width;
		Height = item.Height;
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
	public string? CountryOfOrigin { get; }
	public string? CustomsTariffNumber { get; }
	public ItemTrackingMode TrackingMode { get; }
	public decimal? NetWeight { get; }
	public decimal? Length { get; }
	public decimal? Width { get; }
	public decimal? Height { get; }
	public long? ReplacementItemId { get; }
	public string? Notes { get; }
	public bool IsActive { get; }
	public long Version { get; }
}
