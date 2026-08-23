// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.ViewModels.Shared;

namespace Depot.ViewModels.Users;

public sealed class UserViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly UserService _userService;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private string _searchText = string.Empty;
	private UserListItemViewModel? _selectedUser;
	private string? _errorMessage;
	private int _pageNumber = 1;
	private long _totalCount;
	private IReadOnlyList<Role> _availableRoles = [];
	private ActivationFilterOption _selectedActivationFilter = ActivationFilterOption.All[0];

	public UserViewModel(UserService userService)
	{
		_userService = userService;
		Editor = new UserEditorViewModel();
		NewUserCommand = new RelayCommand(NewUser);
		SaveUserCommand = new AsyncRelayCommand(SaveUserAsync);
		ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, CanToggleActive);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public UserEditorViewModel Editor { get; }
	public RelayCommand NewUserCommand { get; }
	public AsyncRelayCommand SaveUserCommand { get; }
	public AsyncRelayCommand ToggleActiveCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public ObservableCollection<UserListItemViewModel> Users { get; } = new();
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public IReadOnlyList<ActivationFilterOption> ActivationFilters => ActivationFilterOption.All;
	public bool HasUsers => Users.Count > 0;
	public string EditorStatus => Editor.IsExistingUser ? Editor.IsActive ? "Active" : "Inactive" : "New";

	public ActivationFilterOption SelectedActivationFilter
	{
		get => _selectedActivationFilter;
		set
		{
			if (_selectedActivationFilter == value) return;
			_selectedActivationFilter = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = _searchDebouncer.DebounceAsync(LoadUsersAsync);
		}
	}

	public int PageNumber
	{
		get => _pageNumber;
		private set
		{
			if (_pageNumber == value) return;
			_pageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			RaisePagingCommands();
		}
	}

	public long TotalCount
	{
		get => _totalCount;
		private set
		{
			if (_totalCount == value) return;
			_totalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			RaisePagingCommands();
		}
	}

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = _searchDebouncer.DebounceAsync(LoadUsersAsync);
		}
	}

	public UserListItemViewModel? SelectedUser
	{
		get => _selectedUser;
		set
		{
			if (_selectedUser == value) return;
			_selectedUser = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(EditorStatus));
			LoadSelectedUser();
			ToggleActiveCommand.RaiseCanExecuteChanged();
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;
		private set
		{
			if (_errorMessage == value) return;
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

	public async Task LoadUsersAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading users");
		var selectedId = SelectedUser?.Id;
		try
		{
			if (_availableRoles.Count == 0)
			{
				_availableRoles = await _userService.ListAssignableRolesAsync(cancellationToken);
				Editor.SetRoles(_availableRoles, []);
			}
			var page = await _userService.SearchUsersAsync(SearchText, SelectedActivationFilter.IsActive, PageNumber, PageSize, cancellationToken);
			CollectionSynchronizer.Replace(Users, page.Items.Select(user => new UserListItemViewModel(user)).ToArray());
			TotalCount = page.TotalCount;
			SelectedUser = selectedId is null ? null : Users.FirstOrDefault(x => x.Id == selectedId);
			OnPropertyChanged(nameof(HasUsers));
			CompleteOperation(Users.Count == 0, $"{page.TotalCount:N0} users");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { CompleteOperation(Users.Count == 0); }
		catch (Exception exception) { ErrorMessage = exception.Message; FailOperation(exception, "Users could not be loaded"); }
	}

	private void LoadSelectedUser()
	{
		ClearError();
		if (SelectedUser is null) return;
		Editor.Id = SelectedUser.Id;
		Editor.Email = SelectedUser.Email;
		Editor.DisplayName = SelectedUser.DisplayName;
		Editor.Password = string.Empty;
		Editor.ConfirmPassword = string.Empty;
		Editor.SetRoles(_availableRoles, SelectedUser.RoleIds);
		Editor.EffectivePermissions = SelectedUser.EffectivePermissions;
		Editor.IsActive = SelectedUser.IsActive;
		Editor.Version = SelectedUser.Version;
	}

	private void NewUser()
	{
		ClearError();
		SelectedUser = null;
		Editor.Clear();
		OnPropertyChanged(nameof(EditorStatus));
		ToggleActiveCommand.RaiseCanExecuteChanged();
		RequestEditorFocus();
	}

	private async Task SaveUserAsync(CancellationToken cancellationToken)
	{
		ClearError();
		if (!Editor.PasswordInputIsValid)
		{
			ErrorMessage = "The password does not meet all security requirements or the confirmation does not match.";
			FailOperation(new ArgumentException(ErrorMessage), "User could not be saved");
			return;
		}
		BeginOperation("Saving user");
		try
		{
			var user = Editor.Id == 0
				? await _userService.CreateUserAsync(Editor.Email, Editor.DisplayName, Editor.Password, Editor.SelectedRoleIds, cancellationToken)
				: await _userService.UpdateUserAsync(Editor.Id, Editor.Version, Editor.Email, Editor.DisplayName, Editor.Password, Editor.SelectedRoleIds, cancellationToken);
			UpdateUser(user);
			Editor.Clear();
			SelectedUser = null;
			CompleteOperation(Users.Count == 0, "User saved");
			RequestEditorFocus();
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "User could not be saved");
		}
	}

	private bool CanToggleActive() => Editor.IsExistingUser;

	private async Task ToggleActiveAsync(CancellationToken cancellationToken)
	{
		ClearError();
		if (!Editor.IsExistingUser) return;
		BeginOperation(Editor.IsActive ? "Deactivating user" : "Activating user");
		try
		{
			var user = await _userService.SetActiveAsync(Editor.Id, !Editor.IsActive, Editor.Version, cancellationToken);
			UpdateUser(user);
			Editor.Clear();
			SelectedUser = null;
			CompleteOperation(Users.Count == 0, "User updated");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "User could not be updated");
		}
	}

	private void UpdateUser(User user)
	{
		var existing = Users.FirstOrDefault(x => x.Id == user.Id);
		var matchesFilter = SelectedActivationFilter.IsActive is not bool isActive || user.IsActive == isActive;
		if (existing is not null && matchesFilter) Users[Users.IndexOf(existing)] = new UserListItemViewModel(user);
		else if (existing is not null) { Users.Remove(existing); TotalCount = Math.Max(0, TotalCount - 1); }
		else if (PageNumber == 1 && matchesFilter)
		{
			Users.Insert(0, new UserListItemViewModel(user));
			if (Users.Count > PageSize) Users.RemoveAt(Users.Count - 1);
			TotalCount++;
		}
		OnPropertyChanged(nameof(HasUsers));
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken) { if (PageNumber <= 1) return; PageNumber--; await LoadUsersAsync(cancellationToken); }
	private async Task NextPageAsync(CancellationToken cancellationToken) { if (!HasNextPage) return; PageNumber++; await LoadUsersAsync(cancellationToken); }
	private void RaisePagingCommands() { PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
	private void ClearError() => ErrorMessage = null;

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		SaveUserCommand.Dispose();
		ToggleActiveCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
	}
}
