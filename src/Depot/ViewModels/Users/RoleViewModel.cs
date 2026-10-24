// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Users;

public sealed partial class RoleViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly IRoleEditorService _service;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly LatestRequest _editorRequests = new();
	private readonly LatestRequest _listRequests = new();
	private readonly LatestRequest _designerRequests = new();
	private Role? _selectedRole;
	private string _searchText = string.Empty;
	private long _id;
	private long _version = 1;
	private string _code = string.Empty;
	private string _name = string.Empty;
	private string? _description;
	private bool _isSystem;
	private bool _isActive = true;
	private bool _editorReady = true;
	private bool _loadingRole;
	private bool _mutationInProgress;
	private bool _synchronizingSelection;
	private bool _disposed;
	private long _operationGeneration;

	public RoleViewModel(IRoleEditorService service)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		foreach (var definition in service.Permissions) Permissions.Add(new PermissionSelectionViewModel(definition));
		NewCommand = new RelayCommand(New, () => !_disposed);
		SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit);
		ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => Id != 0 && CanEdit);
		InitializePermissionDesigner();
	}

	public ObservableCollection<Role> Roles { get; } = new();
	public ObservableCollection<PermissionSelectionViewModel> Permissions { get; } = new();
	public RelayCommand NewCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand ToggleActiveCommand { get; }
	public string SearchText { get => _searchText; set { if (_disposed || _searchText == value) return; _searchText = value; OnPropertyChanged(); _ = _searchDebouncer.DebounceAsync(LoadAsync); } }
	public Role? SelectedRole
	{
		get => _selectedRole;
		set
		{
			if (_disposed || _synchronizingSelection || ReferenceEquals(_selectedRole, value)) return;
			_ = SelectRoleAsync(value);
		}
	}
	public long Id { get => _id; private set { _id = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditorTitle)); RaiseCommands(); } }
	public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }
	public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
	public string? Description { get => _description; set { _description = value; OnPropertyChanged(); } }
	public bool IsSystem { get => _isSystem; private set { _isSystem = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } }
	public bool IsActive { get => _isActive; private set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActivationButtonText)); } }
	public bool CanEdit => !_disposed && _editorReady && !_mutationInProgress && !IsSystem
		&& (SelectedRole is null ? Id == 0 : SelectedRole.Id == Id);
	public string EditorTitle => _loadingRole ? "Loading Role" : !_editorReady && SelectedRole is not null ? "Role unavailable" : Id == 0 ? "New Role" : IsSystem ? "System Role" : "Edit Role";
	public string ActivationButtonText => IsActive ? "Deactivate" : "Activate";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		if (_disposed) return;
		var request = _listRequests.Begin(cancellationToken);
		var operation = StartOperation("Loading roles");
		try
		{
			var page = await _service.SearchAsync(SearchText, 1, PageSize, request.Token);
			if (!CanApply(request)) return;
			var selected = SelectedRole;
			var visible = page.Items.Select(role => selected is not null && role.Id == selected.Id ? selected : role).ToArray();
			_synchronizingSelection = true;
			try { CollectionSynchronizer.Replace(Roles, visible); }
			finally { _synchronizingSelection = false; }
			OnPropertyChanged(nameof(SelectedRole));
			await RefreshPermissionDesignerAsync(request.Token);
			if (CanApply(request) && IsCurrentOperation(operation)) CompleteOperation(Roles.Count == 0, $"{page.TotalCount:N0} roles");
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception exception)
		{
			if (CanApply(request) && IsCurrentOperation(operation)) FailOperation(exception, "Roles could not be loaded");
		}
	}

	public async Task SelectRoleAsync(Role? role, CancellationToken cancellationToken = default)
	{
		if (_disposed) return;
		var request = _editorRequests.Begin(cancellationToken);
		_editorReady = false;
		_loadingRole = role is not null;
		_selectedRole = role;
		OnPropertyChanged(nameof(SelectedRole));
		ResetEditor();
		RaiseEditorAvailability();
		var operation = StartOperation(role is null ? string.Empty : "Loading role");
		if (role is null)
		{
			_editorReady = true;
			RaiseEditorAvailability();
			CompleteOperation();
			return;
		}

		try
		{
			var detailed = await _service.GetByIdAsync(role.Id, request.Token);
			if (!IsCurrentSelection(request, role.Id)) return;
			if (detailed is null || detailed.Id != role.Id) throw new InvalidOperationException("The selected role was not found.");
			ReplaceInList(detailed, insertIfMissing: false);
			_selectedRole = detailed;
			OnPropertyChanged(nameof(SelectedRole));
			Apply(detailed);
			_editorReady = true;
			_loadingRole = false;
			RaiseEditorAvailability();
			await LoadEffectivePermissionImpactAsync(detailed.Id, request);
			if (IsCurrentSelection(request, role.Id) && IsCurrentOperation(operation)) CompleteOperation();
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception exception)
		{
			if (IsCurrentSelection(request, role.Id) && IsCurrentOperation(operation)) FailOperation(exception, "Role details could not be loaded");
		}
		finally
		{
			if (request.IsCurrent && !_disposed)
			{
				_loadingRole = false;
				if (request.Token.IsCancellationRequested && IsCurrentOperation(operation)) CompleteOperation(false, "Role loading cancelled");
				RaiseEditorAvailability();
			}
		}
	}

	private void New()
	{
		if (!_disposed) _ = SelectRoleAsync(null);
	}

	private void ResetEditor()
	{
		Id = 0; _version = 1; Code = string.Empty; Name = string.Empty; Description = null; IsSystem = false; IsActive = true;
		foreach (var permission in Permissions) permission.IsSelected = false;
		ClearEffectivePermissionImpact();
		RefreshPermissionDesignerSelection();
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		if (!CanEdit) return;
		var value = new Role { Id = Id, Code = Code, Name = Name, Description = Description, IsActive = IsActive, IsSystem = IsSystem, Version = _version, Permissions = Permissions.Where(permission => permission.IsSelected).Select(permission => permission.Permission).ToArray() };
		await MutateAsync(value.Id, token => _service.SaveAsync(value, token), "Saving role", "Role saved", "Role could not be saved", cancellationToken);
	}

	private async Task ToggleActiveAsync(CancellationToken cancellationToken)
	{
		if (!CanEdit || Id == 0) return;
		var id = Id;
		var version = _version;
		var isActive = !IsActive;
		await MutateAsync(id, token => _service.SetActiveAsync(id, version, isActive, token), isActive ? "Activating role" : "Deactivating role", "Role updated", "Role could not be updated", cancellationToken);
	}

	private async Task MutateAsync(long sourceId, Func<CancellationToken, Task<Role>> mutation, string pending, string success, string failure, CancellationToken token)
	{
		var request = _editorRequests.Begin(token);
		var operation = StartOperation(pending);
		_mutationInProgress = true;
		RaiseEditorAvailability();
		try
		{
			var saved = await mutation(token);
			if (_disposed) return;
			if (!IsCurrentSelection(request, sourceId))
			{
				if (SelectedRole?.Id != saved.Id) ReplaceInList(saved);
				return;
			}
			ReplaceInList(saved);
			_selectedRole = saved;
			OnPropertyChanged(nameof(SelectedRole));
			Apply(saved);
			await RefreshPermissionDesignerAsync(request.Token);
			if (!IsCurrentSelection(request, saved.Id)) return;
			await LoadEffectivePermissionImpactAsync(saved.Id, request);
			if (IsCurrentSelection(request, saved.Id) && IsCurrentOperation(operation)) CompleteOperation(false, success);
		}
		catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
		catch (Exception exception)
		{
			if (CanApply(request) && IsCurrentOperation(operation)) FailOperation(exception, failure);
		}
		finally
		{
			_mutationInProgress = false;
			if (!_disposed) RaiseEditorAvailability();
		}
	}

	private void Apply(Role role)
	{
		Id = role.Id; _version = role.Version; Code = role.Code; Name = role.Name; Description = role.Description; IsSystem = role.IsSystem; IsActive = role.IsActive;
		var selected = role.Permissions.ToHashSet();
		foreach (var permission in Permissions) permission.IsSelected = selected.Contains(permission.Permission);
		RefreshPermissionDesignerSelection();
	}

	private void ReplaceInList(Role role, bool insertIfMissing = true)
	{
		_synchronizingSelection = true;
		try
		{
			var existing = Roles.FirstOrDefault(value => value.Id == role.Id);
			if (existing is not null) Roles[Roles.IndexOf(existing)] = role;
			else if (insertIfMissing) Roles.Insert(0, role);
		}
		finally { _synchronizingSelection = false; }
	}

	private bool CanApply(LatestRequestLease request) => !_disposed && request.IsCurrent && !request.Token.IsCancellationRequested;
	private bool IsCurrentSelection(LatestRequestLease request, long roleId) => CanApply(request) && (SelectedRole?.Id ?? 0) == roleId;
	private long StartOperation(string status) { var generation = ++_operationGeneration; BeginOperation(status); return generation; }
	private bool IsCurrentOperation(long generation) => !_disposed && generation == _operationGeneration;
	private void RaiseEditorAvailability() { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(EditorTitle)); RaiseCommands(); }
	private void RaiseCommands() { SaveCommand.RaiseCanExecuteChanged(); ToggleActiveCommand.RaiseCanExecuteChanged(); }
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_editorRequests.Dispose();
		_listRequests.Dispose();
		_designerRequests.Dispose();
		DisposePermissionDesigner();
		_searchDebouncer.Dispose();
		SaveCommand.Dispose();
		ToggleActiveCommand.Dispose();
		NewCommand.RaiseCanExecuteChanged();
		RaiseEditorAvailability();
	}
}
