// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Diagnostics;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed record EnterpriseLoginProviderOption(
	string Code,
	string DisplayName,
	EnterpriseIdentityProviderKind Kind);

public enum EnterpriseSignInStatus
{
	Succeeded,
	Cancelled,
	Failed,
	IdentityNotLinked,
	SessionLimitExceeded
}

public sealed record EnterpriseSignInResult(
	EnterpriseSignInStatus Status,
	string? ProviderCode = null,
	string? FailureCode = null)
{
	public bool Succeeded => Status == EnterpriseSignInStatus.Succeeded;
}

public sealed class EnterpriseAuthenticationService
{
	private readonly EnterpriseIdentityRepository _providers;
	private readonly IEnterpriseIdentityResolver _identityResolver;
	private readonly IOpenIdConnectAuthenticationClient _oidcClient;
	private readonly SessionService _sessions;
	private readonly AuthorizationService _authorization;
	private readonly SecurityEventService _securityEvents;
	private readonly SecurityEventRepository _securityEventRepository;
	private readonly TimeProvider _timeProvider;

	public EnterpriseAuthenticationService(
		EnterpriseIdentityRepository providers,
		IEnterpriseIdentityResolver identityResolver,
		SessionService sessions,
		AuthorizationService authorization,
		SecurityEventService securityEvents,
		SecurityEventRepository securityEventRepository,
		TimeProvider? timeProvider = null)
		: this(
			providers,
			identityResolver,
			new OpenIdConnectAuthenticationClient(),
			sessions,
			authorization,
			securityEvents,
			securityEventRepository,
			timeProvider)
	{
	}

