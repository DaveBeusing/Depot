// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class EnterpriseIdentityAssuranceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-enterprise-assurance-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task SchemaTwoPersistsProviderAssurancePolicy()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var repository = new EnterpriseIdentityRepository(access);
		var transactions = new DatabaseTransactionRunner(access);
		var now = DateTime.UtcNow;
		var provider = new EnterpriseIdentityProvider
		{
			Code = "entra-assured",
			Kind = EnterpriseIdentityProviderKind.MicrosoftEntraId,
			DisplayName = "Assured Entra",
			Authority = "https://login.microsoftonline.com/tenant-a/v2.0",
			ClientId = "depot-client",
			TenantId = "tenant-a",
			RequiredAmr = "mfa",
			RequiredAcr = "urn:example:loa:2",
			MaximumAuthenticationAgeMinutes = 30,
			IsEnabled = true,
			CreatedUtc = now,
			UpdatedUtc = now
		};

		provider = await transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await repository.CreateProviderAsync(transaction, provider, token);
			return provider with { Id = id };
		});
		var stored = await repository.GetProviderByIdAsync(provider.Id, CancellationToken.None);

		Assert.NotNull(stored);
		Assert.Equal("mfa", stored!.RequiredAmr);
		Assert.Equal("urn:example:loa:2", stored.RequiredAcr);
		Assert.Equal(30, stored.MaximumAuthenticationAgeMinutes);
		using var connection = factory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name='EnterpriseIdentity';";
		Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
	}

	[Fact]
	public void SchemaOneMigratesToTwoWithoutChangingExistingProviderIdentity()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		using (var connection = factory.CreateConnection())
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = """
			CREATE TABLE IF NOT EXISTS DepotFeatureVersions (Name TEXT PRIMARY KEY, Version INTEGER NOT NULL);
			CREATE TABLE EnterpriseIdentityProviders (
				Id INTEGER PRIMARY KEY AUTOINCREMENT,
				Code TEXT NOT NULL,
				Kind INTEGER NOT NULL,
				DisplayName TEXT NOT NULL,
				Authority TEXT NOT NULL,
				ClientId TEXT NOT NULL,
				TenantId TEXT NULL,
				IsEnabled INTEGER NOT NULL,
				CreatedUtc TEXT NOT NULL,
				UpdatedUtc TEXT NOT NULL,
				Version INTEGER NOT NULL DEFAULT 1,
				UNIQUE(Code));
			INSERT INTO EnterpriseIdentityProviders
				(Code, Kind, DisplayName, Authority, ClientId, TenantId, IsEnabled, CreatedUtc, UpdatedUtc, Version)
			VALUES
				('legacy-oidc', 1, 'Legacy OIDC', 'https://issuer.example.test', 'legacy-client', NULL, 1, '2026-09-16T12:00:00.0000000Z', '2026-09-16T12:00:00.0000000Z', 1);
			INSERT INTO DepotFeatureVersions (Name, Version) VALUES ('EnterpriseIdentity', 1)
			ON CONFLICT(Name) DO UPDATE SET Version=excluded.Version;
			""";
			command.ExecuteNonQuery();
		}

		EnterpriseIdentitySchemaMigration.Migrate(factory);

		using var verified = factory.CreateConnection();
		verified.Open();
		using var verify = verified.CreateCommand();
		verify.CommandText = "SELECT Code, RequiredAmr, RequiredAcr, MaximumAuthenticationAgeMinutes FROM EnterpriseIdentityProviders WHERE Code='legacy-oidc';";
		using var reader = verify.ExecuteReader();
		Assert.True(reader.Read());
		Assert.Equal("legacy-oidc", reader.GetString(0));
		Assert.True(reader.IsDBNull(1));
		Assert.True(reader.IsDBNull(2));
		Assert.True(reader.IsDBNull(3));
		reader.Close();
		verify.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name='EnterpriseIdentity';";
		Assert.Equal(2L, Convert.ToInt64(verify.ExecuteScalar(), CultureInfo.InvariantCulture));
	}

	[Theory]
	[InlineData("authentication_method_requirement_not_met", "pwd", "urn:example:loa:2", -5, "depot-client")]
	[InlineData("authentication_context_requirement_not_met", "mfa", "urn:example:loa:1", -5, "depot-client")]
	[InlineData("authentication_too_old", "mfa", "urn:example:loa:2", -61, "depot-client")]
	[InlineData("authorized_party_mismatch", "mfa", "urn:example:loa:2", -5, "another-client")]
	public void AssurancePolicyFailsClosedAtExpectedBoundary(
		string expectedFailure,
		string amr,
		string acr,
		int authenticationAgeMinutes,
		string authorizedParty)
	{
		var provider = PolicyProvider();
		var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
		var evidence = new ExternalAuthenticationAssuranceEvidence(
			[amr],
			acr,
			now.AddMinutes(authenticationAgeMinutes),
			authorizedParty,
			["depot-client", "secondary-audience"]);

		var failure = ExternalAuthenticationAssurance.Validate(provider, evidence, now);

		Assert.Equal(expectedFailure, failure);
	}

	[Fact]
	public void MatchingAssuranceEvidencePassesWithoutCreatingAuthorizationClaims()
	{
		var provider = PolicyProvider();
		var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
		var evidence = new ExternalAuthenticationAssuranceEvidence(
			["pwd", "mfa"],
			"urn:example:loa:2",
			now.AddMinutes(-10),
			"depot-client",
			["depot-client", "secondary-audience"]);

		Assert.Null(ExternalAuthenticationAssurance.Validate(provider, evidence, now));
	}

	private static EnterpriseIdentityProvider PolicyProvider() => new()
	{
		Code = "oidc-main",
		Kind = EnterpriseIdentityProviderKind.OpenIdConnect,
		DisplayName = "OIDC Main",
		Authority = "https://issuer.example.test",
		ClientId = "depot-client",
		RequiredAmr = "mfa",
		RequiredAcr = "urn:example:loa:2",
		MaximumAuthenticationAgeMinutes = 60,
		IsEnabled = true,
		CreatedUtc = DateTime.UtcNow,
		UpdatedUtc = DateTime.UtcNow
	};

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
