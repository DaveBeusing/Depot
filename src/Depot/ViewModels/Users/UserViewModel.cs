// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels.Users;

public sealed class UserViewModel
	: BaseViewModel
{
	private readonly UserService _userService;

	private string _searchText = string.Empty;

	public UserViewModel(
		UserService userService)
	{
		_userService =
			userService;

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

			_searchText =
				value;

			OnPropertyChanged();

			LoadUsers();
		}
	}

	public void LoadUsers()
	{
		Users.Clear();

		var users =
			_userService.GetUsers();

		if (!string.IsNullOrWhiteSpace(
				SearchText))
		{
			users =
				users
					.Where(
						x =>
							x.UserName.Contains(
								SearchText,
								StringComparison.OrdinalIgnoreCase) ||

							x.DisplayName.Contains(
								SearchText,
								StringComparison.OrdinalIgnoreCase))
					.ToList();
		}

		foreach (var user in users)
		{
			Users.Add(
				new UserListItemViewModel(
					user));
		}
	}
}
