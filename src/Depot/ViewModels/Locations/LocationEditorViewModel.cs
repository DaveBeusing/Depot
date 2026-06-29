// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels.Locations;

/// <summary>
/// Represents the editor state for a location.
/// </summary>
public sealed class LocationEditorViewModel
	: BaseViewModel
{
	private long _id;
	private string _name = string.Empty;
	private string? _description;

	public long Id
	{
		get => _id;

		set
		{
			_id = value;

			OnPropertyChanged();
			OnPropertyChanged(nameof(IsExistingLocation));
			OnPropertyChanged(nameof(EditorTitle));
		}
	}

	public string Name
	{
		get => _name;

		set
		{
			_name = value;

			OnPropertyChanged();
		}
	}

	public string? Description
	{
		get => _description;

		set
		{
			_description = value;

			OnPropertyChanged();
		}
	}

	public bool IsExistingLocation =>
		Id != 0;

	public string EditorTitle =>
		IsExistingLocation
			? "Edit Location"
			: "New Location";

	public void Clear()
	{
		Id = 0;
		Name = string.Empty;
		Description = null;
	}
}