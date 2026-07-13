// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;
using Depot.Commands;

namespace Depot.ViewModels.Users;

public sealed class UserViewModel : BaseViewModel
{
	private readonly UserService _userService;
	private string _searchText = string.Empty;
	private UserListItemViewModel? _selectedUser;
	private string? _errorMessage;

	public UserEditorViewModel Editor { get; }

	public RelayCommand NewUserCommand { get; }

	public RelayCommand SaveUserCommand { get; }

	public RelayCommand ToggleActiveCommand { get; }

	public UserViewModel(UserService userService)
	{
		_userService = userService;
		Editor = new UserEditorViewModel();
		NewUserCommand = new RelayCommand(NewUser);
		SaveUserCommand = new RelayCommand(SaveUser);
		ToggleActiveCommand = new RelayCommand(ToggleActive, CanDeactivateUser);
		LoadUsers();
	}

	public ObservableCollection<UserListItemViewModel> Users { get; }
		= new();

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value)
			{
				return;
			}
			_searchText = value;
			OnPropertyChanged();
			LoadUsers();
		}
	}

	public void LoadUsers()
	{
		Users.Clear();
		var users = _userService.GetUsers();

		if (!string.IsNullOrWhiteSpace(SearchText))
		{
			users =
				users
					.Where(
						x =>
							x.Email.Contains(
								SearchText,
								StringComparison.OrdinalIgnoreCase) ||

							x.DisplayName.Contains(
								SearchText,
								StringComparison.OrdinalIgnoreCase))
					.ToList();
		}

		foreach (var user in users)
		{
			Users.Add(new UserListItemViewModel(user));
		}
	}

	public UserListItemViewModel? SelectedUser
	{
		get => _selectedUser;
		set
		{
			if (_selectedUser == value)
			{
				return;
			}
			_selectedUser = value;
			OnPropertyChanged();
			LoadSelectedUser();
			ToggleActiveCommand.RaiseCanExecuteChanged();
		}
	}



	private void LoadSelectedUser()
	{
		ClearError();
		if (SelectedUser is null)
		{
			return;
		}
		Editor.Id = SelectedUser.Id;
		Editor.Email = SelectedUser.Email;
		Editor.DisplayName = SelectedUser.DisplayName;
		Editor.Password = string.Empty;
		Editor.IsAdministrator = SelectedUser.IsAdministrator;
		Editor.IsActive = SelectedUser.IsActive;
	}

	private void NewUser()
	{
		ClearError();
		SelectedUser = null;
		Editor.Clear();
		ToggleActiveCommand.RaiseCanExecuteChanged();
	}

	private bool CanDeactivateUser()
	{
		return Editor.IsExistingUser;
	}

	private void ClearError()
	{
		ErrorMessage = null;
	}

	private void SaveUser()
	{
		ClearError();
		try
		{
			if (Editor.Id == 0)
			{
				_userService.CreateUser(
					Editor.Email,
					Editor.DisplayName,
					Editor.Password,
					Editor.IsAdministrator);
			}
			else
			{
				_userService.UpdateUser(
					Editor.Id,
					Editor.Email,
					Editor.DisplayName,
					Editor.Password,
					Editor.IsAdministrator);
			}
			LoadUsers();
			Editor.Clear();
			SelectedUser = null;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void ToggleActive()
	{
		ClearError();
		if (!Editor.IsExistingUser)
		{
			return;
		}
		try
		{
			_userService.SetActive(Editor.Id, !Editor.IsActive);
			LoadUsers();
			Editor.Clear();
			SelectedUser = null;
			ToggleActiveCommand.RaiseCanExecuteChanged();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}


	public string? ErrorMessage
	{
		get => _errorMessage;
		private set
		{
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);


}
