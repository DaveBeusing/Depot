// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Claims;

using Depot.Models;

namespace Depot.Services;

internal sealed record ExternalAuthenticationAssuranceEvidence(
	IReadOnlyList<string> AuthenticationMethods,
	string? AuthenticationContext,
	DateTimeOffset? AuthenticatedAtUtc,
	string? AuthorizedParty,
	IReadOnlyList<string> Audiences);

internal static class ExternalAuthenticationAssurance
{
	private static readonly TimeSpan FutureClockSkew = TimeSpan.FromMinutes(2);

	public static string? ValidateProviderPolicy(EnterpriseIdentityProvider provider)
	{
		ArgumentNullException.ThrowIfNull(provider);
		if (!ValidPolicyValue(provider.RequiredAmr, 100) || !ValidPolicyValue(provider.RequiredAcr, 200))
			return "provider_assurance_policy_invalid";
		if (provider.MaximumAuthenticationAgeMinutes is int maximumAgeMinutes && maximumAgeMinutes is < 1 or > 1440)
			return "provider_assurance_policy_invalid";
		return null;
	}

	public static string? Validate(
		EnterpriseIdentityProvider provider,
		ClaimsPrincipal principal,
		IEnumerable<string> audiences,
		DateTimeOffset nowUtc)
	{
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(principal);
		ArgumentNullException.ThrowIfNull(audiences);

		var policyFailure = ValidateProviderPolicy(provider);
		if (policyFailure is not null) return policyFailure;
		if (!TryReadSingleClaim(principal, "acr", out var acr) ||
			!TryReadSingleClaim(principal, "azp", out var authorizedParty) ||
			!TryReadSingleClaim(principal, "auth_time", out var authenticationTimeValue))
			return "authentication_assurance_claim_ambiguous";

		DateTimeOffset? authenticatedAtUtc = null;
		if (!string.IsNullOrWhiteSpace(authenticationTimeValue))
		{
			if (!long.TryParse(authenticationTimeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
				return "authentication_time_invalid";
			try
			{
				authenticatedAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
			}
			catch (ArgumentOutOfRangeException)
			{
				return "authentication_time_invalid";
			}
		}

		var evidence = new ExternalAuthenticationAssuranceEvidence(
			principal.Claims
				.Where(claim => string.Equals(claim.Type, "amr", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(claim.Value))
				.Select(claim => claim.Value)
				.Distinct(StringComparer.Ordinal)
				.ToArray(),
			acr,
			authenticatedAtUtc,
			authorizedParty,
			audiences
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Distinct(StringComparer.Ordinal)
				.ToArray());

		return Validate(provider, evidence, nowUtc);
	}

	internal static string? Validate(
		EnterpriseIdentityProvider provider,
		ExternalAuthenticationAssuranceEvidence evidence,
		DateTimeOffset nowUtc)
	{
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(evidence);

		var policyFailure = ValidateProviderPolicy(provider);
		if (policyFailure is not null) return policyFailure;
		if (evidence.Audiences.Count > 1 && string.IsNullOrWhiteSpace(evidence.AuthorizedParty))
			return "authorized_party_missing";
		if (!string.IsNullOrWhiteSpace(evidence.AuthorizedParty) &&
			!string.Equals(evidence.AuthorizedParty, provider.ClientId, StringComparison.Ordinal))
			return "authorized_party_mismatch";

		if (!string.IsNullOrWhiteSpace(provider.RequiredAmr) &&
			!evidence.AuthenticationMethods.Contains(provider.RequiredAmr, StringComparer.Ordinal))
			return "authentication_method_requirement_not_met";
		if (!string.IsNullOrWhiteSpace(provider.RequiredAcr) &&
			!string.Equals(evidence.AuthenticationContext, provider.RequiredAcr, StringComparison.Ordinal))
			return "authentication_context_requirement_not_met";

		if (provider.MaximumAuthenticationAgeMinutes is int maximumAgeMinutes)
		{
			if (evidence.AuthenticatedAtUtc is null)
				return "authentication_time_required";
			if (evidence.AuthenticatedAtUtc.Value > nowUtc + FutureClockSkew)
				return "authentication_time_invalid";
			if (nowUtc - evidence.AuthenticatedAtUtc.Value > TimeSpan.FromMinutes(maximumAgeMinutes))
				return "authentication_too_old";
		}

		return null;
	}

	private static bool ValidPolicyValue(string? value, int maximumLength) =>
		value is null ||
		(!string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsWhiteSpace));

	private static bool TryReadSingleClaim(ClaimsPrincipal principal, string type, out string? value)
	{
		var values = principal.Claims
			.Where(claim => string.Equals(claim.Type, type, StringComparison.Ordinal))
			.Select(claim => claim.Value)
			.Where(claimValue => !string.IsNullOrWhiteSpace(claimValue))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		value = values.Length == 1 ? values[0] : null;
		return values.Length <= 1;
	}
}
