// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Depot.Diagnostics;
using Depot.Models;

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Depot.Services;

internal enum OidcAuthenticationStatus
{
	Succeeded,
	Cancelled,
	Failed
}

internal sealed record OidcAuthenticationResult(
	OidcAuthenticationStatus Status,
	ValidatedExternalIdentity? Identity = null,
	string? FailureCode = null)
{
	public static OidcAuthenticationResult Success(ValidatedExternalIdentity identity) => new(OidcAuthenticationStatus.Succeeded, identity);
	public static OidcAuthenticationResult Cancelled() => new(OidcAuthenticationStatus.Cancelled);
	public static OidcAuthenticationResult Failed(string code) => new(OidcAuthenticationStatus.Failed, FailureCode: code);
}

internal interface IOpenIdConnectAuthenticationClient
{
	Task<OidcAuthenticationResult> AuthenticateAsync(
		EnterpriseIdentityProvider provider,
		CancellationToken cancellationToken);
}

internal interface IOidcMetadataProvider
{
	Task<OpenIdConnectConfiguration> GetAsync(string authority, bool forceRefresh, CancellationToken cancellationToken);
}

internal interface IOidcSystemBrowser
{
	void Open(Uri uri);
}

internal interface IOidcCallbackListener : IAsyncDisposable
{
	Uri RedirectUri { get; }
	Task<OidcAuthorizationCallback> WaitAsync(CancellationToken cancellationToken);
}

internal interface IOidcCallbackListenerFactory
{
	IOidcCallbackListener Create();
}

internal sealed record OidcAuthorizationCallback(string? Code, string? State, string? Error);

