// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.ComponentModel;

using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels.Users;

public sealed record RolePermissionGroupSelectionViewModel(
	RolePermissionGroupKind Group,
	string Name,
	IReadOnlyList<PermissionSelectionViewModel> Permissions);

public sealed partial class RoleViewModel
{
	private RolePermissionGroupSelectionViewModel? _selectedPermissionGroup;
	private PermissionSelectionViewModel? _selectedDesignerPermission;
	private string _permissionDesignerStatus = "Select a role to inspect direct permissions.";

	public ObservableCollection<Role> DesignerRoles { get; } = [];
	public ObservableCollection<RolePermissionMatrixRow> PermissionMatrixRows { get; } = [];
	public ObservableCollection<RolePermissionGroupSelectionViewModel> PermissionGroups { get; } = [];
	public ObservableCollection<RolePermissionAdvisory> PermissionAdvisories { get; } = [];

	public AsyncRelayCommand<Role> SelectMatrixRoleCommand { get; private set; } = null!;

	public RolePermissionGroupSelectionViewModel? SelectedPermissionGroup
	{
		get => _selectedPermissionGroup;
		set
		{
			if (ReferenceEquals(_selectedPermissionGroup, value)) return;
			_selectedPermissionGroup = value;
			OnPropertyChanged();
			SelectedDesignerPermission = value?.Permissions.FirstOrDefault();
		}
	}

	public PermissionSelectionViewModel? SelectedDesignerPermission
	{
		get => _selectedDesignerPermission;
		set
		{
			if (ReferenceEquals(_selectedDesignerPermission, value)) return;
			_selectedDesignerPermission = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedPermissionTitle));
			OnPropertyChanged(nameof(SelectedPermissionDescription));
			OnPropertyChanged(nameof(SelectedPermissionWorkspace));
			OnPropertyChanged(nameof(SelectedPermissionCode));
		}
	}

	public string SelectedPermissionTitle => SelectedDesignerPermission?.Name ?? "Select a permission";
	public string SelectedPermissionDescription => SelectedDesignerPermission?.Description ?? "Choose a permission to inspect its capability meaning.";
	public string SelectedPermissionWorkspace => SelectedDesignerPermission?.AffectedWorkspace ?? "—";
	public string SelectedPermissionCode => SelectedDesignerPermission?.Code ?? "—";
	public string PermissionDesignerStatus
	{
		get => _permissionDesignerStatus;
		private set
		{
			if (_permissionDesignerStatus == value) return;
			_permissionDesignerStatus = value;
			OnPropertyChanged();
		}
	}

	public string RoleProtectionSummary => Id == 0
		? "New custom role"
		: IsSystem
			? "Protected system role · read-only"
			: "Custom role · editable through RoleService";

	private void InitializePermissionDesigner()
	{
		foreach (var group in Enum.GetValues<RolePermissionGroupKind>())
		{
			var selections = Permissions.Where(value => value.Group == group).ToArray();
			PermissionGroups.Add(new RolePermissionGroupSelectionViewModel(group, RolePermissionDesignerProjector.GroupName(group), selections));
		}
		SelectedPermissionGroup = PermissionGroups.FirstOrDefault();
		foreach (var permission in Permissions)
			permission.PropertyChanged += OnDesignerPermissionChanged;
		SelectMatrixRoleCommand = new AsyncRelayCommand<Role>(SelectMatrixRoleAsync);
	}

	private async Task RefreshPermissionDesignerAsync(CancellationToken token)
	{
		var selectedRoleId = Id != 0 ? Id : SelectedRole?.Id;
		var roles = await _service.ListDesignerRolesAsync(token);
		CollectionSynchronizer.Replace(DesignerRoles, roles);
		var projection = RolePermissionDesignerProjector.Project(DesignerRoles, _service.Permissions);
		CollectionSynchronizer.Replace(PermissionMatrixRows, projection.MatrixRows);
		if (selectedRoleId is > 0)
		{
			var role = DesignerRoles.FirstOrDefault(value => value.Id == selectedRoleId.Value);
			if (role is not null)
				Apply(role);
		}
		RefreshPermissionDesignerSelection();
	}

	private async Task SelectMatrixRoleAsync(Role role, CancellationToken token)
	{
		var detailed = await _service.GetByIdAsync(role.Id, token) ?? throw new InvalidOperationException("The role was not found.");
		_selectedRole = Roles.FirstOrDefault(value => value.Id == role.Id) ?? role;
		OnPropertyChanged(nameof(SelectedRole));
		Apply(detailed);
	}

	private void RefreshPermissionDesignerSelection()
	{
		var selected = Permissions.Where(value => value.IsSelected).Select(value => value.Permission).ToArray();
		CollectionSynchronizer.Replace(PermissionAdvisories, RolePermissionDesignerProjector.Advisories(selected));
		PermissionDesignerStatus = Id == 0
			? $"{selected.Length} direct permission(s) selected for the new custom role."
			: $"{selected.Length} direct permission(s) on {Name}. Matrix values represent persisted direct role permissions.";
		OnPropertyChanged(nameof(RoleProtectionSummary));
	}

	private void OnDesignerPermissionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(PermissionSelectionViewModel.IsSelected))
			RefreshPermissionDesignerSelection();
	}

	private void DisposePermissionDesigner()
	{
		foreach (var permission in Permissions)
			permission.PropertyChanged -= OnDesignerPermissionChanged;
		SelectMatrixRoleCommand.Dispose();
	}
}
