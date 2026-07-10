// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels.Users;

/// <summary>
/// Represents the editor state for a user.
/// </summary>
public sealed class UserEditorViewModel : BaseViewModel
{
	private long _id;
	private string _userName = string.Empty;
	private string _displayName = string.Empty;
	private bool _isAdministrator;
	private bool _isActive = true;

	public long Id
	{
		get => _id;
		set
		{
			_id = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsExistingUser));
			OnPropertyChanged(nameof(EditorTitle));
			OnPropertyChanged(nameof(CanEditUserName));
		}
	}

	public string UserName
	{
		get => _userName;
		set
		{
			_userName = value;
			OnPropertyChanged();
		}
	}

	public string DisplayName
	{
		get => _displayName;
		set
		{
			_displayName = value;
			OnPropertyChanged();
		}
	}

	public bool IsAdministrator
	{
		get => _isAdministrator;
		set
		{
			_isAdministrator = value;
			OnPropertyChanged();
		}
	}

	public bool IsActive
	{
		get => _isActive;
		set
		{
			_isActive = value;
			OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
	        OnPropertyChanged(nameof(ActivationButtonText));
			OnPropertyChanged(nameof(IsInactive));
		}
	}

	public bool IsInactive => !IsActive;
	public bool IsExistingUser => Id != 0;
	public bool CanEditUserName => !IsExistingUser;
	public string EditorTitle => IsExistingUser ? "Edit User" : "New User";
    public string Status => IsActive ? "Active" : "Inactive";
    public string ActivationButtonText => IsActive ? "Deactivate" : "Activate";

	public void Clear()
	{
		Id = 0;
		UserName = string.Empty;
		DisplayName = string.Empty;
		IsAdministrator = false;
		IsActive = true;
	}
}