internal sealed class OpenIdConnectAuthenticationClient : IOpenIdConnectAuthenticationClient
{
	private static readonly HttpClient SharedHttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(30)
	};

	private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(3);
	private readonly IOidcMetadataProvider _metadata;
	private readonly IOidcSystemBrowser _browser;
	private readonly IOidcCallbackListenerFactory _callbacks;
	private readonly HttpClient _httpClient;

	public OpenIdConnectAuthenticationClient()
		: this(new IdentityModelOidcMetadataProvider(), new SystemOidcBrowser(), new TcpOidcCallbackListenerFactory(), SharedHttpClient)
	{
	}

	internal OpenIdConnectAuthenticationClient(
		IOidcMetadataProvider metadata,
		IOidcSystemBrowser browser,
		IOidcCallbackListenerFactory callbacks,
		HttpClient httpClient)
	{
		_metadata = metadata;
		_browser = browser;
		_callbacks = callbacks;
		_httpClient = httpClient;
	}

	public async Task<OidcAuthenticationResult> AuthenticateAsync(
		EnterpriseIdentityProvider provider,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(provider);
		if (!provider.IsEnabled) return OidcAuthenticationResult.Failed("provider_disabled");
		if (!Enum.IsDefined(provider.Kind)) return OidcAuthenticationResult.Failed("provider_kind_invalid");
		if (provider.Kind == EnterpriseIdentityProviderKind.MicrosoftEntraId && string.IsNullOrWhiteSpace(provider.TenantId))
			return OidcAuthenticationResult.Failed("entra_tenant_required");

		OpenIdConnectConfiguration configuration;
		try
		{
			configuration = await _metadata.GetAsync(provider.Authority, forceRefresh: false, cancellationToken);
			if (!ValidateMetadata(configuration)) return OidcAuthenticationResult.Failed("metadata_invalid");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			LogProtocolFailure("metadata", exception);
			return OidcAuthenticationResult.Failed("metadata_unavailable");
		}

		await using var callbackListener = _callbacks.Create();
		var state = RandomUrlSafeValue(32);
		var nonce = RandomUrlSafeValue(32);
		var codeVerifier = RandomUrlSafeValue(48);
		var codeChallenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
		var authorizationUri = BuildAuthorizationUri(
			configuration.AuthorizationEndpoint,
			provider.ClientId,
			callbackListener.RedirectUri,
			state,
			nonce,
			codeChallenge);

		try
		{
			_browser.Open(authorizationUri);
		}
		catch (Exception exception)
		{
			LogProtocolFailure("browser", exception);
			return OidcAuthenticationResult.Failed("browser_launch_failed");
		}

		OidcAuthorizationCallback callback;
		using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			timeout.CancelAfter(CallbackTimeout);
			try
			{
				callback = await callbackListener.WaitAsync(timeout.Token);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (OperationCanceledException)
			{
				return OidcAuthenticationResult.Failed("callback_timeout");
			}
			catch (Exception exception)
			{
				LogProtocolFailure("callback", exception);
				return OidcAuthenticationResult.Failed("callback_invalid");
			}
		}

		if (string.Equals(callback.Error, "access_denied", StringComparison.Ordinal))
			return OidcAuthenticationResult.Cancelled();
		if (!string.IsNullOrWhiteSpace(callback.Error))
			return OidcAuthenticationResult.Failed("authorization_error");
		if (string.IsNullOrWhiteSpace(callback.Code) || string.IsNullOrWhiteSpace(callback.State))
			return OidcAuthenticationResult.Failed("authorization_response_incomplete");
		if (!FixedTimeEquals(state, callback.State))
			return OidcAuthenticationResult.Failed("state_mismatch");

		var idToken = await RedeemCodeAsync(
			configuration.TokenEndpoint,
			provider.ClientId,
			callback.Code,
			callbackListener.RedirectUri,
			codeVerifier,
			cancellationToken);
		if (idToken is null) return OidcAuthenticationResult.Failed("token_exchange_failed");

		var validation = ValidateIdToken(provider, configuration, idToken, nonce);
		if (validation.Status == OidcAuthenticationStatus.Succeeded) return validation;
		if (!string.Equals(validation.FailureCode, "signing_key_not_found", StringComparison.Ordinal)) return validation;

		try
		{
			configuration = await _metadata.GetAsync(provider.Authority, forceRefresh: true, cancellationToken);
			if (!ValidateMetadata(configuration)) return OidcAuthenticationResult.Failed("metadata_invalid");
			return ValidateIdToken(provider, configuration, idToken, nonce);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			LogProtocolFailure("metadata-refresh", exception);
			return OidcAuthenticationResult.Failed("signing_key_refresh_failed");
		}
	}

	private async Task<string?> RedeemCodeAsync(
		string tokenEndpoint,
		string clientId,
		string code,
		Uri redirectUri,
		string codeVerifier,
		CancellationToken cancellationToken)
	{
		if (!TryHttpsEndpoint(tokenEndpoint, out var endpoint)) return null;
		using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new FormUrlEncodedContent(
			[
				new("grant_type", "authorization_code"),
				new("client_id", clientId),
				new("code", code),
				new("redirect_uri", redirectUri.AbsoluteUri),
				new("code_verifier", codeVerifier)
			])
		};
		request.Headers.Accept.ParseAdd("application/json");

		try
		{
			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode) return null;
			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
			return document.RootElement.TryGetProperty("id_token", out var value) && value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			LogProtocolFailure("token", exception);
			return null;
		}
	}

	private static OidcAuthenticationResult ValidateIdToken(
		EnterpriseIdentityProvider provider,
		OpenIdConnectConfiguration configuration,
		string idToken,
		string expectedNonce)
	{
		var algorithms = configuration.IdTokenSigningAlgValuesSupported
			.Where(value => !string.Equals(value, SecurityAlgorithms.None, StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		if (algorithms.Length == 0 || configuration.SigningKeys.Count == 0)
			return OidcAuthenticationResult.Failed("signing_configuration_missing");

		try
		{
			var parameters = new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidIssuer = configuration.Issuer,
				ValidateAudience = true,
				ValidAudience = provider.ClientId,
				ValidateLifetime = true,
				RequireExpirationTime = true,
				RequireSignedTokens = true,
				ValidateIssuerSigningKey = true,
				IssuerSigningKeys = configuration.SigningKeys,
				ValidAlgorithms = algorithms,
				ClockSkew = TimeSpan.FromMinutes(2)
			};
			var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
			var principal = handler.ValidateToken(idToken, parameters, out var validatedToken);
			if (validatedToken is not JwtSecurityToken jwt)
				return OidcAuthenticationResult.Failed("id_token_invalid");
			if (!string.Equals(jwt.Issuer, configuration.Issuer, StringComparison.Ordinal))
				return OidcAuthenticationResult.Failed("issuer_mismatch");

			var nonce = Claim(principal, "nonce");
			if (nonce is null || !FixedTimeEquals(expectedNonce, nonce))
				return OidcAuthenticationResult.Failed("nonce_mismatch");
			var subject = Claim(principal, "sub");
			if (string.IsNullOrWhiteSpace(subject))
				return OidcAuthenticationResult.Failed("subject_missing");

			var tenantId = Claim(principal, "tid") ?? Claim(principal, "tenant_id");
			if (provider.Kind == EnterpriseIdentityProviderKind.MicrosoftEntraId &&
				(string.IsNullOrWhiteSpace(tenantId) ||
				 !string.Equals(provider.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)))
				return OidcAuthenticationResult.Failed("tenant_mismatch");

			return OidcAuthenticationResult.Success(new ValidatedExternalIdentity(
				provider.Code,
				jwt.Issuer,
				subject,
				tenantId,
				Claim(principal, "email"),
				Claim(principal, "name")));
		}
		catch (SecurityTokenSignatureKeyNotFoundException)
		{
			return OidcAuthenticationResult.Failed("signing_key_not_found");
		}
		catch (SecurityTokenException exception)
		{
			LogProtocolFailure("id-token", exception);
			return OidcAuthenticationResult.Failed("id_token_invalid");
		}
		catch (ArgumentException exception)
		{
			LogProtocolFailure("id-token", exception);
			return OidcAuthenticationResult.Failed("id_token_invalid");
		}
	}

	private static bool ValidateMetadata(OpenIdConnectConfiguration configuration)
	{
		if (configuration is null || !TryHttpsEndpoint(configuration.Issuer, out _)) return false;
		if (!TryHttpsEndpoint(configuration.AuthorizationEndpoint, out _)) return false;
		if (!TryHttpsEndpoint(configuration.TokenEndpoint, out _)) return false;
		if (configuration.CodeChallengeMethodsSupported.Count > 0 &&
			!configuration.CodeChallengeMethodsSupported.Contains("S256", StringComparer.Ordinal)) return false;
		return true;
	}

	private static Uri BuildAuthorizationUri(
		string authorizationEndpoint,
		string clientId,
		Uri redirectUri,
		string state,
		string nonce,
		string codeChallenge)
	{
		if (!TryHttpsEndpoint(authorizationEndpoint, out var endpoint))
			throw new InvalidOperationException("OIDC authorization endpoint must use HTTPS.");
		var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client_id"] = clientId,
			["response_type"] = "code",
			["response_mode"] = "query",
			["redirect_uri"] = redirectUri.AbsoluteUri,
			["scope"] = "openid profile email",
			["state"] = state,
			["nonce"] = nonce,
			["code_challenge"] = codeChallenge,
			["code_challenge_method"] = "S256"
		};
		var builder = new UriBuilder(endpoint);
		var existing = builder.Query.TrimStart('?');
		var encoded = string.Join("&", parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
		builder.Query = string.IsNullOrWhiteSpace(existing) ? encoded : $"{existing}&{encoded}";
		return builder.Uri;
	}

	private static string RandomUrlSafeValue(int byteCount) => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(byteCount));

	private static string? Claim(ClaimsPrincipal principal, string type) =>
		principal.Claims.FirstOrDefault(claim => string.Equals(claim.Type, type, StringComparison.Ordinal))?.Value;

	private static bool FixedTimeEquals(string expected, string actual)
	{
		var left = Encoding.UTF8.GetBytes(expected);
		var right = Encoding.UTF8.GetBytes(actual);
		return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
	}

	private static bool TryHttpsEndpoint(string? value, out Uri uri)
	{
		if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
			string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
			string.IsNullOrEmpty(parsed.UserInfo) &&
			string.IsNullOrEmpty(parsed.Fragment))
		{
			uri = parsed;
			return true;
		}
		uri = null!;
		return false;
	}

	private static void LogProtocolFailure(string stage, Exception exception) =>
		StartupDiagnostics.Log($"Enterprise OIDC {stage} failure: {exception.GetType().Name}.");
}

