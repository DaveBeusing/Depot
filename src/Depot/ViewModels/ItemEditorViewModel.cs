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
	private string? _manufacturer;
	private string? _category;
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

	public string? Manufacturer
	{
		get => _manufacturer;
		set
		{
			_manufacturer = value;
			OnPropertyChanged();
		}
	}

	public string? Category
	{
		get => _category;
		set
		{
			_category = value;
			OnPropertyChanged();
		}
	}

	public void Load(
		Item item)
	{
		Id = item.Id;
		PartNumber = item.PartNumber;
		Description = item.Description;
		Manufacturer = item.Manufacturer;
		Category = item.Category;
		Version = item.Version;
	}

	public void Clear()
	{
		Id = 0;
		PartNumber = string.Empty;
		Description = string.Empty;
		Manufacturer = null;
		Category = null;
		Version = 1;
	}
}
