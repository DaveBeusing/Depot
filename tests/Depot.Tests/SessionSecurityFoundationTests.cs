// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SessionSecurityFoundationTests : IDisposable
{
	private readonly string _path=Path.Combine(Path.GetTempPath(),$"depot-session-security-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task SharedThrottleAccumulatesFailuresAcrossServiceInstances()
	{
		var context=CreateContext();
		var first=CreateAuthenticationSecurity(context);
		var second=CreateAuthenticationSecurity(context);
		Assert.Equal(1,(await first.RecordFailureAsync("shared@test.local",CancellationToken.None)).FailureCount);
		Assert.Equal(2,(await second.RecordFailureAsync("shared@test.local",CancellationToken.None)).FailureCount);
		Assert.Equal(3,(await first.RecordFailureAsync("shared@test.local",CancellationToken.None)).FailureCount);
		var snapshot=await second.GetStatusAsync("shared@test.local",CancellationToken.None);
		Assert.Equal(3,snapshot.FailureCount);
		Assert.False(snapshot.IsBlocked);
	}

	[Fact]
	public async Task SharedThrottleBlocksAtConfiguredDefaultThreshold()
	{
		var context=CreateContext();var service=CreateAuthenticationSecurity(context);LoginAttemptStatus status=new(0,false,TimeSpan.Zero);
		for(var i=0;i<AuthenticationSecurityPolicy.DefaultLockoutThreshold;i++)status=await service.RecordFailureAsync("blocked@test.local",CancellationToken.None);
		Assert.True(status.IsBlocked);Assert.Equal(AuthenticationSecurityPolicy.DefaultLockoutThreshold,status.FailureCount);Assert.True(status.RetryAfter>TimeSpan.Zero);
		var fromSecondInstance=await CreateAuthenticationSecurity(context).GetStatusAsync("blocked@test.local",CancellationToken.None);Assert.True(fromSecondInstance.IsBlocked);
	}

	[Fact]
	public async Task SingleSessionPolicyCanSupersedeOldestSession()
	{
		var context=CreateContext();var userId=await CreateUserAsync(context);
		var policy=await context.Sessions.GetPolicyAsync(CancellationToken.None);policy.ConcurrentSessionMode=ConcurrentSessionMode.SingleSession;policy.ConcurrentSessionLimitAction=ConcurrentSessionLimitAction.SupersedeOldestSession;policy.UpdatedUtc=DateTime.UtcNow;Assert.True(await context.Sessions.UpdatePolicyAsync(policy,policy.Version,CancellationToken.None));
		var first=CreateSessionService(context,"CLIENT-A");await first.StartAuthenticatedSessionAsync(userId,CancellationToken.None);var firstId=Assert.IsType<Guid>(first.CurrentSessionId);
		var second=CreateSessionService(context,"CLIENT-B");await second.StartAuthenticatedSessionAsync(userId,CancellationToken.None);var secondId=Assert.IsType<Guid>(second.CurrentSessionId);
		var oldSession=await context.Sessions.GetBySessionIdAsync(firstId,CancellationToken.None);var newSession=await context.Sessions.GetBySessionIdAsync(secondId,CancellationToken.None);
		Assert.Equal(UserSessionEndReason.Superseded,oldSession!.EndReason);Assert.Null(newSession!.EndedUtc);first.Dispose();second.Dispose();
	}

	[Fact]
	public async Task SingleSessionPolicyCanRejectNewSession()
	{
		var context=CreateContext();var userId=await CreateUserAsync(context);
		var policy=await context.Sessions.GetPolicyAsync(CancellationToken.None);policy.ConcurrentSessionMode=ConcurrentSessionMode.SingleSession;policy.ConcurrentSessionLimitAction=ConcurrentSessionLimitAction.RejectNewSession;policy.UpdatedUtc=DateTime.UtcNow;Assert.True(await context.Sessions.UpdatePolicyAsync(policy,policy.Version,CancellationToken.None));
		var first=CreateSessionService(context,"CLIENT-A");await first.StartAuthenticatedSessionAsync(userId,CancellationToken.None);
		var second=CreateSessionService(context,"CLIENT-B");await Assert.ThrowsAsync<SessionLimitExceededException>(()=>second.StartAuthenticatedSessionAsync(userId,CancellationToken.None));
		Assert.Null(second.CurrentSessionId);first.Dispose();second.Dispose();
	}

	private TestContext CreateContext()
	{
		var factory=new SqliteConnectionFactory(_path);new DepotDatabase(factory).Initialize();UserSessionSchemaMigration.Migrate(factory);SecurityEventSchemaMigration.Migrate(factory);var access=new DatabaseAccess(factory);var authorization=new AuthorizationService();var securityEvents=new SecurityEventService(new SecurityEventRepository(access),authorization);
		return new TestContext(access,new DatabaseTransactionRunner(access),new UserRepository(access),new UserSessionRepository(access),new AuthenticationSecurityRepository(access),new AuditRepository(access),authorization,securityEvents);
	}

	private AuthenticationSecurityService CreateAuthenticationSecurity(TestContext context)=>new(context.Transactions,context.AuthenticationSecurity,context.Audit,new AuditService(context.Audit,context.Authorization),context.SecurityEvents,context.Authorization);
	private SessionService CreateSessionService(TestContext context,string machine){var service=new SessionService(context.Authorization);service.Configure(context.Transactions,context.Sessions,context.SecurityEvents,new UserSessionClientInfo(Guid.NewGuid(),machine,"0.15.96-preview"));return service;}
	private static async Task<long> CreateUserAsync(TestContext context){var user=new User{Email=$"session-{Guid.NewGuid():N}@test.local",DisplayName="Session Policy User",IsActive=true,CreatedUtc=DateTime.UtcNow};var id=await context.Users.CreateAsync(user,new PasswordHasher().Hash("Correct-Password-42!"),CancellationToken.None);return id;}

	public void Dispose(){SqliteConnection.ClearAllPools();if(File.Exists(_path))File.Delete(_path);}
	private sealed record TestContext(DatabaseAccess Access,DatabaseTransactionRunner Transactions,UserRepository Users,UserSessionRepository Sessions,AuthenticationSecurityRepository AuthenticationSecurity,AuditRepository Audit,AuthorizationService Authorization,SecurityEventService SecurityEvents);
}
