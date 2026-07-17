// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

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
			var page = await _userService.SearchUsersAsync(
				SearchText,
				PageNumber,
				PageSize,
				cancellationToken);
			Users.Clear();
			foreach (var user in page.Items) Users.Add(new UserListItemViewModel(user));
			TotalCount = page.TotalCount;
			SelectedUser = selectedId is null ? null : Users.FirstOrDefault(x => x.Id == selectedId);
			CompleteOperation(Users.Count == 0, $"{page.TotalCount:N0} users");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(Users.Count == 0);
		}
		catch (Exception exception)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Users could not be loaded");
		}
	}

	private void LoadSelectedUser()
	{
		ClearError();
		if (SelectedUser is null) return;
		Editor.Id = SelectedUser.Id;
		Editor.Email = SelectedUser.Email;
		Editor.DisplayName = SelectedUser.DisplayName;
		Editor.Password = string.Empty;
		Editor.IsAdministrator = SelectedUser.IsAdministrator;
		Editor.IsActive = SelectedUser.IsActive;
		Editor.Version = SelectedUser.Version;
	}

	private void NewUser()
	{
		ClearError();
		SelectedUser = null;
		Editor.Clear();
		ToggleActiveCommand.RaiseCanExecuteChanged();
	}

	private async Task SaveUserAsync(CancellationToken cancellationToken)
	{
		ClearError();
		BeginOperation("Saving user");
		try
		{
			var user = Editor.Id == 0
				? await _userService.CreateUserAsync(
					Editor.Email, Editor.DisplayName, Editor.Password, Editor.IsAdministrator, cancellationToken)
				: await _userService.UpdateUserAsync(
					Editor.Id, Editor.Version, Editor.Email, Editor.DisplayName,
					Editor.Password, Editor.IsAdministrator, cancellationToken);
			UpdateUser(user);
			Editor.Clear();
			SelectedUser = null;
			CompleteOperation(Users.Count == 0, "User saved");
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
			var user = await _userService.SetActiveAsync(
				Editor.Id,
				!Editor.IsActive,
				Editor.Version,
				cancellationToken);
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
		if (existing is not null) Users[Users.IndexOf(existing)] = new UserListItemViewModel(user);
		else if (PageNumber == 1)
		{
			Users.Insert(0, new UserListItemViewModel(user));
			if (Users.Count > PageSize) Users.RemoveAt(Users.Count - 1);
			TotalCount++;
		}
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadUsersAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadUsersAsync(cancellationToken);
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

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
