// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net.Mail;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class UserService
{
	private readonly UserRepository _userRepository;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuthorizationService _authorizationService;
	private readonly AuditService _auditService;

	public UserService(
		UserRepository userRepository,
		PasswordHasher passwordHasher,
		AuthorizationService authorizationService,
		AuditService auditService)
	{
		_userRepository = userRepository;
		_passwordHasher = passwordHasher;
		_authorizationService = authorizationService;
		_auditService = auditService;
	}

	public IReadOnlyList<User> GetUsers() => _userRepository.GetAll();

	public User CreateUser(
		string email,
		string displayName,
		string password,
		bool isAdministrator)
	{
		email = NormalizeAndValidateEmail(email);
		displayName = ValidateDisplayName(displayName);
		ValidatePassword(password);

		if (_userRepository.GetByEmail(email) is not null)
		{
			throw new InvalidOperationException($"A user with email '{email}' already exists.");
		}

		var user = new User
		{
			Email = email,
			DisplayName = displayName,
			IsAdministrator = isAdministrator,
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};

		user.Id = _userRepository.Create(user, _passwordHasher.Hash(password));
		_auditService.RecordCreated(user.Id, user);
		return user;
	}

	public User UpdateUser(
		long id,
		long expectedVersion,
		string email,
		string displayName,
		string password,
		bool isAdministrator)
	{
		if (id <= 0)
		{
			throw new ArgumentException("User id is required.", nameof(id));
		}

		email = NormalizeAndValidateEmail(email);
		displayName = ValidateDisplayName(displayName);
		if (!string.IsNullOrEmpty(password))
		{
			ValidatePassword(password);
		}

		var user = _userRepository.GetById(id)
			?? throw new InvalidOperationException($"User with id '{id}' was not found.");
		if (user.Version != expectedVersion)
		{
			throw new ConcurrencyConflictException("user");
		}

		var before = Copy(user);
		var existingEmail = _userRepository.GetByEmail(email);
		if (existingEmail is not null && existingEmail.Id != id)
		{
			throw new InvalidOperationException($"A user with email '{email}' already exists.");
		}

		user.Email = email;
		user.DisplayName = displayName;
		user.IsAdministrator = isAdministrator;
		if (!_userRepository.Update(
			user,
			string.IsNullOrEmpty(password) ? null : _passwordHasher.Hash(password)))
		{
			throw new ConcurrencyConflictException("user");
		}

		user.Version++;
		_auditService.RecordUpdated(user.Id, before, user);

		if (_authorizationService.CurrentUser?.Id == user.Id)
		{
			_authorizationService.SignIn(user);
		}

		return user;
	}

	public void SetActive(long id, bool isActive, long expectedVersion)
	{
		if (id <= 0)
		{
			throw new ArgumentException("User id is required.", nameof(id));
		}

		var user = _userRepository.GetById(id)
			?? throw new InvalidOperationException($"User with id '{id}' was not found.");
		if (user.Version != expectedVersion)
		{
			throw new ConcurrencyConflictException("user");
		}

		if (!isActive && _authorizationService.CurrentUser?.Id == user.Id)
		{
			throw new InvalidOperationException("The currently signed-in user cannot be deactivated.");
		}

		if (!_userRepository.SetActive(id, isActive, expectedVersion))
		{
			throw new ConcurrencyConflictException("user");
		}

		var before = Copy(user);
		user.IsActive = isActive;
		user.Version++;
		if (isActive)
		{
			_auditService.RecordUpdated(user.Id, before, user);
		}
		else
		{
			_auditService.RecordDeactivated(user.Id, before, user);
		}
	}

	private static string NormalizeAndValidateEmail(string email)
	{
		email = email.Trim().ToLowerInvariant();
		if (!MailAddress.TryCreate(email, out var parsedAddress) ||
			!string.Equals(parsedAddress.Address, email, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("A valid email address is required.", nameof(email));
		}

		return email;
	}

	private static string ValidateDisplayName(string displayName)
	{
		displayName = displayName.Trim();
		if (string.IsNullOrWhiteSpace(displayName))
		{
			throw new ArgumentException("Display name is required.", nameof(displayName));
		}

		return displayName;
	}

	private static void ValidatePassword(string password)
	{
		if (password.Length < 8 ||
			!password.Any(char.IsUpper) ||
			!password.Any(char.IsLower) ||
			!password.Any(char.IsDigit))
		{
			throw new ArgumentException(
				"The password must contain at least 8 characters, including uppercase, lowercase, and a number.",
				nameof(password));
		}
	}

	private static User Copy(User user) =>
		new()
		{
			Id = user.Id,
			Email = user.Email,
			DisplayName = user.DisplayName,
			IsAdministrator = user.IsAdministrator,
			IsActive = user.IsActive,
			CreatedUtc = user.CreatedUtc,
			Version = user.Version
		};
}
