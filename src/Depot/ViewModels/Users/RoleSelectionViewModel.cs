// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels.Users;

public sealed class RoleSelectionViewModel : BaseViewModel
{
	private bool _isSelected;

	public RoleSelectionViewModel(Role role, bool isSelected)
	{
		Id = role.Id;
		Name = role.Name;
		Description = role.Description;
		IsSystem = role.IsSystem;
		_isSelected = isSelected;
	}

	public long Id { get; }
	public string Name { get; }
	public string? Description { get; }
	public bool IsSystem { get; }
	public bool IsSelected
	{
		get => _isSelected;
		set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
	}
}
