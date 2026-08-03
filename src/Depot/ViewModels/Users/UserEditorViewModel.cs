// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;

namespace Depot.ViewModels.Users;

/// <summary>
/// Represents the editor state for a user.
/// </summary>
public sealed class UserEditorViewModel : BaseViewModel
{
	private long _id;
	private string _email = string.Empty;
	private string _displayName = string.Empty;
	private string _password = string.Empty;
	private bool _isActive = true;
	private long _version = 1;
	private string _effectivePermissions = "Calculated after saving.";

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
			OnPropertyChanged(nameof(IsExistingUser));
			OnPropertyChanged(nameof(EditorTitle));
			OnPropertyChanged(nameof(PasswordHint));
		}
	}

	public string Email
	{
		get => _email;
		set
		{
			_email = value;
			OnPropertyChanged();
		}
	}

	public string Password
	{
		get => _password;
		set
		{
			_password = value;
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

	public ObservableCollection<RoleSelectionViewModel> Roles { get; } = new();
	public IReadOnlyCollection<long> SelectedRoleIds => Roles.Where(role => role.IsSelected).Select(role => role.Id).ToArray();
	public string EffectivePermissions { get => _effectivePermissions; set { _effectivePermissions = value; OnPropertyChanged(); } }

	public void SetRoles(IEnumerable<Role> availableRoles, IEnumerable<long> selectedRoleIds)
	{
		var selected = selectedRoleIds.ToHashSet();
		Roles.Clear();
		foreach (var role in availableRoles) Roles.Add(new RoleSelectionViewModel(role, selected.Contains(role.Id)));
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
	public string EditorTitle => IsExistingUser ? "Edit User" : "New User";
	public string PasswordHint => IsExistingUser
		? "Leave blank to keep it; new passwords require 8+ characters, uppercase, lowercase, and a number."
		: "8+ characters with uppercase, lowercase, and a number.";
    public string Status => IsActive ? "Active" : "Inactive";
    public string ActivationButtonText => IsActive ? "Deactivate" : "Activate";

	public void Clear()
	{
		Id = 0;
		Email = string.Empty;
		DisplayName = string.Empty;
		Password = string.Empty;
		foreach (var role in Roles) role.IsSelected = string.Equals(role.Name, "User", StringComparison.Ordinal);
		IsActive = true;
		Version = 1;
		EffectivePermissions = "Calculated after saving.";
	}
}
