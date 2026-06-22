// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class ItemEditorViewModel
	: BaseViewModel
{
	private string _partNumber = string.Empty;
	private string _description = string.Empty;
	private string? _manufacturer;
	private string? _category;

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

	public void Clear()
	{
		PartNumber = string.Empty;
		Description = string.Empty;
		Manufacturer = null;
		Category = null;
	}

}