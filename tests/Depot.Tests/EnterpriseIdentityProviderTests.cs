// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class EnterpriseIdentityProviderTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SqliteMigratesAndPersistsEnterpriseIdentity()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-enterprise-provider-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			await VerifyProviderContractAsync(factory, new DepotDatabase(factory));
		}
		finally
		{
			Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public async Task SqlServerMigratesAndPersistsEnterpriseIdentity()
	{
		var settings = ProcurementProviderConfiguration.GetSqlServerSettings();
		var factory = new SqlServerConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new SqlServerDatabase(factory));
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public async Task MariaDbMigratesAndPersistsEnterpriseIdentity()
	{
		var settings = ProcurementProviderConfiguration.GetMariaDbSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public async Task MySqlMigratesAndPersistsEnterpriseIdentity()
	{
		var settings = ProcurementProviderConfiguration.GetMySqlSettings();
		var factory = new MySqlConnectionFactory(settings);
		await VerifyProviderContractAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyProviderContractAsync(
		IDatabaseConnectionFactory factory,
		IDatabaseInitializer initializer)
	{
		initializer.Initialize();
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(access);
		var users = new UserRepository(access);
		var identities = new EnterpriseIdentityRepository(access);
		var nonce = Guid.NewGuid().ToString("N");
		var user = new User
		{
			Email = $"enterprise-provider-{nonce}@test.local",
			DisplayName = "Enterprise Identity Provider Test",
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};
		var userId = await users.CreateAsync(user, "unused", CancellationToken.None);
		var provider = new EnterpriseIdentityProvider
		{
			Code = $"oidc-{nonce}",
			Kind = EnterpriseIdentityProviderKind.OpenIdConnect,
			DisplayName = "Provider Acceptance",
			Authority = "https://identity.example.test",
			ClientId = $"client-{nonce}",
			IsEnabled = true,
			CreatedUtc = DateTime.UtcNow,
			UpdatedUtc = DateTime.UtcNow
		};
		provider = await transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await identities.CreateProviderAsync(transaction, provider, token);
			return provider with { Id = id };
		});
		var subject = $"subject-{nonce}";
		var identityKey = Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes($"{provider.Code}\0https://issuer.example.test\0{subject}")))
			.ToLowerInvariant();
		var link = new ExternalIdentityLink
		{
			ProviderId = provider.Id,
			ProviderCode = provider.Code,
			UserId = userId,
			Issuer = "https://issuer.example.test",
			Subject = subject,
			IdentityKeySha256 = identityKey,
			Email = user.Email,
			DisplayName = user.DisplayName,
			LinkedUtc = DateTime.UtcNow
		};
		link = await transactions.ExecuteAsync(async (transaction, token) =>
		{
			var id = await identities.CreateLinkAsync(transaction, link, token);
			return link with { Id = id };
		});

		var storedProvider = await identities.GetProviderByCodeAsync(provider.Code, CancellationToken.None);
		var storedLink = await identities.GetLinkByIdentityKeyAsync(identityKey, CancellationToken.None);
		Assert.NotNull(storedProvider);
		Assert.Equal(provider.Id, storedProvider!.Id);
		Assert.True(storedProvider.IsEnabled);
		Assert.NotNull(storedLink);
		Assert.Equal(link.Id, storedLink!.Id);
		Assert.Equal(userId, storedLink.UserId);
		Assert.Equal(subject, storedLink.Subject);
		Assert.Equal(identityKey, storedLink.IdentityKeySha256);
	}
}
