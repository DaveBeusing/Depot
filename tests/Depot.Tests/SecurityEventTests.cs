// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SecurityEventTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-security-events-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task RepeatedFailuresEscalateAndSuccessfulLoginAfterFailuresIsVisible()
	{
		var context = await CreateContextAsync();
		Assert.False(await context.Authentication.SignInAsync(context.Email, "wrong-1", CancellationToken.None));
		Assert.False(await context.Authentication.SignInAsync(context.Email, "wrong-2", CancellationToken.None));
		Assert.False(await context.Authentication.SignInAsync(context.Email, "wrong-3", CancellationToken.None));
		Assert.True(await context.Authentication.SignInAsync(context.Email, "Correct-Password-42!", CancellationToken.None));

		context.Authorization.SignIn(new User { Id = context.UserId, Email = context.Email, DisplayName = "Security Admin", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.SecurityEventsView });
		var events = await context.Security.GetRecentAsync(new SecurityEventFilter(null, null, null), CancellationToken.None);
		Assert.Contains(events, item => item.EventType == SecurityEventType.SuspiciousAuthenticationFailures && item.Severity == SecurityEventSeverity.Warning);
		Assert.Contains(events, item => item.EventType == SecurityEventType.AuthenticationSucceededAfterFailures && item.Severity == SecurityEventSeverity.High);
		var metrics = await context.Security.GetMetricsAsync(CancellationToken.None);
		Assert.True(metrics.Suspicious24Hours >= 2);
	}

	[Fact]
	public async Task FifthFailureCreatesCriticalBlockAndBlockedAttemptsRemainVisible()
	{
		var context = await CreateContextAsync();
		for (var index = 0; index < 5; index++) Assert.False(await context.Authentication.SignInAsync(context.Email, $"wrong-{index}", CancellationToken.None));
		Assert.False(await context.Authentication.SignInAsync(context.Email, "still-wrong", CancellationToken.None));

		context.Authorization.SignIn(new User { Id = context.UserId, Email = context.Email, DisplayName = "Security Admin", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.SecurityEventsView });
		var events = await context.Security.GetRecentAsync(new SecurityEventFilter(null, SecurityEventSeverity.High, null), CancellationToken.None);
		Assert.True(events.Count(item => item.EventType == SecurityEventType.AuthenticationBlocked) >= 2);
		Assert.Contains(events, item => item.EventType == SecurityEventType.AuthenticationBlocked && item.Severity == SecurityEventSeverity.Critical);
	}

	[Fact]
	public async Task ReviewRequiresManagePermissionAndUsesOptimisticVersion()
	{
		var context = await CreateContextAsync();
		Assert.False(await context.Authentication.SignInAsync(context.Email, "wrong", CancellationToken.None));
		context.Authorization.SignIn(new User { Id = context.UserId, Email = context.Email, DisplayName = "Reviewer", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.SecurityEventsView });
		var item = Assert.Single(await context.Security.GetRecentAsync(new SecurityEventFilter(null, null, false), CancellationToken.None));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Security.MarkReviewedAsync(item.Id, item.Version, CancellationToken.None));

		context.Authorization.SignIn(new User { Id = context.UserId, Email = context.Email, DisplayName = "Reviewer", IsActive = true },
			new HashSet<ApplicationPermission> { ApplicationPermission.SecurityEventsView, ApplicationPermission.SecurityEventsManage });
		await context.Security.MarkReviewedAsync(item.Id, item.Version, CancellationToken.None);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.Security.MarkReviewedAsync(item.Id, item.Version, CancellationToken.None));
		var reviewed = await context.Security.GetRecentAsync(new SecurityEventFilter(null, null, true), CancellationToken.None);
		Assert.Contains(reviewed, value => value.Id == item.Id && value.IsReviewed);
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var users = new UserRepository(access);
		var roles = new RoleRepository(access);
		var securityRepository = new SecurityEventRepository(access);
		var authorization = new AuthorizationService();
		var clock = new MutableTimeProvider { UtcNow = new DateTime(2026, 9, 1, 19, 0, 0, DateTimeKind.Utc) };
		var security = new SecurityEventService(securityRepository, authorization, timeProvider: clock);
		var limiter = new LoginAttemptLimiter(clock);
		var hasher = new PasswordHasher();
		var email = $"security-{Guid.NewGuid():N}@test.local";
		var user = new User { Email = email, DisplayName = "Security User", IsActive = true, CreatedUtc = clock.UtcNow };
		var userId = await users.CreateAsync(user, hasher.Hash("Correct-Password-42!"), CancellationToken.None);
		var authentication = new AuthenticationService(users, roles, hasher, authorization, limiter, security);
		return new TestContext(email, userId, authorization, authentication, security);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(string Email, long UserId, AuthorizationService Authorization, AuthenticationService Authentication, SecurityEventService Security);
	private sealed class MutableTimeProvider : TimeProvider
	{
		public DateTime UtcNow { get; set; }
		public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
	}
}
