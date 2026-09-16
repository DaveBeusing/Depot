// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum EnterpriseIdentityProviderKind
{
	OpenIdConnect = 1,
	MicrosoftEntraId = 2
}

public sealed record EnterpriseIdentityProvider
{
	public long Id { get; init; }
	public string Code { get; init; } = string.Empty;
	public EnterpriseIdentityProviderKind Kind { get; init; }
	public string DisplayName { get; init; } = string.Empty;
	public string Authority { get; init; } = string.Empty;
	public string ClientId { get; init; } = string.Empty;
	public string? TenantId { get; init; }
	public bool IsEnabled { get; init; }
	public DateTime CreatedUtc { get; init; }
	public DateTime UpdatedUtc { get; init; }
	public long Version { get; init; } = 1;
}

public sealed record ExternalIdentityLink
{
	public long Id { get; init; }
	public long ProviderId { get; init; }
	public string ProviderCode { get; init; } = string.Empty;
	public long UserId { get; init; }
	public string Issuer { get; init; } = string.Empty;
	public string Subject { get; init; } = string.Empty;
	public string IdentityKeySha256 { get; init; } = string.Empty;
	public string? TenantId { get; init; }
	public string? Email { get; init; }
	public string? DisplayName { get; init; }
	public DateTime LinkedUtc { get; init; }
	public DateTime? LastSeenUtc { get; init; }
	public long Version { get; init; } = 1;
}

/// <summary>
/// External identity values that have already passed protocol validation by an authentication provider.
/// This contract intentionally excludes external roles, groups and permissions; Depot RBAC remains authoritative.
/// </summary>
public sealed record ValidatedExternalIdentity(
	string ProviderCode,
	string Issuer,
	string Subject,
	string? TenantId = null,
	string? Email = null,
	string? DisplayName = null);

public sealed record EnterpriseIdentityResolution(
	EnterpriseIdentityProvider Provider,
	ExternalIdentityLink Link,
	User User);
