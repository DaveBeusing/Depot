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
	private long _version = 1;

	public long Version
	{
		get => _version;
		set => _version = value;
	}

	public long Id
	{
		get => _id;
		set
		{
			_id = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsExistingItem));
			OnPropertyChanged(nameof(CanEditPartNumber));
			OnPropertyChanged(nameof(SaveButtonText));
			OnPropertyChanged(nameof(EditorTitle));
		}
	}

	public bool IsExistingItem =>
		Id > 0;

	public bool CanEditPartNumber =>
		!IsExistingItem;

	public string SaveButtonText =>
		IsExistingItem ? "Update" : "Create";

	public string EditorTitle =>
		IsExistingItem ? "Edit Item" : "New Item";

	public string PartNumber
	{
		get => _partNumber;
		set
		{
			_partNumber = value;
			OnPropertyChanged();
		}
	}

	public string Description
	{
		get => _description;
		set
		{
			_description = value;
			OnPropertyChanged();
		}
	}

	public ItemReferenceData? Manufacturer
	{
		get => _manufacturer;
		set
		{
			_manufacturer = value;
			OnPropertyChanged();
		}
	}

	public ItemReferenceData? Category
	{
		get => _category;
		set
		{
			_category = value;
			OnPropertyChanged();
		}
	}

	public ItemReferenceData? UnitOfMeasure { get => _unitOfMeasure; set { if (_unitOfMeasure == value) return; _unitOfMeasure = value; OnPropertyChanged(); } }
	public ItemReferenceData? Packaging { get => _packaging; set { if (_packaging == value) return; _packaging = value; OnPropertyChanged(); } }

	public void Load(
		Item item)
	{
		Id = item.Id;
		PartNumber = item.PartNumber;
		Description = item.Description;
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
		Version = 1;
	}
}
