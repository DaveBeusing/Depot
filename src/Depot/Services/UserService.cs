// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class UserService
{
	private readonly UserRepository _userRepository;

	public UserService(
		UserRepository userRepository)
	{
		_userRepository =
			userRepository;
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
}
