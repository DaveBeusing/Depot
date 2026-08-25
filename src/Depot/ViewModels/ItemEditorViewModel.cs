// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ItemEditorViewModel : BaseViewModel
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
	private string? _revision;
	private string? _model;
	private string? _productFamily;
	private string? _countryOfOrigin;
	private string? _customsTariffNumber;
	private string? _eccn;
	private ItemTrackingMode _trackingMode = ItemTrackingMode.None;
	private decimal? _netWeightKg;
	private decimal? _grossWeightKg;
	private decimal? _lengthMm;
	private decimal? _widthMm;
	private decimal? _heightMm;
	private bool _isDangerousGoods;
	private string? _unNumber;
	private bool _containsBattery;
	private ItemComplianceStatus _rohsStatus = ItemComplianceStatus.Unknown;
	private ItemComplianceStatus _reachStatus = ItemComplianceStatus.Unknown;
	private DateTime? _introductionDate;
	private DateTime? _endOfLifeDate;
	private DateTime? _lastBuyDate;
	private DateTime? _endOfSupportDate;
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
	public string? Revision { get => _revision; set { if (_revision == value) return; _revision = value; OnPropertyChanged(); } }
	public string? Model { get => _model; set { if (_model == value) return; _model = value; OnPropertyChanged(); } }
	public string? ProductFamily { get => _productFamily; set { if (_productFamily == value) return; _productFamily = value; OnPropertyChanged(); } }
	public string? CountryOfOrigin { get => _countryOfOrigin; set { if (_countryOfOrigin == value) return; _countryOfOrigin = value; OnPropertyChanged(); } }
	public string? CustomsTariffNumber { get => _customsTariffNumber; set { if (_customsTariffNumber == value) return; _customsTariffNumber = value; OnPropertyChanged(); } }
	public string? Eccn { get => _eccn; set { if (_eccn == value) return; _eccn = value; OnPropertyChanged(); } }
	public ItemTrackingMode TrackingMode { get => _trackingMode; set { if (_trackingMode == value) return; _trackingMode = value; OnPropertyChanged(); } }
	public decimal? NetWeightKg { get => _netWeightKg; set { if (_netWeightKg == value) return; _netWeightKg = value; OnPropertyChanged(); } }
	public decimal? GrossWeightKg { get => _grossWeightKg; set { if (_grossWeightKg == value) return; _grossWeightKg = value; OnPropertyChanged(); } }
	public decimal? LengthMm { get => _lengthMm; set { if (_lengthMm == value) return; _lengthMm = value; OnPropertyChanged(); } }
	public decimal? WidthMm { get => _widthMm; set { if (_widthMm == value) return; _widthMm = value; OnPropertyChanged(); } }
	public decimal? HeightMm { get => _heightMm; set { if (_heightMm == value) return; _heightMm = value; OnPropertyChanged(); } }
	public bool IsDangerousGoods { get => _isDangerousGoods; set { if (_isDangerousGoods == value) return; _isDangerousGoods = value; OnPropertyChanged(); } }
	public string? UnNumber { get => _unNumber; set { if (_unNumber == value) return; _unNumber = value; OnPropertyChanged(); } }
	public bool ContainsBattery { get => _containsBattery; set { if (_containsBattery == value) return; _containsBattery = value; OnPropertyChanged(); } }
	public ItemComplianceStatus RohsStatus { get => _rohsStatus; set { if (_rohsStatus == value) return; _rohsStatus = value; OnPropertyChanged(); } }
	public ItemComplianceStatus ReachStatus { get => _reachStatus; set { if (_reachStatus == value) return; _reachStatus = value; OnPropertyChanged(); } }
	public DateTime? IntroductionDate { get => _introductionDate; set { if (_introductionDate == value) return; _introductionDate = value; OnPropertyChanged(); } }
	public DateTime? EndOfLifeDate { get => _endOfLifeDate; set { if (_endOfLifeDate == value) return; _endOfLifeDate = value; OnPropertyChanged(); } }
	public DateTime? LastBuyDate { get => _lastBuyDate; set { if (_lastBuyDate == value) return; _lastBuyDate = value; OnPropertyChanged(); } }
	public DateTime? EndOfSupportDate { get => _endOfSupportDate; set { if (_endOfSupportDate == value) return; _endOfSupportDate = value; OnPropertyChanged(); } }
	public long? ReplacementItemId { get => _replacementItemId; set { if (_replacementItemId == value) return; _replacementItemId = value; OnPropertyChanged(); } }
	public string? Notes { get => _notes; set { if (_notes == value) return; _notes = value; OnPropertyChanged(); } }

	public ItemMasterDataInput ToMasterData() => new()
	{
		Gtin = Gtin,
		ItemType = ItemType,
		LifecycleStatus = LifecycleStatus,
		Revision = Revision,
		Model = Model,
		ProductFamily = ProductFamily,
		CountryOfOrigin = CountryOfOrigin,
		CustomsTariffNumber = CustomsTariffNumber,
		Eccn = Eccn,
		TrackingMode = TrackingMode,
		NetWeightKg = NetWeightKg,
		GrossWeightKg = GrossWeightKg,
		LengthMm = LengthMm,
		WidthMm = WidthMm,
		HeightMm = HeightMm,
		IsDangerousGoods = IsDangerousGoods,
		UnNumber = UnNumber,
		ContainsBattery = ContainsBattery,
		RohsStatus = RohsStatus,
		ReachStatus = ReachStatus,
		IntroductionDate = IntroductionDate,
		EndOfLifeDate = EndOfLifeDate,
		LastBuyDate = LastBuyDate,
		EndOfSupportDate = EndOfSupportDate,
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
		Revision = null;
		Model = null;
		ProductFamily = null;
		CountryOfOrigin = null;
		CustomsTariffNumber = null;
		Eccn = null;
		TrackingMode = ItemTrackingMode.None;
		NetWeightKg = null;
		GrossWeightKg = null;
		LengthMm = null;
		WidthMm = null;
		HeightMm = null;
		IsDangerousGoods = false;
		UnNumber = null;
		ContainsBattery = false;
		RohsStatus = ItemComplianceStatus.Unknown;
		ReachStatus = ItemComplianceStatus.Unknown;
		IntroductionDate = null;
		EndOfLifeDate = null;
		LastBuyDate = null;
		EndOfSupportDate = null;
		ReplacementItemId = null;
		Notes = null;
		Version = 1;
	}
}
