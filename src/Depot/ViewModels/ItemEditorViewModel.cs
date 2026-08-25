// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ItemEditorViewModel
	: BaseViewModel
{
	private long _id;
	private string _partNumber = string.Empty;
	private string _description = string.Empty;
	private ItemReferenceData? _manufacturer;
	private ItemReferenceData? _category;
	private ItemReferenceData? _unitOfMeasure;
	private ItemReferenceData? _packaging;
	private string? _gtin;
	private ItemType _itemType = ItemType.StockItem;
	private ItemLifecycleStatus _lifecycleStatus = ItemLifecycleStatus.Active;
	private string? _countryOfOrigin;
	private string? _customsTariffNumber;
	private ItemTrackingMode _trackingMode = ItemTrackingMode.None;
	private decimal? _netWeight;
	private decimal? _length;
	private decimal? _width;
	private decimal? _height;
	private long? _replacementItemId;
	private string? _notes;
	private long _version = 1;

	public long Version { get => _version; set => _version = value; }
	public long Id { get => _id; set { _id = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsExistingItem)); OnPropertyChanged(nameof(CanEditPartNumber)); OnPropertyChanged(nameof(SaveButtonText)); OnPropertyChanged(nameof(EditorTitle)); } }
	public bool IsExistingItem => Id > 0;
	public bool CanEditPartNumber => !IsExistingItem;
	public string SaveButtonText => IsExistingItem ? "Update" : "Create";
	public string EditorTitle => IsExistingItem ? "Edit Item" : "New Item";
	public string PartNumber { get => _partNumber; set { _partNumber = value; OnPropertyChanged(); } }
	public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
	public ItemReferenceData? Manufacturer { get => _manufacturer; set { _manufacturer = value; OnPropertyChanged(); } }
	public ItemReferenceData? Category { get => _category; set { _category = value; OnPropertyChanged(); } }
	public ItemReferenceData? UnitOfMeasure { get => _unitOfMeasure; set { if (_unitOfMeasure == value) return; _unitOfMeasure = value; OnPropertyChanged(); } }
	public ItemReferenceData? Packaging { get => _packaging; set { if (_packaging == value) return; _packaging = value; OnPropertyChanged(); } }
	public string? Gtin { get => _gtin; set { if (_gtin == value) return; _gtin = value; OnPropertyChanged(); } }
	public ItemType ItemType { get => _itemType; set { if (_itemType == value) return; _itemType = value; OnPropertyChanged(); } }
	public ItemLifecycleStatus LifecycleStatus { get => _lifecycleStatus; set { if (_lifecycleStatus == value) return; _lifecycleStatus = value; OnPropertyChanged(); } }
	public string? CountryOfOrigin { get => _countryOfOrigin; set { if (_countryOfOrigin == value) return; _countryOfOrigin = value; OnPropertyChanged(); } }
	public string? CustomsTariffNumber { get => _customsTariffNumber; set { if (_customsTariffNumber == value) return; _customsTariffNumber = value; OnPropertyChanged(); } }
	public ItemTrackingMode TrackingMode { get => _trackingMode; set { if (_trackingMode == value) return; _trackingMode = value; OnPropertyChanged(); } }
	public decimal? NetWeight { get => _netWeight; set { if (_netWeight == value) return; _netWeight = value; OnPropertyChanged(); } }
	public decimal? Length { get => _length; set { if (_length == value) return; _length = value; OnPropertyChanged(); } }
	public decimal? Width { get => _width; set { if (_width == value) return; _width = value; OnPropertyChanged(); } }
	public decimal? Height { get => _height; set { if (_height == value) return; _height = value; OnPropertyChanged(); } }
	public long? ReplacementItemId { get => _replacementItemId; set { if (_replacementItemId == value) return; _replacementItemId = value; OnPropertyChanged(); } }
	public string? Notes { get => _notes; set { if (_notes == value) return; _notes = value; OnPropertyChanged(); } }

	public ItemMasterDataInput ToMasterData() =>
		new()
		{
			Gtin = Gtin,
			ItemType = ItemType,
			LifecycleStatus = LifecycleStatus,
			CountryOfOrigin = CountryOfOrigin,
			CustomsTariffNumber = CustomsTariffNumber,
			TrackingMode = TrackingMode,
			NetWeight = NetWeight,
			Length = Length,
			Width = Width,
			Height = Height,
			ReplacementItemId = ReplacementItemId,
			Notes = Notes
		};

	public void Load(Item item)
	{
		Id = item.Id;
		PartNumber = item.PartNumber;
		Description = item.Description;
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
		Version = item.Version;
	}

	public void Clear()
	{
		Id = 0;
		PartNumber = string.Empty;
		Description = string.Empty;
		Manufacturer = null;
		Category = null;
		UnitOfMeasure = null;
		Packaging = null;
		Gtin = null;
		ItemType = global::Depot.Models.ItemType.StockItem;
		LifecycleStatus = ItemLifecycleStatus.Active;
		CountryOfOrigin = null;
		CustomsTariffNumber = null;
		TrackingMode = ItemTrackingMode.None;
		NetWeight = null;
		Length = null;
		Width = null;
		Height = null;
		ReplacementItemId = null;
		Notes = null;
		Version = 1;
	}
}
