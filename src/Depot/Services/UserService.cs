// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net.Mail;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class UserService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly UserRepository _users;
	private readonly RoleRepository _roles;
	private readonly AuditRepository _auditEntries;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuthorizationService _authorization;
	private readonly AuditService _audit;

	public UserService(IDatabaseTransactionRunner transactions, UserRepository users, RoleRepository roles, AuditRepository auditEntries, PasswordHasher passwordHasher, AuthorizationService authorization, AuditService audit)
	{
		_transactions = transactions;
		_users = users;
		_roles = roles;
		_auditEntries = auditEntries;
		_passwordHasher = passwordHasher;
		_authorization = authorization;
		_audit = audit;
	}

	public async Task<PageResult<User>> SearchUsersAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersView);
		var page = await _users.SearchPageAsync(searchText, pageNumber, pageSize, cancellationToken);
		var ids = page.Items.Select(user => user.Id).ToArray();
		var roles = await _roles.GetUserRolesAsync(ids, cancellationToken);
		var permissions = await _roles.GetEffectivePermissionsAsync(ids, cancellationToken);
		foreach (var user in page.Items)
		{
			user.Roles = roles.GetValueOrDefault(user.Id) ?? [];
			user.EffectivePermissions = permissions.GetValueOrDefault(user.Id) ?? new HashSet<ApplicationPermission>();
		}
		return page;
	}

	public Task<IReadOnlyList<Role>> ListAssignableRolesAsync(CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		return _roles.ListActiveAsync(cancellationToken);
	}

	public async Task<User> CreateUserAsync(string email, string displayName, string password, IReadOnlyCollection<long> roleIds, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		email = NormalizeAndValidateEmail(email);
		displayName = ValidateDisplayName(displayName);
		ValidatePassword(password);
		var roles = await ValidateRolesAsync(roleIds, cancellationToken);
		if (await _users.GetByEmailAsync(email, cancellationToken) is not null) throw new InvalidOperationException($"A user with email '{email}' already exists.");
		var user = new User { Email = email, DisplayName = displayName, Role = UserRole.User, IsAdministrator = false, CanApprovePurchaseOrders = false, IsActive = true, CreatedUtc = DateTime.UtcNow, Roles = roles };
		user.Id = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await UserRepository.CreateAsync(transaction, user, _passwordHasher.Hash(password), token);
			user.Id = id;
			await RoleRepository.ReplaceUserRolesAsync(transaction, id, roles.Select(role => role.Id), token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, user), token);
			return id;
		}, cancellationToken);
		return await HydrateAsync(user, cancellationToken);
	}

	public async Task<User> UpdateUserAsync(long id, long expectedVersion, string email, string displayName, string password, IReadOnlyCollection<long> roleIds, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		email = NormalizeAndValidateEmail(email);
		displayName = ValidateDisplayName(displayName);
		if (!string.IsNullOrEmpty(password)) ValidatePassword(password);
		var roles = await ValidateRolesAsync(roleIds, cancellationToken);
		var stored = await _users.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The user was not found.");
		var before = await HydrateAsync(stored, cancellationToken);
		if (before.Version != expectedVersion) throw new ConcurrencyConflictException("user");
		var duplicate = await _users.GetByEmailAsync(email, cancellationToken);
		if (duplicate is not null && duplicate.Id != id) throw new InvalidOperationException($"A user with email '{email}' already exists.");
		var after = Copy(before);
		after.Email = email;
		after.DisplayName = displayName;
		after.Roles = roles;
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var hash = string.IsNullOrEmpty(password) ? null : _passwordHasher.Hash(password);
			if (!await UserRepository.UpdateAsync(transaction, after, hash, token)) throw new ConcurrencyConflictException("user");
			await RoleRepository.ReplaceUserRolesAsync(transaction, id, roles.Select(role => role.Id), token);
			after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return true;
		}, cancellationToken);
		after = await HydrateAsync(after, cancellationToken);
		if (_authorization.CurrentUser?.Id == after.Id) _authorization.SignIn(after, after.EffectivePermissions);
		return after;
	}

	public async Task<User> SetActiveAsync(long id, bool isActive, long expectedVersion, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		var stored = await _users.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The user was not found.");
		var before = await HydrateAsync(stored, cancellationToken);
		if (before.Version != expectedVersion) throw new ConcurrencyConflictException("user");
		if (!isActive && _authorization.CurrentUser?.Id == id) throw new InvalidOperationException("The currently signed-in user cannot be deactivated.");
		var after = Copy(before);
		after.IsActive = isActive;
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await UserRepository.SetActiveAsync(transaction, id, isActive, expectedVersion, token)) throw new ConcurrencyConflictException("user");
			after.Version++;
			await _auditEntries.CreateAsync(transaction, isActive ? _audit.CreateUpdatedEntry(id, before, after) : CreateDeactivatedEntry(id, before, after), token);
			return true;
		}, cancellationToken);
		return after;
	}

	private AuditEntry CreateDeactivatedEntry(long id, User before, User after)
	{
		var entry = _audit.CreateUpdatedEntry(id, before, after);
		entry.Action = "Deactivated";
		return entry;
	}

	private async Task<User> HydrateAsync(User user, CancellationToken cancellationToken)
	{
		user.Roles = await _roles.GetUserRolesAsync(user.Id, cancellationToken);
		user.EffectivePermissions = await _roles.GetEffectivePermissionsAsync(user.Id, cancellationToken);
		return user;
	}

	private async Task<IReadOnlyList<Role>> ValidateRolesAsync(IEnumerable<long> roleIds, CancellationToken cancellationToken)
	{
		var ids = roleIds.Distinct().ToArray();
		if (ids.Length == 0) throw new ArgumentException("At least one active role is required.", nameof(roleIds));
		var roles = await _roles.GetByIdsAsync(ids, cancellationToken);
		if (roles.Count != ids.Length || roles.Any(role => !role.IsActive)) throw new InvalidOperationException("Every assigned role must exist and be active.");
		return roles;
	}

	private static string NormalizeAndValidateEmail(string email)
	{
		email = email.Trim().ToLowerInvariant();
		if (!MailAddress.TryCreate(email, out var parsed) || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A valid email address is required.", nameof(email));
		return email;
	}

	private static string ValidateDisplayName(string displayName)
	{
		displayName = displayName.Trim();
		if (displayName.Length is < 1 or > 200) throw new ArgumentException("Display name must contain 1-200 characters.", nameof(displayName));
		return displayName;
	}

	private static void ValidatePassword(string password)
	{
		if (password.Length < 8 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
			throw new ArgumentException("The password must contain at least 8 characters, including uppercase, lowercase, and a number.", nameof(password));
	}

	private static User Copy(User user) => new() { Id = user.Id, Email = user.Email, DisplayName = user.DisplayName, IsAdministrator = user.IsAdministrator, CanApprovePurchaseOrders = user.CanApprovePurchaseOrders, Role = user.Role, Roles = user.Roles.ToArray(), EffectivePermissions = user.EffectivePermissions.ToHashSet(), IsActive = user.IsActive, CreatedUtc = user.CreatedUtc, Version = user.Version };
}
