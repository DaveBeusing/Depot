// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels.Users;

public sealed class PermissionSelectionViewModel : BaseViewModel
{
	private bool _isSelected;

	public PermissionSelectionViewModel(PermissionDefinition definition)
	{
		Permission = definition.Permission;
		Code = definition.Code;
		Name = definition.Name;
		var descriptor = RolePermissionDesignerProjector.Describe(definition);
		Group = descriptor.Group;
		GroupName = descriptor.GroupName;
		AffectedWorkspace = descriptor.AffectedWorkspace;
		Description = descriptor.Description;
		IsSensitive = descriptor.IsSensitive;
	}

	public ApplicationPermission Permission { get; }
	public string Code { get; }
	public string Name { get; }
	public RolePermissionGroupKind Group { get; }
	public string GroupName { get; }
	public string AffectedWorkspace { get; }
	public string Description { get; }
	public bool IsSensitive { get; }
	public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); } }
}
