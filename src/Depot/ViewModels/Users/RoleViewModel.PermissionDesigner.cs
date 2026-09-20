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
	private RoleEffectivePermissionUserProjection? _selectedEffectivePermissionUser;

	public ObservableCollection<Role> DesignerRoles { get; } = [];
	public ObservableCollection<RolePermissionMatrixRow> PermissionMatrixRows { get; } = [];
	public ObservableCollection<RolePermissionGroupSelectionViewModel> PermissionGroups { get; } = [];
	public ObservableCollection<RolePermissionAdvisory> PermissionAdvisories { get; } = [];
	public ObservableCollection<RoleEffectivePermissionUserProjection> EffectivePermissionUsers { get; } = [];
	public ObservableCollection<RolePermissionDescriptor> SelectedEffectivePermissions { get; } = [];

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

	public RoleEffectivePermissionUserProjection? SelectedEffectivePermissionUser
	{
		get => _selectedEffectivePermissionUser;
		set
		{
			if (ReferenceEquals(_selectedEffectivePermissionUser, value)) return;
			_selectedEffectivePermissionUser = value;
			OnPropertyChanged();
			RefreshSelectedEffectivePermissions();
		}
	}

	public string EffectivePermissionSummary => SelectedEffectivePermissionUser is null
		? "Select an affected user to inspect the effective union of all active assigned roles."
		: $"{SelectedEffectivePermissionUser.DisplayName}: {SelectedEffectivePermissionUser.EffectivePermissionCount} effective permissions from {SelectedEffectivePermissionUser.AssignedRoles.Count} assigned role(s).";

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
			{
				Apply(role);
				await LoadEffectivePermissionImpactAsync(role.Id, token);
			}
		}
		else
		{
			EffectivePermissionUsers.Clear();
			SelectedEffectivePermissionUser = null;
		}
		RefreshPermissionDesignerSelection();
	}

	private async Task SelectMatrixRoleAsync(Role role, CancellationToken token)
	{
		var detailed = await _service.GetByIdAsync(role.Id, token) ?? throw new InvalidOperationException("The role was not found.");
		_selectedRole = Roles.FirstOrDefault(value => value.Id == role.Id) ?? role;
		OnPropertyChanged(nameof(SelectedRole));
		Apply(detailed);
		await LoadEffectivePermissionImpactAsync(detailed.Id, token);
	}

	private async Task LoadEffectivePermissionImpactAsync(long roleId, CancellationToken token)
	{
		var selectedUserId = SelectedEffectivePermissionUser?.UserId;
		var users = await _service.GetEffectivePermissionUsersAsync(roleId, token);
		CollectionSynchronizer.Replace(EffectivePermissionUsers, users);
		SelectedEffectivePermissionUser = selectedUserId.HasValue
			? EffectivePermissionUsers.FirstOrDefault(value => value.UserId == selectedUserId.Value) ?? EffectivePermissionUsers.FirstOrDefault()
			: EffectivePermissionUsers.FirstOrDefault();
		OnPropertyChanged(nameof(EffectivePermissionSummary));
	}

	private void RefreshSelectedEffectivePermissions()
	{
		var definitions = PermissionCatalog.Definitions.ToDictionary(value => value.Permission);
		var descriptors = SelectedEffectivePermissionUser?.EffectivePermissions
			.Where(definitions.ContainsKey)
			.Select(permission => RolePermissionDesignerProjector.Describe(definitions[permission]))
			.OrderBy(value => value.Group)
			.ThenBy(value => value.Module, StringComparer.OrdinalIgnoreCase)
			.ThenBy(value => value.Action, StringComparer.OrdinalIgnoreCase)
			.ToArray() ?? [];
		CollectionSynchronizer.Replace(SelectedEffectivePermissions, descriptors);
		OnPropertyChanged(nameof(EffectivePermissionSummary));
	}

	private void ClearEffectivePermissionImpact()
	{
		EffectivePermissionUsers.Clear();
		SelectedEffectivePermissionUser = null;
		SelectedEffectivePermissions.Clear();
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
