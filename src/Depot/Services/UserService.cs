// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class UserService
{
	private readonly UserRepository _userRepository;

	public UserService(UserRepository userRepository)
	{
		_userRepository = userRepository;
	}

	public IReadOnlyList<User> GetUsers()
	{
		return _userRepository.GetAll();
	}

	public User CreateUser(
		string userName,
		string displayName,
		bool isAdministrator)
	{
		userName =
			userName.Trim();

		displayName =
			displayName.Trim();

		if (string.IsNullOrWhiteSpace(
				userName))
		{
			throw new ArgumentException(
				"User name is required.",
				nameof(userName));
		}

		if (string.IsNullOrWhiteSpace(
				displayName))
		{
			throw new ArgumentException(
				"Display name is required.",
				nameof(displayName));
		}

		var existingUser =
			_userRepository.GetByUserName(
				userName);

		if (existingUser is not null)
		{
			throw new InvalidOperationException(
				$"User '{userName}' already exists.");
		}

		var user =
			new User
			{
				UserName =
					userName,

				DisplayName =
					displayName,

				IsAdministrator =
					isAdministrator,

				IsActive =
					true,

				CreatedUtc =
					DateTime.UtcNow
			};

		user.Id =
			_userRepository.Create(
				user);

		return user;
	}

	public User UpdateUser(long id, string displayName, bool isAdministrator)
	{
		displayName = displayName.Trim();

		if (id <= 0)
		{
			throw new ArgumentException(
				"User id is required.",
				nameof(id));
		}

		if (string.IsNullOrWhiteSpace(displayName))
		{
			throw new ArgumentException(
				"Display name is required.",
				nameof(displayName));
		}

		var user =
			_userRepository.GetById(
				id);

		if (user is null)
		{
			throw new InvalidOperationException(
				$"User with id '{id}' was not found.");
		}

		user.DisplayName =
			displayName;

		user.IsAdministrator =
			isAdministrator;

		_userRepository.Update(
			user);

		return user;
	}

	public void SetActive(long id, bool isActive)
	{
		if (id <= 0)
		{
			throw new ArgumentException("User id is required.", nameof(id));
		}

		var user = _userRepository.GetById(id);

		if (user is null)
		{
			throw new InvalidOperationException($"User with id '{id}' was not found.");
		}

		var currentUser = App.AuthorizationService.CurrentUser;

		if (!isActive && currentUser is not null && currentUser.Id == id)
		{
			throw new InvalidOperationException("The currently signed-in user cannot be deactivated.");
		}

		_userRepository.SetActive(id, isActive);
	}

}
