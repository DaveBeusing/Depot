// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SecurityAdministrationCompletionTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-security-completion-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task RetentionMaintenancePurgesOnlyExpiredSecurityData()
	{
		var context = await CreateContextAsync();
		var sessionPolicy = await context.Sessions.GetPolicyAsync(CancellationToken.None);
		await context.SessionAdministration.SavePolicyAsync(
			30, 12, ConcurrentSessionMode.Unlimited, 3, ConcurrentSessionLimitAction.RejectNewSession, 30,
			sessionPolicy.Version, CancellationToken.None);
		var authenticationPolicy = await context.AuthenticationSecurity.GetPolicyAsync(CancellationToken.None);
		await context.AuthenticationSecurity.SavePolicyAsync(15, 5, 15, 30, authenticationPolicy.Version, CancellationToken.None);

		var oldSession = await CreateEndedSessionAsync(context, context.Clock.UtcNow.AddDays(-60), context.Clock.UtcNow.AddDays(-59));
		var recentSession = await CreateEndedSessionAsync(context, context.Clock.UtcNow.AddDays(-5), context.Clock.UtcNow.AddDays(-4));
		var oldEvent = await context.SecurityEventsRepository.CreateAsync(new SecurityEvent { TimestampUtc = context.Clock.UtcNow.AddDays(-60), EventType = SecurityEventType.AuthenticationFailed, Severity = SecurityEventSeverity.Information, Summary = "old" }, CancellationToken.None);
		var recentEvent = await context.SecurityEventsRepository.CreateAsync(new SecurityEvent { TimestampUtc = context.Clock.UtcNow.AddDays(-5), EventType = SecurityEventType.AuthenticationFailed, Severity = SecurityEventSeverity.Information, Summary = "recent" }, CancellationToken.None);
		await context.Transactions.ExecuteAsync(async (transaction, token) =>
		{
			await AuthenticationSecurityRepository.UpsertThrottleAsync(transaction, new AuthenticationThrottleState
			{
				AccountKey = "stale@test.local",
				FirstFailureUtc = context.Clock.UtcNow.AddDays(-2),
				FailureCount = 1,
				UpdatedUtc = context.Clock.UtcNow.AddDays(-2)
			}, token);
			return true;
		}, CancellationToken.None);

		var result = await context.Maintenance.RunOnceAsync(CancellationToken.None);

		Assert.True(result.SessionsDeleted >= 1);
		Assert.True(result.SecurityEventsDeleted >= 1);
		Assert.True(result.ThrottleEntriesDeleted >= 1);
		Assert.Null(await context.Sessions.GetBySessionIdAsync(oldSession, CancellationToken.None));
		Assert.NotNull(await context.Sessions.GetBySessionIdAsync(recentSession, CancellationToken.None));
		var remaining = await context.SecurityEventsRepository.GetRecentAsync(new SecurityEventFilter(null, null, null), 250, CancellationToken.None);
		Assert.DoesNotContain(remaining, item => item.Id == oldEvent);
		Assert.Contains(remaining, item => item.Id == recentEvent);
	}

	[Fact]
	public async Task InvestigationCorrelatesSessionClientAndUserEvents()
	{
		var context = await CreateContextAsync();
		var sessionId = Guid.NewGuid();
		var clientId = Guid.NewGuid();
		var anchorId = await context.SecurityEventsRepository.CreateAsync(new SecurityEvent
		{
			TimestampUtc = context.Clock.UtcNow,
			EventType = SecurityEventType.AuthenticationSucceeded,
			Severity = SecurityEventSeverity.Information,
			UserId = context.TargetUserId,
			AccountIdentifier = context.TargetEmail,
			SessionId = sessionId,
			ClientInstanceId = clientId,
			Summary = "anchor"
		}, CancellationToken.None);
		var relatedId = await context.SecurityEventsRepository.CreateAsync(new SecurityEvent
		{
			TimestampUtc = context.Clock.UtcNow.AddMinutes(-1),
			EventType = SecurityEventType.AuthenticationFailed,
			Severity = SecurityEventSeverity.Warning,
			UserId = context.TargetUserId,
			ClientInstanceId = clientId,
			Summary = "related"
		}, CancellationToken.None);
		var unrelatedId = await context.SecurityEventsRepository.CreateAsync(new SecurityEvent
		{
			TimestampUtc = context.Clock.UtcNow.AddMinutes(-2),
			EventType = SecurityEventType.AuthenticationFailed,
			Severity = SecurityEventSeverity.Warning,
			AccountIdentifier = "other@test.local",
			Summary = "unrelated"
		}, CancellationToken.None);
		var anchor = (await context.SecurityEventsRepository.GetRecentAsync(new SecurityEventFilter("anchor", null, null), 10, CancellationToken.None)).Single(item => item.Id == anchorId);

		var investigation = await context.SecurityAdministration.GetInvestigationAsync(anchor, CancellationToken.None);

		Assert.Equal(context.TargetUserId, investigation.UserId);
		Assert.Contains(investigation.RelatedEvents, item => item.Id == relatedId);
		Assert.DoesNotContain(investigation.RelatedEvents, item => item.Id == unrelatedId);
	}

	[Fact]
	public async Task SecurityResponseCanTerminateSessionAndDeactivateResolvedUser()
	{
		var context = await CreateContextAsync();
		var firstSession = await CreateOpenSessionAsync(context);
		var anchor = new SecurityEventListItem { Id = 100, UserId = context.TargetUserId, AccountIdentifier = context.TargetEmail, SessionId = firstSession, Summary = "response", Version = 1 };

		Assert.True(await context.SecurityAdministration.TerminateSelectedSessionAsync(anchor, CancellationToken.None));
		Assert.Equal(UserSessionEndReason.AdministrativeLogout, (await context.Sessions.GetBySessionIdAsync(firstSession, CancellationToken.None))!.EndReason);

		var secondSession = await CreateOpenSessionAsync(context);
		await context.SecurityAdministration.DeactivateSelectedUserAsync(anchor, CancellationToken.None);

		var user = await context.Users.GetByIdAsync(context.TargetUserId, CancellationToken.None);
		Assert.False(user!.IsActive);
		Assert.Equal(UserSessionEndReason.Revoked, (await context.Sessions.GetBySessionIdAsync(secondSession, CancellationToken.None))!.EndReason);
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		SecurityEventSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(access);
		var users = new UserRepository(access);
		var roles = new RoleRepository(access);
		var sessions = new UserSessionRepository(access);
		var securityEventsRepository = new SecurityEventRepository(access);
		var authenticationSecurityRepository = new AuthenticationSecurityRepository(access);
		var auditRepository = new AuditRepository(access);
		var clock = new MutableTimeProvider { UtcNow = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc) };
		var admin = new User { Email = "admin@test.local", DisplayName = "Admin", IsActive = true, CreatedUtc = clock.UtcNow };
		admin.Id = await users.CreateAsync(admin, "unused", CancellationToken.None);
		var target = new User { Email = "target@test.local", DisplayName = "Target", IsActive = true, CreatedUtc = clock.UtcNow };
		target.Id = await users.CreateAsync(target, "unused", CancellationToken.None);
		var authorization = new AuthorizationService();
		authorization.SignIn(admin, new HashSet<ApplicationPermission>
		{
			ApplicationPermission.SecurityEventsView,
			ApplicationPermission.SecurityEventsManage,
			ApplicationPermission.UsersView,
			ApplicationPermission.UsersManage,
			ApplicationPermission.UserSessionsTerminate,
			ApplicationPermission.SettingsManage
		});
		var audit = new AuditService(auditRepository, authorization);
		var securityEvents = new SecurityEventService(securityEventsRepository, authorization, timeProvider: clock);
		var authenticationSecurity = new AuthenticationSecurityService(transactions, authenticationSecurityRepository, auditRepository, audit, securityEvents, authorization, clock);
		var sessionAdministration = new UserSessionAdministrationService(transactions, sessions, auditRepository, authorization, audit, securityEvents, clock);
		var userService = new UserService(transactions, users, roles, auditRepository, new PasswordHasher(), authorization, audit, securityEvents: securityEvents);
		var securityAdministration = new SecurityAdministrationService(securityEvents, authenticationSecurity, sessionAdministration, sessions, users, userService, authorization);
		securityEvents.ConfigureAdministration(securityAdministration);
		var maintenance = new SecurityMaintenanceService(transactions, sessions, securityEventsRepository, authenticationSecurityRepository, clock);
		return new TestContext(transactions, users, sessions, securityEventsRepository, authenticationSecurity, sessionAdministration, securityAdministration, maintenance, clock, target.Id, target.Email);
	}

	private static async Task<Guid> CreateOpenSessionAsync(TestContext context)
	{
		var session = new UserSession
		{
			SessionId = Guid.NewGuid(),
			UserId = context.TargetUserId,
			StartedUtc = context.Clock.UtcNow,
			LastSeenUtc = context.Clock.UtcNow,
			LastActivityUtc = context.Clock.UtcNow,
			ClientInstanceId = Guid.NewGuid(),
			MachineName = "TEST-CLIENT",
			AppVersion = "test"
		};
		await context.Sessions.CreateAsync(session, CancellationToken.None);
		return session.SessionId;
	}

	private static async Task<Guid> CreateEndedSessionAsync(TestContext context, DateTime startedUtc, DateTime endedUtc)
	{
		var session = new UserSession
		{
			SessionId = Guid.NewGuid(),
			UserId = context.TargetUserId,
			StartedUtc = startedUtc,
			LastSeenUtc = endedUtc,
			LastActivityUtc = endedUtc,
			ClientInstanceId = Guid.NewGuid(),
			MachineName = "TEST-CLIENT",
			AppVersion = "test"
		};
		await context.Sessions.CreateAsync(session, CancellationToken.None);
		Assert.True(await context.Sessions.EndAsync(session.SessionId, endedUtc, UserSessionEndReason.LoggedOut, CancellationToken.None));
		return session.SessionId;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		DatabaseTransactionRunner Transactions,
		UserRepository Users,
		UserSessionRepository Sessions,
		SecurityEventRepository SecurityEventsRepository,
		AuthenticationSecurityService AuthenticationSecurity,
		UserSessionAdministrationService SessionAdministration,
		SecurityAdministrationService SecurityAdministration,
		SecurityMaintenanceService Maintenance,
		MutableTimeProvider Clock,
		long TargetUserId,
		string TargetEmail);

	private sealed class MutableTimeProvider : TimeProvider
	{
		public DateTime UtcNow { get; set; }
		public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
	}
}
