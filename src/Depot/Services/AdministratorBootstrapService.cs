// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net.Mail;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class AdministratorBootstrapService
{
	public const string LegacyAdministratorEmail = "admin@depot.local";

	private readonly DatabaseAccess _database;
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly UserRepository _users;
	private readonly RoleRepository _roles;
	private readonly AuditRepository _auditEntries;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuditService _audit;

	public AdministratorBootstrapService(DatabaseAccess database, IDatabaseTransactionRunner transactions, AuthorizationService authorization)
	{
		_database = database;
		_transactions = transactions;
		_users = new UserRepository(database);
		_roles = new RoleRepository(database);
		_auditEntries = new AuditRepository(database);
		_passwordHasher = new PasswordHasher();
		_audit = new AuditService(_auditEntries, authorization);
	}

	public async Task<bool> RequiresSetupAsync(CancellationToken cancellationToken)
	{
		var legacy = await _users.GetByEmailAsync(LegacyAdministratorEmail, cancellationToken);
		if (legacy?.IsActive == true) return true;

		var activeAdministrators = Convert.ToInt64(await _database.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND IsAdministrator = 1;",
			cancellationToken));
		return activeAdministrators == 0;
	}

	public async Task<User> CreateAdministratorAsync(string email, string displayName, string password, CancellationToken cancellationToken)
	{
		email = NormalizeEmail(email);
		displayName = displayName.Trim();
		if (string.Equals(email, LegacyAdministratorEmail, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a personal administrator email address instead of the retired legacy account.", nameof(email));
		if (displayName.Length is < 1 or > 200) throw new ArgumentException("Display name must contain 1-200 characters.", nameof(displayName));
		PasswordPolicy.Validate(password, email);
		if (await _users.GetByEmailAsync(email, cancellationToken) is not null) throw new InvalidOperationException("An account with this email already exists.");

		var administratorRole = await _roles.GetByCodeAsync(SystemRoleCatalog.AdministratorCode, cancellationToken)
			?? throw new InvalidOperationException("The system Administrator role is missing.");
		if (!administratorRole.IsActive) throw new InvalidOperationException("The system Administrator role is inactive.");
		var legacy = await _users.GetByEmailAsync(LegacyAdministratorEmail, cancellationToken);
		var user = new User
		{
			Email = email,
			DisplayName = displayName,
			Role = UserRole.Administrator,
			IsAdministrator = true,
			CanApprovePurchaseOrders = true,
			IsActive = true,
			CreatedUtc = DateTime.UtcNow,
			Roles = [administratorRole]
		};

		user.Id = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (legacy?.IsActive == true)
			{
				if (!await UserRepository.SetActiveAsync(transaction, legacy.Id, false, legacy.Version, token)) throw new ConcurrencyConflictException("legacy administrator");
				var retired = Copy(legacy);
				retired.IsActive = false;
				retired.Version++;
				var retirement = _audit.CreateUpdatedEntry(legacy.Id, legacy, retired);
				retirement.Action = "RetiredLegacyAdministrator";
				await _auditEntries.CreateAsync(transaction, retirement, token);
			}

			var id = await UserRepository.CreateAsync(transaction, user, _passwordHasher.Hash(password), token);
			user.Id = id;
			await RoleRepository.ReplaceUserRolesAsync(transaction, id, [administratorRole.Id], token);
			var created = _audit.CreateCreatedEntry(id, user);
			created.Action = "InitialAdministratorCreated";
			await _auditEntries.CreateAsync(transaction, created, token);
			return id;
		}, cancellationToken);
		return user;
	}

	private static string NormalizeEmail(string email)
	{
		email = email.Trim().ToLowerInvariant();
		if (!MailAddress.TryCreate(email, out var parsed) || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A valid email address is required.", nameof(email));
		return email;
	}

	private static User Copy(User source) => new()
	{
		Id = source.Id,
		Email = source.Email,
		DisplayName = source.DisplayName,
		Role = source.Role,
		IsAdministrator = source.IsAdministrator,
		CanApprovePurchaseOrders = source.CanApprovePurchaseOrders,
		IsActive = source.IsActive,
		CreatedUtc = source.CreatedUtc,
		Version = source.Version,
		Roles = source.Roles.ToArray(),
		EffectivePermissions = source.EffectivePermissions.ToHashSet()
	};
}
