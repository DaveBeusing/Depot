// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels.Purposes;

/// <summary>
/// Represents the editor state for a purpose.
/// </summary>
public sealed class PurposeEditorViewModel
	: BaseViewModel
{
	private long _id;
	private string _name = string.Empty;
	private string? _description;
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
			OnPropertyChanged(nameof(IsExistingPurpose));
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

	public bool IsExistingPurpose =>
		Id != 0;

	public string EditorTitle =>
		IsExistingPurpose
			? "Edit Purpose"
			: "New Purpose";

	public void Clear()
	{
		Id = 0;
		Name = string.Empty;
		Description = null;
		Version = 1;
	}
}
