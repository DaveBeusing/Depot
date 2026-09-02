// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class UserSessionPolicyTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-user-session-policy-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task SettingsManageMayUpdatePolicyAndChangeIsVersioned()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(
			new User { Id = 1, Email = "admin@test.local", DisplayName = "Admin", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.UsersView, ApplicationPermission.SettingsManage });

		var before = await context.Repository.GetPolicyAsync(CancellationToken.None);
		var saved = await context.Service.SavePolicyAsync(45, 24, before.Version, CancellationToken.None);
		Assert.Equal(45, saved.IdleTimeoutMinutes);
		Assert.Equal(24, saved.MaximumSessionAgeHours);
		Assert.Equal(before.Version + 1, saved.Version);

		var persisted = await context.Repository.GetPolicyAsync(CancellationToken.None);
		Assert.Equal(saved.IdleTimeoutMinutes, persisted.IdleTimeoutMinutes);
		Assert.Equal(saved.MaximumSessionAgeHours, persisted.MaximumSessionAgeHours);
		Assert.Equal(saved.Version, persisted.Version);
	}

	[Fact]
	public async Task PolicyUpdateRequiresSettingsManage()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(
			new User { Id = 1, Email = "viewer@test.local", DisplayName = "Viewer", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.UsersView });
		var policy = await context.Repository.GetPolicyAsync(CancellationToken.None);

		Assert.False(context.Service.CanManagePolicy);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
			context.Service.SavePolicyAsync(45, 24, policy.Version, CancellationToken.None));
	}

	[Theory]
	[InlineData(4, 12)]
	[InlineData(481, 12)]
	[InlineData(30, 0)]
	[InlineData(30, 169)]
	public async Task PolicyRejectsValuesOutsideSupportedRanges(int idleMinutes, int maximumAgeHours)
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(
			new User { Id = 1, Email = "admin@test.local", DisplayName = "Admin", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.UsersView, ApplicationPermission.SettingsManage });
		var policy = await context.Repository.GetPolicyAsync(CancellationToken.None);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			context.Service.SavePolicyAsync(idleMinutes, maximumAgeHours, policy.Version, CancellationToken.None));
	}

	[Fact]
	public async Task PolicyUpdateDetectsOptimisticConcurrencyConflict()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(
			new User { Id = 1, Email = "admin@test.local", DisplayName = "Admin", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.UsersView, ApplicationPermission.SettingsManage });
		var policy = await context.Repository.GetPolicyAsync(CancellationToken.None);
		await context.Service.SavePolicyAsync(45, 24, policy.Version, CancellationToken.None);

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			context.Service.SavePolicyAsync(60, 24, policy.Version, CancellationToken.None));
	}

	[Fact]
	public async Task SavingStricterPolicyExpiresAlreadyOverAgeOpenSessions()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(
			new User { Id = 1, Email = "admin@test.local", DisplayName = "Admin", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.UsersView, ApplicationPermission.SettingsManage });
		var session = new UserSession
		{
			SessionId = Guid.NewGuid(),
			UserId = context.UserId,
			StartedUtc = context.Clock.UtcNow.AddHours(-2),
			LastSeenUtc = context.Clock.UtcNow,
			LastActivityUtc = context.Clock.UtcNow,
			ClientInstanceId = Guid.NewGuid(),
			MachineName = "OLD-SESSION",
			AppVersion = "0.15.93-preview"
		};
		await context.Repository.CreateAsync(session, CancellationToken.None);
		var policy = await context.Repository.GetPolicyAsync(CancellationToken.None);

		await context.Service.SavePolicyAsync(30, 1, policy.Version, CancellationToken.None);
		var ended = await context.Repository.GetBySessionIdAsync(session.SessionId, CancellationToken.None);
		Assert.Equal(UserSessionEndReason.Expired, ended!.EndReason);
		Assert.NotNull(ended.EndedUtc);
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var users = new UserRepository(access);
		var user = new User
		{
			Email = "policy-user@test.local",
			DisplayName = "Policy User",
			IsActive = true,
			CreatedUtc = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc)
		};
		var userId = await users.CreateAsync(user, "unused", CancellationToken.None);
		var repository = new UserSessionRepository(access);
		var authorization = new AuthorizationService();
		var clock = new MutableTimeProvider { UtcNow = user.CreatedUtc };
		var service = new UserSessionAdministrationService(repository, authorization, clock);
		return new TestContext(repository, authorization, service, clock, userId);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		UserSessionRepository Repository,
		AuthorizationService Authorization,
		UserSessionAdministrationService Service,
		MutableTimeProvider Clock,
		long UserId);

	private sealed class MutableTimeProvider : TimeProvider
	{
		public DateTime UtcNow { get; set; }
		public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
	}
}
