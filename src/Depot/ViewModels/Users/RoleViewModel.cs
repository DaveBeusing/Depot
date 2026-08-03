// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Users;

public sealed class RoleViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly RoleService _service;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private Role? _selectedRole;
	private string _searchText = string.Empty;
	private long _id;
	private long _version = 1;
	private string _code = string.Empty;
	private string _name = string.Empty;
	private string? _description;
	private bool _isSystem;
	private bool _isActive = true;

	public RoleViewModel(RoleService service)
	{
		_service = service;
		foreach (var definition in service.Permissions) Permissions.Add(new PermissionSelectionViewModel(definition));
		NewCommand = new RelayCommand(New);
		SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsSystem);
		ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => Id != 0 && !IsSystem);
	}

	public ObservableCollection<Role> Roles { get; } = new();
	public ObservableCollection<PermissionSelectionViewModel> Permissions { get; } = new();
	public RelayCommand NewCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand ToggleActiveCommand { get; }
	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); _ = _searchDebouncer.DebounceAsync(LoadAsync); } }
	public Role? SelectedRole { get => _selectedRole; set { if (_selectedRole == value) return; _selectedRole = value; OnPropertyChanged(); _ = LoadSelectedAsync(); } }
	public long Id { get => _id; private set { _id = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorTitle)); RaiseCommands(); } }
	public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }
	public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
	public string? Description { get => _description; set { _description = value; OnPropertyChanged(); } }
	public bool IsSystem { get => _isSystem; private set { _isSystem = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } }
	public bool IsActive { get => _isActive; private set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActivationButtonText)); } }
	public bool CanEdit => !IsSystem;
	public string EditorTitle => Id == 0 ? "New Role" : IsSystem ? "System Role" : "Edit Role";
	public string ActivationButtonText => IsActive ? "Deactivate" : "Activate";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading roles");
		try
		{
			var page = await _service.SearchAsync(SearchText, 1, PageSize, cancellationToken);
			CollectionSynchronizer.Replace(Roles, page.Items);
			CompleteOperation(Roles.Count == 0, $"{page.TotalCount:N0} roles");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Roles could not be loaded"); }
	}

	private async Task LoadSelectedAsync()
	{
		if (SelectedRole is null) return;
		try
		{
			var role = await _service.GetByIdAsync(SelectedRole.Id, CancellationToken.None) ?? throw new InvalidOperationException("The role was not found.");
			Apply(role);
		}
		catch (Exception exception) { FailOperation(exception, "Role details could not be loaded"); }
	}

	private void New()
	{
		SelectedRole = null; Id = 0; _version = 1; Code = string.Empty; Name = string.Empty; Description = null; IsSystem = false; IsActive = true;
		foreach (var permission in Permissions) permission.IsSelected = false;
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving role");
		try
		{
			var saved = await _service.SaveAsync(new Role { Id = Id, Code = Code, Name = Name, Description = Description, IsActive = IsActive, IsSystem = IsSystem, Version = _version, Permissions = Permissions.Where(permission => permission.IsSelected).Select(permission => permission.Permission).ToArray() }, cancellationToken);
			Replace(saved); Apply(saved); CompleteOperation(false, "Role saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Role could not be saved"); }
	}

	private async Task ToggleActiveAsync(CancellationToken cancellationToken)
	{
		BeginOperation(IsActive ? "Deactivating role" : "Activating role");
		try { var saved = await _service.SetActiveAsync(Id, _version, !IsActive, cancellationToken); Replace(saved); Apply(saved); CompleteOperation(false, "Role updated"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Role could not be updated"); }
	}

	private void Apply(Role role)
	{
		Id = role.Id; _version = role.Version; Code = role.Code; Name = role.Name; Description = role.Description; IsSystem = role.IsSystem; IsActive = role.IsActive;
		var selected = role.Permissions.ToHashSet();
		foreach (var permission in Permissions) permission.IsSelected = selected.Contains(permission.Permission);
	}

	private void Replace(Role role)
	{
		var existing = Roles.FirstOrDefault(value => value.Id == role.Id);
		if (existing is null) Roles.Insert(0, role); else Roles[Roles.IndexOf(existing)] = role;
		SelectedRole = role;
	}

	private void RaiseCommands() { SaveCommand.RaiseCanExecuteChanged(); ToggleActiveCommand.RaiseCanExecuteChanged(); }
	public void Dispose() { _searchDebouncer.Dispose(); SaveCommand.Dispose(); ToggleActiveCommand.Dispose(); }
}
