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

public sealed class EnterpriseIdentityTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-enterprise-identity-{Guid.NewGuid():N}.db");

	[Fact]
	public void FeatureSchemaIsCreatedAndMigrationIsIdempotent()
	{
		var factory = InitializeCore();
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		EnterpriseIdentitySchemaMigration.Migrate(factory);

		using var connection = Open();
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='EnterpriseIdentityProviders';"));
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ExternalIdentityLinks';"));
		Assert.Equal(EnterpriseIdentitySchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='EnterpriseIdentity';"));
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_ExternalIdentityLinks_User';"));
	}

	[Fact]
	public void NewerFeatureSchemaFailsClosed()
	{
		var factory = InitializeCore();
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		using (var connection = Open())
		{
			Execute(connection, $"UPDATE DepotFeatureVersions SET Version={EnterpriseIdentitySchemaMigration.CurrentVersion + 1} WHERE Name='EnterpriseIdentity';");
		}

		var error = Assert.Throws<InvalidOperationException>(() => EnterpriseIdentitySchemaMigration.Migrate(factory));
		Assert.Contains("newer than the supported version", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AssurancePolicyConfigurationIsPermissionedVersionedAndAudited()
	{
		var context = await CreateContextAsync();
		var provider = await context.Service.CreateProviderAsync(
			"oidc-assurance",
			EnterpriseIdentityProviderKind.OpenIdConnect,
			"OIDC Assurance",
			"https://identity.example.test",
			"depot-client",
			null,
			true);

		var updated = await context.Service.ConfigureAssurancePolicyAsync(
			provider.Id,
			provider.Version,
			"mfa",
			"urn:example:loa:2",
			30);

		Assert.Equal(provider.Version + 1, updated.Version);
		Assert.Equal("mfa", updated.RequiredAmr);
		Assert.Equal("urn:example:loa:2", updated.RequiredAcr);
		Assert.Equal(30, updated.MaximumAuthenticationAgeMinutes);
		var history = await context.AuditEntries.GetEntityHistoryAsync(
			nameof(EnterpriseIdentityProvider),
			provider.Id,
			10,
			CancellationToken.None);
		Assert.Contains(history, entry =>
			entry.Action == "Updated" &&
			entry.AfterJson is not null &&
			entry.AfterJson.Contains("\"requiredAmr\":\"mfa\"", StringComparison.Ordinal));

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.Service.ConfigureAssurancePolicyAsync(
			provider.Id,
			provider.Version,
			"mfa",
			"urn:example:loa:2",
			30));

		context.Authorization.SignOut();
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Service.ConfigureAssurancePolicyAsync(
			provider.Id,
			updated.Version,
			"mfa",
			"urn:example:loa:2",
			30));
	}

	[Fact]
	public async Task ResolutionUsesOnlyLocalUserRolesAndPermissions()
	{
		var context = await CreateContextAsync();
		var target = await context.CreateUserAsync("local-user@example.test", "Local User");
		await context.AssignRoleAsync(target.Id, "enterprise-user", ApplicationPermission.DashboardView);
		await context.Service.CreateProviderAsync(
			"entra-main",
			EnterpriseIdentityProviderKind.MicrosoftEntraId,
			"Microsoft Entra ID",
			"https://login.microsoftonline.com/tenant-a/v2.0",
			"depot-client",
			"tenant-a",
			true);
		var identity = new ValidatedExternalIdentity(
			"entra-main",
			"https://login.microsoftonline.com/tenant-a/v2.0",
			"subject-001",
			"tenant-a",
			"external-name@example.test",
			"External Display Name");
		await context.Service.LinkAsync(target.Id, identity);

		var resolution = await context.Service.ResolveAsync(identity);

		Assert.NotNull(resolution);
		Assert.Equal(target.Id, resolution!.User.Id);
		Assert.Equal("local-user@example.test", resolution.User.Email);
		Assert.Equal("Local User", resolution.User.DisplayName);
		Assert.Contains(ApplicationPermission.DashboardView, resolution.User.EffectivePermissions);
		Assert.DoesNotContain(ApplicationPermission.UsersManage, resolution.User.EffectivePermissions);
		Assert.Single(resolution.User.Roles);
		Assert.Equal("enterprise-user", resolution.User.Roles[0].Code);
	}

	[Fact]
	public async Task IdentitySubjectAndIssuerAreMatchedExactly()
	{
		var context = await CreateContextAsync();
		var target = await context.CreateUserAsync("case-user@example.test", "Case User");
		await context.Service.CreateProviderAsync(
			"oidc",
			EnterpriseIdentityProviderKind.OpenIdConnect,
			"OIDC",
			"https://identity.example.test",
			"depot-client",
			null,
			true);
		var linked = new ValidatedExternalIdentity("oidc", "https://issuer.example.test", "Subject-A");
		await context.Service.LinkAsync(target.Id, linked);

		Assert.NotNull(await context.Service.ResolveAsync(linked));
		Assert.Null(await context.Service.ResolveAsync(linked with { Subject = "subject-a" }));
		Assert.Null(await context.Service.ResolveAsync(linked with { Issuer = "https://ISSUER.example.test" }));
	}

	[Fact]
	public async Task DisabledProviderAndInactiveUserFailClosed()
	{
		var context = await CreateContextAsync();
		var target = await context.CreateUserAsync("disabled-user@example.test", "Disabled User");
		var provider = await context.Service.CreateProviderAsync(
			"entra",
			EnterpriseIdentityProviderKind.MicrosoftEntraId,
			"Entra",
			"https://login.microsoftonline.com/tenant-b/v2.0",
			"client-b",
			"tenant-b",
			true);
		var identity = new ValidatedExternalIdentity(
			"entra",
			"https://login.microsoftonline.com/tenant-b/v2.0",
			"subject-b",
			"tenant-b");
		await context.Service.LinkAsync(target.Id, identity);
		Assert.NotNull(await context.Service.ResolveAsync(identity));

		provider = await context.Service.UpdateProviderAsync(
			provider.Id,
			provider.Version,
			provider.DisplayName,
			provider.Authority,
			provider.ClientId,
			provider.TenantId,
			false);
		Assert.False(provider.IsEnabled);
		Assert.Null(await context.Service.ResolveAsync(identity));

		provider = await context.Service.UpdateProviderAsync(
			provider.Id,
			provider.Version,
			provider.DisplayName,
			provider.Authority,
			provider.ClientId,
			provider.TenantId,
			true);
		var storedUser = await context.Users.GetByIdAsync(target.Id, CancellationToken.None) ?? throw new InvalidOperationException();
		Assert.True(await context.Users.SetActiveAsync(storedUser.Id, false, storedUser.Version, CancellationToken.None));
		Assert.Null(await context.Service.ResolveAsync(identity));
	}

	[Fact]
	public async Task OneExternalIdentityCannotBeLinkedToTwoLocalUsers()
	{
		var context = await CreateContextAsync();
		var first = await context.CreateUserAsync("first@example.test", "First");
		var second = await context.CreateUserAsync("second@example.test", "Second");
		await context.Service.CreateProviderAsync(
			"oidc-main",
			EnterpriseIdentityProviderKind.OpenIdConnect,
			"OIDC Main",
			"https://identity.example.test",
			"client-main",
			null,
			true);
		var identity = new ValidatedExternalIdentity("oidc-main", "https://issuer.example.test", "shared-subject");
		await context.Service.LinkAsync(first.Id, identity);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.LinkAsync(second.Id, identity));
		Assert.Contains("another local user", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task SuccessfulAuthenticationUpdatesObservationWithoutChangingLocalIdentity()
	{
		var context = await CreateContextAsync();
		var target = await context.CreateUserAsync("local@example.test", "Local Identity");
		await context.Service.CreateProviderAsync(
			"oidc-observe",
			EnterpriseIdentityProviderKind.OpenIdConnect,
			"OIDC Observe",
			"https://identity.example.test",
			"client-observe",
			null,
			true);
		var initial = new ValidatedExternalIdentity(
			"oidc-observe",
			"https://issuer.example.test",
			"observe-subject",
			null,
			"old@example.test",
			"Old Name");
		var link = await context.Service.LinkAsync(target.Id, initial);
		Assert.Null(link.LastSeenUtc);

		var resolution = await context.Service.RecordSuccessfulAuthenticationAsync(
			initial with { Email = "new@example.test", DisplayName = "New Name" });

		Assert.NotNull(resolution);
		Assert.NotNull(resolution!.Link.LastSeenUtc);
		Assert.Equal("new@example.test", resolution.Link.Email);
		Assert.Equal("New Name", resolution.Link.DisplayName);
		Assert.Equal(link.Version + 1, resolution.Link.Version);
		var local = await context.Users.GetByIdAsync(target.Id, CancellationToken.None);
		Assert.NotNull(local);
		Assert.Equal("local@example.test", local!.Email);
		Assert.Equal("Local Identity", local.DisplayName);
	}

	[Fact]
	public async Task TenantBoundaryRejectsIdentityFromAnotherTenant()
	{
		var context = await CreateContextAsync();
		var target = await context.CreateUserAsync("tenant@example.test", "Tenant User");
		await context.Service.CreateProviderAsync(
			"entra-tenant",
			EnterpriseIdentityProviderKind.MicrosoftEntraId,
			"Tenant Entra",
			"https://login.microsoftonline.com/tenant-c/v2.0",
			"client-c",
			"tenant-c",
			true);
		var identity = new ValidatedExternalIdentity(
			"entra-tenant",
			"https://login.microsoftonline.com/tenant-c/v2.0",
			"subject-c",
			"tenant-c");
		await context.Service.LinkAsync(target.Id, identity);

		Assert.Null(await context.Service.ResolveAsync(identity with { TenantId = "tenant-other" }));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			context.Service.LinkAsync(target.Id, identity with { Subject = "subject-other", TenantId = "tenant-other" }));
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = InitializeCore();
		EnterpriseIdentitySchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(access);
		var users = new UserRepository(access);
		var roles = new RoleRepository(access);
		var auditEntries = new AuditRepository(access);
		var authorization = new AuthorizationService();
		var admin = new User
		{
			Email = "enterprise-admin@example.test",
			DisplayName = "Enterprise Admin",
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};
		admin.Id = await users.CreateAsync(admin, "unused", CancellationToken.None);
		authorization.SignIn(admin, [ApplicationPermission.UsersView, ApplicationPermission.UsersManage]);
		var audit = new AuditService(auditEntries, authorization);
		var repository = new EnterpriseIdentityRepository(access);
		var service = new EnterpriseIdentityService(
			transactions,
			repository,
			users,
			roles,
			auditEntries,
			audit,
			authorization);
		return new TestContext(transactions, users, roles, auditEntries, authorization, service);
	}

	private SqliteConnectionFactory InitializeCore()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		return factory;
	}

	private SqliteConnection Open()
	{
		var connection = new SqliteConnection($"Data Source={_path}");
		connection.Open();
		return connection;
	}

	private static int Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		IDatabaseTransactionRunner Transactions,
		UserRepository Users,
		RoleRepository Roles,
		AuditRepository AuditEntries,
		AuthorizationService Authorization,
		EnterpriseIdentityService Service)
	{
		public async Task<User> CreateUserAsync(string email, string displayName)
		{
			var user = new User
			{
				Email = email,
				DisplayName = displayName,
				IsActive = true,
				CreatedUtc = DateTime.UtcNow
			};
			user.Id = await Users.CreateAsync(user, "unused", CancellationToken.None);
			return user;
		}

		public Task AssignRoleAsync(long userId, string code, ApplicationPermission permission) =>
			Transactions.ExecuteAsync(async (transaction, token) =>
			{
				var role = new Role
				{
					Code = code,
					Name = code,
					Description = "Enterprise identity test role",
					IsActive = true
				};
				role.Id = await RoleRepository.CreateAsync(transaction, role, token);
				await RoleRepository.ReplacePermissionsAsync(transaction, role.Id, [permission], token);
				await RoleRepository.ReplaceUserRolesAsync(transaction, userId, [role.Id], token);
				return true;
			});
	}
}