internal sealed class IdentityModelOidcMetadataProvider : IOidcMetadataProvider
{
	private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new(StringComparer.Ordinal);
	private readonly object _gate = new();

	public Task<OpenIdConnectConfiguration> GetAsync(string authority, bool forceRefresh, CancellationToken cancellationToken)
	{
		ConfigurationManager<OpenIdConnectConfiguration> manager;
		lock (_gate)
		{
			if (!_managers.TryGetValue(authority, out manager!))
			{
				var metadataAddress = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
				manager = new ConfigurationManager<OpenIdConnectConfiguration>(
					metadataAddress,
					new OpenIdConnectConfigurationRetriever(),
					new HttpDocumentRetriever { RequireHttps = true });
				_managers.Add(authority, manager);
			}
		}
		if (forceRefresh) manager.RequestRefresh();
		return manager.GetConfigurationAsync(cancellationToken);
	}
}

internal sealed class SystemOidcBrowser : IOidcSystemBrowser
{
	public void Open(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
	}
}

internal sealed class TcpOidcCallbackListenerFactory : IOidcCallbackListenerFactory
{
	public IOidcCallbackListener Create() => new TcpOidcCallbackListener();
}

internal sealed class TcpOidcCallbackListener : IOidcCallbackListener
{
	private readonly TcpListener _listener;
	private bool _disposed;

