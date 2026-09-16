// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;

using Depot.Models;
using Depot.Services;

using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Depot.Tests;

public sealed class OpenIdConnectAuthenticationClientTests : IDisposable
{
	private readonly RSA _rsa = RSA.Create(2048);
	private readonly RsaSecurityKey _signingKey;

	public OpenIdConnectAuthenticationClientTests()
	{
		_signingKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
	}

	[Fact]
	public async Task ValidAuthorizationCodeFlowUsesPkceAndReturnsValidatedIdentity()
	{
		var browser = new CapturingBrowser();
		var callback = new CallbackFactory(browser, state => new OidcAuthorizationCallback("authorization-code", state, null));
		var tokenEndpoint = new TokenEndpointHandler(request =>
		{
			Assert.NotNull(browser.LastUri);
			var query = ParseQuery(browser.LastUri!.Query);
			Assert.Equal("S256", query["code_challenge_method"]);
			Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
			Assert.Contains("openid", query["scope"], StringComparison.Ordinal);
			return CreateToken("https://issuer.example.test", "depot-client", query["nonce"], "subject-1", null, "external@example.test", "External User");
		});
		var client = CreateClient(browser, callback, tokenEndpoint);
		var provider = Provider(EnterpriseIdentityProviderKind.OpenIdConnect, tenantId: null);

		var result = await client.AuthenticateAsync(provider, CancellationToken.None);

		Assert.Equal(OidcAuthenticationStatus.Succeeded, result.Status);
		Assert.NotNull(result.Identity);
		Assert.Equal("oidc-main", result.Identity!.ProviderCode);
		Assert.Equal("https://issuer.example.test", result.Identity.Issuer);
		Assert.Equal("subject-1", result.Identity.Subject);
		Assert.Equal("external@example.test", result.Identity.Email);
		Assert.Equal("External User", result.Identity.DisplayName);
		Assert.True(tokenEndpoint.Called);
		Assert.Contains("code_verifier=", tokenEndpoint.LastBody, StringComparison.Ordinal);
		Assert.DoesNotContain("client_secret", tokenEndpoint.LastBody, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("grant_type=authorization_code", tokenEndpoint.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task StateMismatchFailsBeforeTokenExchange()
	{
		var browser = new CapturingBrowser();
		var callback = new CallbackFactory(browser, _ => new OidcAuthorizationCallback("code", "wrong-state", null));
		var tokenEndpoint = new TokenEndpointHandler(_ => throw new InvalidOperationException("Token endpoint must not be called."));
		var client = CreateClient(browser, callback, tokenEndpoint);

		var result = await client.AuthenticateAsync(Provider(EnterpriseIdentityProviderKind.OpenIdConnect, null), CancellationToken.None);

		Assert.Equal(OidcAuthenticationStatus.Failed, result.Status);
		Assert.Equal("state_mismatch", result.FailureCode);
		Assert.False(tokenEndpoint.Called);
	}

	[Fact]
	public async Task NonceMismatchFailsClosed()
	{
		var browser = new CapturingBrowser();
		var callback = new CallbackFactory(browser, state => new OidcAuthorizationCallback("code", state, null));
		var tokenEndpoint = new TokenEndpointHandler(_ =>
			CreateToken("https://issuer.example.test", "depot-client", "wrong-nonce", "subject-2", null, null, null));
		var client = CreateClient(browser, callback, tokenEndpoint);

		var result = await client.AuthenticateAsync(Provider(EnterpriseIdentityProviderKind.OpenIdConnect, null), CancellationToken.None);

		Assert.Equal(OidcAuthenticationStatus.Failed, result.Status);
		Assert.Equal("nonce_mismatch", result.FailureCode);
	}

	[Fact]
	public async Task InvalidAudienceFailsClosed()
	{
		var browser = new CapturingBrowser();
		var callback = new CallbackFactory(browser, state => new OidcAuthorizationCallback("code", state, null));
		var tokenEndpoint = new TokenEndpointHandler(_ =>
		{
			var nonce = ParseQuery(browser.LastUri!.Query)["nonce"];
			return CreateToken("https://issuer.example.test", "another-client", nonce, "subject-3", null, null, null);
		});
		var client = CreateClient(browser, callback, tokenEndpoint);

		var result = await client.AuthenticateAsync(Provider(EnterpriseIdentityProviderKind.OpenIdConnect, null), CancellationToken.None);

		Assert.Equal(OidcAuthenticationStatus.Failed, result.Status);
		Assert.Equal("id_token_invalid", result.FailureCode);
	}

	[Fact]
	public async Task EntraTenantMustMatchConfiguredTenant()
	{
		var browser = new CapturingBrowser();
		var callback = new CallbackFactory(browser, state => new OidcAuthorizationCallback("code", state, null));
		var tokenEndpoint = new TokenEndpointHandler(_ =>
		{
			var nonce = ParseQuery(browser.LastUri!.Query)["nonce"];
			return CreateToken("https://issuer.example.test", "depot-client", nonce, "subject-4", "tenant-b", null, null);
		});
		var client = CreateClient(browser, callback, tokenEndpoint);

		var result = await client.AuthenticateAsync(Provider(EnterpriseIdentityProviderKind.MicrosoftEntraId, "tenant-a"), CancellationToken.None);

		Assert.Equal(OidcAuthenticationStatus.Failed, result.Status);
		Assert.Equal("tenant_mismatch", result.FailureCode);
	}

	[Fact]
	public async Task AccessDeniedIsTreatedAsUserCancellation()
	{
		var browser = new CapturingBrowser();
		var callback = new CallbackFactory(browser, state => new OidcAuthorizationCallback(null, state, "access_denied"));
		var tokenEndpoint = new TokenEndpointHandler(_ => throw new InvalidOperationException("Token endpoint must not be called."));
		var client = CreateClient(browser, callback, tokenEndpoint);

		var result = await client.AuthenticateAsync(Provider(EnterpriseIdentityProviderKind.OpenIdConnect, null), CancellationToken.None);

		Assert.Equal(OidcAuthenticationStatus.Cancelled, result.Status);
		Assert.False(tokenEndpoint.Called);
	}

	private OpenIdConnectAuthenticationClient CreateClient(
		CapturingBrowser browser,
		CallbackFactory callback,
		TokenEndpointHandler tokenEndpoint)
	{
		var configuration = new OpenIdConnectConfiguration
		{
			Issuer = "https://issuer.example.test",
			AuthorizationEndpoint = "https://issuer.example.test/authorize",
			TokenEndpoint = "https://issuer.example.test/token"
		};
		configuration.SigningKeys.Add(_signingKey);
		configuration.IdTokenSigningAlgValuesSupported.Add(SecurityAlgorithms.RsaSha256);
		configuration.CodeChallengeMethodsSupported.Add("S256");
		return new OpenIdConnectAuthenticationClient(
			new StaticMetadataProvider(configuration),
			browser,
			callback,
			new HttpClient(tokenEndpoint));
	}

	private EnterpriseIdentityProvider Provider(EnterpriseIdentityProviderKind kind, string? tenantId) => new()
	{
		Id = 1,
		Code = "oidc-main",
		Kind = kind,
		DisplayName = "Organization",
		Authority = "https://issuer.example.test",
		ClientId = "depot-client",
		TenantId = tenantId,
		IsEnabled = true,
		CreatedUtc = DateTime.UtcNow,
		UpdatedUtc = DateTime.UtcNow
	};

	private string CreateToken(
		string issuer,
		string audience,
		string nonce,
		string subject,
		string? tenantId,
		string? email,
		string? name)
	{
		var claims = new List<Claim>
		{
			new("sub", subject),
			new("nonce", nonce)
		};
		if (tenantId is not null) claims.Add(new Claim("tid", tenantId));
		if (email is not null) claims.Add(new Claim("email", email));
		if (name is not null) claims.Add(new Claim("name", name));
		var token = new JwtSecurityToken(
			issuer,
			audience,
			claims,
			DateTime.UtcNow.AddMinutes(-1),
			DateTime.UtcNow.AddMinutes(5),
			new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256));
		return new JwtSecurityTokenHandler().WriteToken(token);
	}

	private static Dictionary<string, string> ParseQuery(string query) =>
		query.TrimStart('?')
			.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.ToDictionary(
				parts => Uri.UnescapeDataString(parts[0]),
				parts => Uri.UnescapeDataString(parts.Length == 2 ? parts[1] : string.Empty),
				StringComparer.Ordinal);

	public void Dispose() => _rsa.Dispose();

	private sealed class StaticMetadataProvider(OpenIdConnectConfiguration configuration) : IOidcMetadataProvider
	{
		public Task<OpenIdConnectConfiguration> GetAsync(string authority, bool forceRefresh, CancellationToken cancellationToken) =>
			Task.FromResult(configuration);
	}

	private sealed class CapturingBrowser : IOidcSystemBrowser
	{
		public Uri? LastUri { get; private set; }
		public void Open(Uri uri) => LastUri = uri;
	}

	private sealed class CallbackFactory(
		CapturingBrowser browser,
		Func<string, OidcAuthorizationCallback> callbackFactory) : IOidcCallbackListenerFactory
	{
		public IOidcCallbackListener Create() => new Listener(browser, callbackFactory);

		private sealed class Listener(
			CapturingBrowser browser,
			Func<string, OidcAuthorizationCallback> callbackFactory) : IOidcCallbackListener
		{
			public Uri RedirectUri { get; } = new("http://localhost:45678/");

			public Task<OidcAuthorizationCallback> WaitAsync(CancellationToken cancellationToken)
			{
				var uri = browser.LastUri ?? throw new InvalidOperationException("Browser was not opened before callback wait.");
				var state = ParseQuery(uri.Query)["state"];
				return Task.FromResult(callbackFactory(state));
			}

			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}

	private sealed class TokenEndpointHandler(Func<HttpRequestMessage, string> tokenFactory) : HttpMessageHandler
	{
		public bool Called { get; private set; }
		public string LastBody { get; private set; } = string.Empty;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Called = true;
			LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
			var token = tokenFactory(request);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent($"{{\"id_token\":\"{token}\"}}", System.Text.Encoding.UTF8, "application/json")
			};
		}
	}
}
