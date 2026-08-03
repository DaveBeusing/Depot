// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class RoleService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly RoleRepository _roles;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public RoleService(IDatabaseTransactionRunner transactions, RoleRepository roles, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_roles = roles;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public IReadOnlyList<PermissionDefinition> Permissions => PermissionCatalog.Definitions;

	public Task<PageResult<Role>> SearchAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.RolesView);
		return _roles.SearchPageAsync(searchText, pageNumber, pageSize, cancellationToken);
	}

	public Task<IReadOnlyList<Role>> ListAssignableAsync(CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		return _roles.ListActiveAsync(cancellationToken);
	}

	public Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.RolesView);
		return _roles.GetByIdAsync(id, cancellationToken);
	}

	public async Task<Role> SaveAsync(Role value, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.RolesManage);
		var normalized = Normalize(value);
		if (normalized.Permissions.Count == 0)
			throw new InvalidOperationException("A role must contain at least one permission.");
		var existingCode = await _roles.GetByCodeAsync(normalized.Code, cancellationToken);
		if (existingCode is not null && existingCode.Id != normalized.Id)
			throw new InvalidOperationException($"Role code '{normalized.Code}' already exists.");
		Role? before = null;
		if (normalized.Id != 0)
		{
			before = await _roles.GetByIdAsync(normalized.Id, cancellationToken) ?? throw new InvalidOperationException("The role was not found.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("role");
			if (before.IsSystem) throw new InvalidOperationException("Protected system roles cannot be changed.");
			if (!string.Equals(before.Code, normalized.Code, StringComparison.Ordinal)) throw new InvalidOperationException("A role code cannot be changed after creation.");
		}

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (before is null)
			{
				normalized.Id = await RoleRepository.CreateAsync(transaction, normalized, token);
			}
			else
			{
				if (!await RoleRepository.UpdateAsync(transaction, normalized, token)) throw new ConcurrencyConflictException("role");
				normalized.Version++;
			}
			await RoleRepository.ReplacePermissionsAsync(transaction, normalized.Id, normalized.Permissions, token);
			await _auditEntries.CreateAsync(transaction, before is null
				? _audit.CreateCreatedEntry(normalized.Id, normalized)
				: _audit.CreateUpdatedEntry(normalized.Id, before, normalized), token);
			return normalized;
		}, cancellationToken);
	}

	public async Task<Role> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.RolesManage);
		var before = await _roles.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("The role was not found.");
		if (before.Version != version) throw new ConcurrencyConflictException("role");
		if (before.IsSystem) throw new InvalidOperationException("Protected system roles cannot be deactivated or reactivated manually.");
		var after = Copy(before);
		after.IsActive = isActive;
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await RoleRepository.UpdateAsync(transaction, after, token)) throw new ConcurrencyConflictException("role");
			after.Version++;
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(id, before, after), token);
			return after;
		}, cancellationToken);
	}

	private static Role Normalize(Role source)
	{
		var code = source.Code.Trim().ToUpperInvariant().Replace(' ', '_');
		var name = source.Name.Trim();
		var description = string.IsNullOrWhiteSpace(source.Description) ? null : source.Description.Trim();
		if (code.Length is < 2 or > 100 || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
			throw new ArgumentException("Role code must contain 2-100 letters, numbers, or underscores.", nameof(source));
		if (name.Length is < 2 or > 200) throw new ArgumentException("Role name must contain 2-200 characters.", nameof(source));
		if (description?.Length > 1000) throw new ArgumentException("Role description must not exceed 1000 characters.", nameof(source));
		return new Role { Id = source.Id, Code = code, Name = name, Description = description, IsSystem = source.IsSystem, IsActive = source.IsActive, Version = source.Version, Permissions = source.Permissions.Distinct().ToArray() };
	}

	private static Role Copy(Role source) => new() { Id = source.Id, Code = source.Code, Name = source.Name, Description = source.Description, IsSystem = source.IsSystem, IsActive = source.IsActive, Version = source.Version, Permissions = source.Permissions.ToArray() };
}