	public TcpOidcCallbackListener()
	{
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start(1);
		var endpoint = (IPEndPoint)_listener.LocalEndpoint;
		RedirectUri = new Uri($"http://localhost:{endpoint.Port}/", UriKind.Absolute);
	}

	public Uri RedirectUri { get; }

	public async Task<OidcAuthorizationCallback> WaitAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
		var requestLine = await reader.ReadLineAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(requestLine)) throw new InvalidDataException("OIDC callback request line is missing.");
		var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 3 || !string.Equals(parts[0], "GET", StringComparison.Ordinal) || parts[1].Length > 8192)
			throw new InvalidDataException("OIDC callback request is invalid.");
		for (var index = 0; index < 64; index++)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null || line.Length > 8192) throw new InvalidDataException("OIDC callback headers are invalid.");
			if (line.Length == 0) break;
			if (index == 63) throw new InvalidDataException("OIDC callback contains too many headers.");
		}

		var callbackUri = parts[1].StartsWith('/', StringComparison.Ordinal)
			? new Uri(RedirectUri, parts[1])
			: new Uri(parts[1], UriKind.Absolute);
		if (!string.Equals(callbackUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || callbackUri.Port != RedirectUri.Port)
			throw new InvalidDataException("OIDC callback target does not match the loopback listener.");
		var values = ParseQuery(callbackUri.Query);
		await WriteResponseAsync(stream, cancellationToken);
		return new OidcAuthorizationCallback(
			values.GetValueOrDefault("code"),
			values.GetValueOrDefault("state"),
			values.GetValueOrDefault("error"));
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed) return ValueTask.CompletedTask;
		_disposed = true;
		_listener.Stop();
		return ValueTask.CompletedTask;
	}

	private static Dictionary<string, string> ParseQuery(string query)
	{
		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = pair.IndexOf('=');
			var rawName = separator < 0 ? pair : pair[..separator];
			var rawValue = separator < 0 ? string.Empty : pair[(separator + 1)..];
			var name = Uri.UnescapeDataString(rawName.Replace('+', ' '));
			var value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
			if (!values.TryAdd(name, value)) throw new InvalidDataException("OIDC callback contains duplicate parameters.");
		}
		return values;
	}

	private static async Task WriteResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		const string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Depot sign-in</title></head><body><p>Sign-in completed. You can return to Depot and close this browser tab.</p></body></html>";
		var body = Encoding.UTF8.GetBytes(html);
		var headers = Encoding.ASCII.GetBytes(
			"HTTP/1.1 200 OK\r\n" +
			"Content-Type: text/html; charset=utf-8\r\n" +
			"Cache-Control: no-store\r\n" +
			"Pragma: no-cache\r\n" +
			$"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n" +
			"Connection: close\r\n\r\n");
		await stream.WriteAsync(headers, cancellationToken);
		await stream.WriteAsync(body, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}
}