	internal EnterpriseAuthenticationService(
		EnterpriseIdentityRepository providers,
		IEnterpriseIdentityResolver identityResolver,
		IOpenIdConnectAuthenticationClient oidcClient,
		SessionService sessions,
		AuthorizationService authorization,
		SecurityEventService securityEvents,
		SecurityEventRepository securityEventRepository,
		TimeProvider? timeProvider = null)
	{
		_providers = providers;
		_identityResolver = identityResolver;
		_oidcClient = oidcClient;
		_sessions = sessions;
		_authorization = authorization;
		_securityEvents = securityEvents;
		_securityEventRepository = securityEventRepository;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public async Task<IReadOnlyList<EnterpriseLoginProviderOption>> ListLoginProvidersAsync(
		CancellationToken cancellationToken = default)
	{
		var providers = await _providers.ListProvidersAsync(cancellationToken);
		return providers
			.Where(IsSupportedLoginProvider)
			.OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(provider => provider.Code, StringComparer.Ordinal)
			.Select(provider => new EnterpriseLoginProviderOption(provider.Code, provider.DisplayName, provider.Kind))
			.ToArray();
	}

	public async Task<EnterpriseSignInResult> SignInAsync(
		string providerCode,
		CancellationToken cancellationToken = default)
	{
		var normalizedCode = NormalizeProviderCode(providerCode);
		EnterpriseIdentityProvider? provider;
		try
		{
			provider = await _providers.GetProviderByCodeAsync(normalizedCode, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.Log($"Enterprise authentication configuration lookup failed: {exception.GetType().Name}.");
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, normalizedCode, "provider_lookup_failed");
		}
		if (provider is null || !IsSupportedLoginProvider(provider))
		{
			await RecordFailureAsync(normalizedCode, "provider_unavailable", cancellationToken);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, normalizedCode, "provider_unavailable");
		}

		OidcAuthenticationResult protocolResult;
		try
		{
			protocolResult = await _oidcClient.AuthenticateAsync(provider, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.Log($"Enterprise authentication provider failure: {exception.GetType().Name}.");
			await RecordFailureAsync(provider.Code, "provider_execution_failed", cancellationToken);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, provider.Code, "provider_execution_failed");
		}

		if (protocolResult.Status == OidcAuthenticationStatus.Cancelled)
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Cancelled, provider.Code);
		if (protocolResult.Status != OidcAuthenticationStatus.Succeeded || protocolResult.Identity is null)
		{
			var failureCode = NormalizeFailureCode(protocolResult.FailureCode);
			await RecordFailureAsync(provider.Code, failureCode, cancellationToken);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, provider.Code, failureCode);
		}
		if (!string.Equals(protocolResult.Identity.ProviderCode, provider.Code, StringComparison.Ordinal))
		{
			await RecordFailureAsync(provider.Code, "provider_identity_mismatch", cancellationToken);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, provider.Code, "provider_identity_mismatch");
		}

		EnterpriseIdentityResolution? resolution;
		try
		{
			resolution = await _identityResolver.RecordSuccessfulAuthenticationAsync(protocolResult.Identity, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.Log($"Enterprise identity resolution failure: {exception.GetType().Name}.");
			await RecordFailureAsync(provider.Code, "identity_resolution_failed", cancellationToken);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, provider.Code, "identity_resolution_failed");
		}

		if (resolution is null)
		{
			await RecordFailureAsync(provider.Code, "identity_not_linked", cancellationToken);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.IdentityNotLinked, provider.Code, "identity_not_linked");
		}

		try
		{
			await _sessions.StartAuthenticatedSessionAsync(resolution.User.Id, cancellationToken);
		}
		catch (SessionLimitExceededException)
		{
			await RecordFailureAsync(provider.Code, "session_limit_exceeded", cancellationToken, resolution.User.Id);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.SessionLimitExceeded, provider.Code, "session_limit_exceeded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.Log($"Enterprise session start failure: {exception.GetType().Name}.");
			await RecordFailureAsync(provider.Code, "session_start_failed", cancellationToken, resolution.User.Id);
			return new EnterpriseSignInResult(EnterpriseSignInStatus.Failed, provider.Code, "session_start_failed");
		}

		_authorization.SignIn(resolution.User, resolution.User.EffectivePermissions);
		await _securityEvents.RecordAuthenticationSuccessAsync(
			resolution.User,
			0,
			_sessions.CurrentSessionId,
			_sessions.CurrentClientInstanceId,
			_sessions.CurrentMachineName,
			cancellationToken);
		return new EnterpriseSignInResult(EnterpriseSignInStatus.Succeeded, provider.Code);
	}

	private static bool IsSupportedLoginProvider(EnterpriseIdentityProvider provider) =>
		provider.IsEnabled &&
		provider.Kind switch
		{
			EnterpriseIdentityProviderKind.OpenIdConnect => true,
			EnterpriseIdentityProviderKind.MicrosoftEntraId => !string.IsNullOrWhiteSpace(provider.TenantId),
			_ => false
		};

	private async Task RecordFailureAsync(
		string providerCode,
		string failureCode,
		CancellationToken cancellationToken,
		long? userId = null)
	{
		var securityEvent = new SecurityEvent
		{
			TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime,
			EventType = SecurityEventType.AuthenticationFailed,
			Severity = SecurityEventSeverity.Information,
			UserId = userId,
			AccountIdentifier = $"enterprise:{providerCode}",
			Summary = "Enterprise authentication failed",
			Details = $"Provider '{providerCode}' rejected the sign-in at boundary '{failureCode}'."
		};
		try
		{
			securityEvent.Id = await _securityEventRepository.CreateAsync(securityEvent, cancellationToken);
			await _securityEvents.NotifyPersistedAsync(securityEvent, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
		}
	}

	private static string NormalizeProviderCode(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Enterprise identity provider code is required.", nameof(value));
		var normalized = value.Trim().ToLowerInvariant();
		if (normalized.Length > 64 ||
			!char.IsLetterOrDigit(normalized[0]) ||
			normalized.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
			throw new ArgumentException("Enterprise identity provider code is invalid.", nameof(value));
		return normalized;
	}

	private static string NormalizeFailureCode(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "protocol_failed";
		var normalized = value.Trim().ToLowerInvariant();
		return normalized.Length <= 64 && normalized.All(character => char.IsLetterOrDigit(character) || character is '_' or '-')
			? normalized
			: "protocol_failed";
	}
}
