// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public interface IEnterpriseIdentityResolver
{
	Task<EnterpriseIdentityResolution?> ResolveAsync(
		ValidatedExternalIdentity identity,
		CancellationToken cancellationToken = default);

	Task<EnterpriseIdentityResolution?> RecordSuccessfulAuthenticationAsync(
		ValidatedExternalIdentity identity,
		CancellationToken cancellationToken = default);
}

public sealed class EnterpriseIdentityService : IEnterpriseIdentityResolver
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly EnterpriseIdentityRepository _identities;
	private readonly UserRepository _users;
	private readonly RoleRepository _roles;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public EnterpriseIdentityService(
		IDatabaseTransactionRunner transactions,
		EnterpriseIdentityRepository identities,
		UserRepository users,
		RoleRepository roles,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_identities = identities;
		_users = users;
		_roles = roles;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public Task<IReadOnlyList<EnterpriseIdentityProvider>> ListProvidersAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersView);
		return _identities.ListProvidersAsync(cancellationToken);
	}

	public async Task<EnterpriseIdentityProvider> CreateProviderAsync(
		string code,
		EnterpriseIdentityProviderKind kind,
		string displayName,
		string authority,
		string clientId,
		string? tenantId,
		bool isEnabled,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		var provider = NormalizeProvider(new EnterpriseIdentityProvider
		{
			Code = code,
			Kind = kind,
			DisplayName = displayName,
			Authority = authority,
			ClientId = clientId,
			TenantId = tenantId,
			IsEnabled = isEnabled,
			CreatedUtc = DateTime.UtcNow,
			UpdatedUtc = DateTime.UtcNow
		});
		if (await _identities.GetProviderByCodeAsync(provider.Code, cancellationToken) is not null)
			throw new InvalidOperationException($"Enterprise identity provider '{provider.Code}' already exists.");

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await _identities.CreateProviderAsync(transaction, provider, token);
			var created = provider with { Id = id, Version = 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<EnterpriseIdentityProvider> UpdateProviderAsync(
		long id,
		long expectedVersion,
		string displayName,
		string authority,
		string clientId,
		string? tenantId,
		bool isEnabled,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		var before = await _identities.GetProviderByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException("The enterprise identity provider was not found.");
		if (before.Version != expectedVersion) throw new ConcurrencyConflictException("enterprise identity provider");
		var after = NormalizeProvider(before with
		{
			DisplayName = displayName,
			Authority = authority,
			ClientId = clientId,
			TenantId = tenantId,
			IsEnabled = isEnabled,
			UpdatedUtc = DateTime.UtcNow
		});

		return await CommitProviderUpdateAsync(before, after, expectedVersion, cancellationToken);
	}

	public async Task<EnterpriseIdentityProvider> ConfigureAssurancePolicyAsync(
		long id,
		long expectedVersion,
		string? requiredAmr,
		string? requiredAcr,
		int? maximumAuthenticationAgeMinutes,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
		var before = await _identities.GetProviderByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException("The enterprise identity provider was not found.");
		if (before.Version != expectedVersion) throw new ConcurrencyConflictException("enterprise identity provider");
		var after = NormalizeProvider(before with
		{
			RequiredAmr = requiredAmr,
			RequiredAcr = requiredAcr,
			MaximumAuthenticationAgeMinutes = maximumAuthenticationAgeMinutes,
			UpdatedUtc = DateTime.UtcNow
		});

		return await CommitProviderUpdateAsync(before, after, expectedVersion, cancellationToken);
	}

	private Task<EnterpriseIdentityProvider> CommitProviderUpdateAsync(
		EnterpriseIdentityProvider before,
		EnterpriseIdentityProvider after,
		long expectedVersion,
		CancellationToken cancellationToken) =>
		_transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _identities.UpdateProviderAsync(transaction, after, expectedVersion, token))
				throw new ConcurrencyConflictException("enterprise identity provider");
			var committed = after with { Version = expectedVersion + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(before.Id, before, committed), token);
			return committed;
		}, cancellationToken);

	public async Task<IReadOnlyList<ExternalIdentityLink>> ListLinksForUserAsync(
		long userId,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersView);
		if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
		if (await _users.GetByIdAsync(userId, cancellationToken) is null)
			throw new InvalidOperationException("The user was not found.");
		return await _identities.ListLinksForUserAsync(userId, cancellationToken);
	}

	public async Task<ExternalIdentityLink> LinkAsync(
		long userId,
		ValidatedExternalIdentity identity,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
		var normalized = NormalizeIdentity(identity);
		var provider = await _identities.GetProviderByCodeAsync(normalized.ProviderCode, cancellationToken)
			?? throw new InvalidOperationException($"Enterprise identity provider '{normalized.ProviderCode}' was not found.");
		ValidateTenantBoundary(provider, normalized, throwOnMismatch: true);
		if (await _users.GetByIdAsync(userId, cancellationToken) is null)
			throw new InvalidOperationException("The user was not found.");

		var identityKey = ComputeIdentityKey(provider.Code, normalized.Issuer, normalized.Subject);
		var existing = await _identities.GetLinkByIdentityKeyAsync(identityKey, cancellationToken);
		if (existing is not null)
		{
			if (existing.UserId == userId)
				throw new InvalidOperationException("The external identity is already linked to this user.");
			throw new InvalidOperationException("The external identity is already linked to another local user.");
		}

		var now = DateTime.UtcNow;
		var link = new ExternalIdentityLink
		{
			ProviderId = provider.Id,
			ProviderCode = provider.Code,
			UserId = userId,
			Issuer = normalized.Issuer,
			Subject = normalized.Subject,
			IdentityKeySha256 = identityKey,
			TenantId = normalized.TenantId,
			Email = normalized.Email,
			DisplayName = normalized.DisplayName,
			LinkedUtc = now,
			LastSeenUtc = null,
			Version = 1
		};

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await _identities.CreateLinkAsync(transaction, link, token);
			var created = link with { Id = id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task UnlinkAsync(long linkId, long expectedVersion, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);
		if (linkId <= 0) throw new ArgumentOutOfRangeException(nameof(linkId));
		var before = await _identities.GetLinkByIdAsync(linkId, cancellationToken)
			?? throw new InvalidOperationException("The external identity link was not found.");
		if (before.Version != expectedVersion) throw new ConcurrencyConflictException("external identity link");

		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _identities.DeleteLinkAsync(transaction, linkId, expectedVersion, token))
				throw new ConcurrencyConflictException("external identity link");
			await _auditEntries.CreateAsync(
				transaction,
				_audit.CreateActionEntry(linkId, "Unlinked", before, (ExternalIdentityLink?)null),
				token);
			return true;
		}, cancellationToken);
	}

	public async Task<EnterpriseIdentityResolution?> ResolveAsync(
		ValidatedExternalIdentity identity,
		CancellationToken cancellationToken = default)
	{
		var normalized = NormalizeIdentity(identity);
		var provider = await _identities.GetProviderByCodeAsync(normalized.ProviderCode, cancellationToken);
		if (provider is not { IsEnabled: true }) return null;
		if (!Enum.IsDefined(provider.Kind)) throw new InvalidOperationException("Stored enterprise identity provider kind is invalid.");
		if (!ValidateTenantBoundary(provider, normalized, throwOnMismatch: false)) return null;

		var identityKey = ComputeIdentityKey(provider.Code, normalized.Issuer, normalized.Subject);
		var link = await _identities.GetLinkByIdentityKeyAsync(identityKey, cancellationToken);
		if (link is null) return null;
		if (link.ProviderId != provider.Id ||
			!string.Equals(link.ProviderCode, provider.Code, StringComparison.Ordinal) ||
			!string.Equals(link.Issuer, normalized.Issuer, StringComparison.Ordinal) ||
			!string.Equals(link.Subject, normalized.Subject, StringComparison.Ordinal))
			throw new InvalidOperationException("Stored external identity key failed its integrity check.");

		var user = await _users.GetByIdAsync(link.UserId, cancellationToken);
		if (user is not { IsActive: true }) return null;
		await HydrateLocalAuthorizationAsync(user, cancellationToken);
		return new EnterpriseIdentityResolution(provider, link, user);
	}

	public async Task<EnterpriseIdentityResolution?> RecordSuccessfulAuthenticationAsync(
		ValidatedExternalIdentity identity,
		CancellationToken cancellationToken = default)
	{
		var normalized = NormalizeIdentity(identity);
		var resolution = await ResolveAsync(normalized, cancellationToken);
		if (resolution is null) return null;
		var observed = resolution.Link with
		{
			TenantId = normalized.TenantId,
			Email = normalized.Email,
			DisplayName = normalized.DisplayName,
			LastSeenUtc = DateTime.UtcNow
		};

		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _identities.UpdateObservationAsync(transaction, observed, resolution.Link.Version, token))
				throw new ConcurrencyConflictException("external identity link");
			return true;
		}, cancellationToken);
		return resolution with { Link = observed with { Version = resolution.Link.Version + 1 } };
	}

	private async Task HydrateLocalAuthorizationAsync(User user, CancellationToken cancellationToken)
	{
		var rolesTask = _roles.GetUserRolesAsync(user.Id, cancellationToken);
		var permissionsTask = _roles.GetEffectivePermissionsAsync(user.Id, cancellationToken);
		await Task.WhenAll(rolesTask, permissionsTask);
		user.Roles = rolesTask.Result;
		user.EffectivePermissions = permissionsTask.Result;
	}

	private static EnterpriseIdentityProvider NormalizeProvider(EnterpriseIdentityProvider provider)
	{
		if (!Enum.IsDefined(provider.Kind)) throw new ArgumentOutOfRangeException(nameof(provider), "A supported enterprise identity provider kind is required.");
		return provider with
		{
			Code = NormalizeProviderCode(provider.Code),
			DisplayName = NormalizeRequired(provider.DisplayName, 120, "Provider display name"),
			Authority = NormalizeHttpsUri(provider.Authority, 512, "Provider authority"),
			ClientId = NormalizeRequired(provider.ClientId, 200, "Provider client ID"),
			TenantId = NormalizeOptional(provider.TenantId, 128, "Provider tenant ID"),
			RequiredAmr = NormalizeAssuranceValue(provider.RequiredAmr, 100, "Required amr value"),
			RequiredAcr = NormalizeAssuranceValue(provider.RequiredAcr, 200, "Required acr value"),
			MaximumAuthenticationAgeMinutes = NormalizeAuthenticationAge(provider.MaximumAuthenticationAgeMinutes)
		};
	}

	private static ValidatedExternalIdentity NormalizeIdentity(ValidatedExternalIdentity identity)
	{
		ArgumentNullException.ThrowIfNull(identity);
		var subject = identity.Subject;
		if (string.IsNullOrWhiteSpace(subject) || subject.Length > 512 || subject.Contains('\0'))
			throw new ArgumentException("External identity subject must contain 1-512 characters and cannot contain a NUL character.", nameof(identity));
		return identity with
		{
			ProviderCode = NormalizeProviderCode(identity.ProviderCode),
			Issuer = NormalizeHttpsUri(identity.Issuer, 512, "External identity issuer"),
			Subject = subject,
			TenantId = NormalizeOptional(identity.TenantId, 128, "External identity tenant ID"),
			Email = NormalizeOptional(identity.Email, 320, "External identity email"),
			DisplayName = NormalizeOptional(identity.DisplayName, 200, "External identity display name")
		};
	}

	private static bool ValidateTenantBoundary(
		EnterpriseIdentityProvider provider,
		ValidatedExternalIdentity identity,
		bool throwOnMismatch)
	{
		if (provider.TenantId is null || string.Equals(provider.TenantId, identity.TenantId, StringComparison.OrdinalIgnoreCase))
			return true;
		if (throwOnMismatch)
			throw new InvalidOperationException("The external identity tenant does not match the configured provider tenant boundary.");
		return false;
	}

	private static string NormalizeProviderCode(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Enterprise identity provider code is required.", nameof(value));
		var code = value.Trim().ToLowerInvariant();
		if (code.Length > 64 || !char.IsLetterOrDigit(code[0]) || code.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
			throw new ArgumentException("Enterprise identity provider code must be 1-64 characters and contain only letters, digits, '.', '-' or '_'.", nameof(value));
		return code;
	}

	private static string NormalizeHttpsUri(string value, int maximumLength, string name)
	{
		var normalized = NormalizeRequired(value, maximumLength, name);
		if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
			!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
			!string.IsNullOrEmpty(uri.UserInfo) ||
			!string.IsNullOrEmpty(uri.Query) ||
			!string.IsNullOrEmpty(uri.Fragment))
			throw new ArgumentException($"{name} must be an absolute HTTPS URI without user-info, query or fragment.", nameof(value));
		return normalized;
	}

	private static string NormalizeRequired(string value, int maximumLength, string name)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", nameof(value));
		var normalized = value.Trim();
		if (normalized.Length > maximumLength) throw new ArgumentException($"{name} cannot exceed {maximumLength} characters.", nameof(value));
		return normalized;
	}

	private static string? NormalizeOptional(string? value, int maximumLength, string name)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var normalized = value.Trim();
		if (normalized.Length > maximumLength) throw new ArgumentException($"{name} cannot exceed {maximumLength} characters.", nameof(value));
		return normalized;
	}

	private static string? NormalizeAssuranceValue(string? value, int maximumLength, string name)
	{
		var normalized = NormalizeOptional(value, maximumLength, name);
		if (normalized is not null && normalized.Any(char.IsWhiteSpace))
			throw new ArgumentException($"{name} cannot contain whitespace.", nameof(value));
		return normalized;
	}

	private static int? NormalizeAuthenticationAge(int? value)
	{
		if (value is null) return null;
		if (value is < 1 or > 1440)
			throw new ArgumentOutOfRangeException(nameof(value), "Maximum authentication age must be between 1 and 1440 minutes.");
		return value;
	}

	private static string ComputeIdentityKey(string providerCode, string issuer, string subject)
	{
		var material = Encoding.UTF8.GetBytes($"{providerCode}\0{issuer}\0{subject}");
		return Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
	}
}
