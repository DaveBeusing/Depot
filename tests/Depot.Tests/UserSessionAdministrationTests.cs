// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Depot.ViewModels.Administration;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class UserSessionAdministrationTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-user-session-admin-{Guid.NewGuid():N}.db");

	[Fact]
	public async Task AuthorizedUserMayLoadSessionsAndMultiSessionMetricsRemainDistinct()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(new User { Id = 1, DisplayName = "Admin", IsActive = true }, new HashSet<ApplicationPermission> { ApplicationPermission.UsersView });
		await context.Repository.CreateAsync(NewSession(context.UserId, context.Clock.UtcNow, "OFFICE-PC"), CancellationToken.None);
		await context.Repository.CreateAsync(NewSession(context.UserId, context.Clock.UtcNow.AddSeconds(-10), "NOTEBOOK"), CancellationToken.None);
		var snapshot = await context.Service.GetSnapshotAsync(CancellationToken.None);
		Assert.Equal(1, snapshot.Metrics.OnlineUsers);
		Assert.Equal(2, snapshot.Metrics.ActiveSessions);
		Assert.Equal(2, snapshot.Sessions.Count);

		var viewModel = new UserSessionsViewModel(context.Service);
		await viewModel.LoadAsync();
		Assert.Equal(2, viewModel.Sessions.Count);
		viewModel.SearchText = "NOTEBOOK";
		Assert.Single(viewModel.Sessions);
		viewModel.StartPolling(); Assert.True(viewModel.IsPolling);
		viewModel.StopPolling(); Assert.False(viewModel.IsPolling);
		viewModel.Dispose();
	}

	[Fact]
	public void SessionRowShowsConcreteOnlineDurationAndLocalStartTime()
	{
		var asOfUtc = new DateTime(2026, 9, 1, 14, 49, 0, DateTimeKind.Utc);
		var startedUtc = asOfUtc.AddHours(-2).AddMinutes(-14);
		var session = new ActiveUserSession(1, Guid.NewGuid(), 42, "anna@example.com", "Anna Müller", startedUtc, asOfUtc.AddSeconds(-8), null, Guid.NewGuid(), "OFFICE-PC", "0.15.83-preview", 1);
		var row = new UserSessionRowViewModel(session, asOfUtc);
		Assert.Equal("2h 14m", row.OnlineFor);
		Assert.Equal(42, row.UserId);
		Assert.Equal(startedUtc.ToLocalTime(), row.StartedLocal);
		Assert.Equal("8 sec ago", row.LastSeen);
	}

	[Fact]
	public async Task UnauthorizedUserIsDeniedByServiceLayer()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(new User { Id = context.UserId, DisplayName = "User", IsActive = true }, []);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Service.GetSnapshotAsync(CancellationToken.None));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Service.TerminateUserSessionsAsync(context.UserId, CancellationToken.None));
	}

	[Fact]
	public async Task TwoUsersWithOneSessionEachProduceTwoOnlineUsersAndTwoSessions()
	{
		var context = await CreateContextAsync();
		var secondUser = new User { Email = "second@test.local", DisplayName = "Second User", IsActive = true, CreatedUtc = context.Clock.UtcNow };
		var secondUserId = await new UserRepository(context.Access).CreateAsync(secondUser, "unused", CancellationToken.None);
		await context.Repository.CreateAsync(NewSession(context.UserId, context.Clock.UtcNow, "CLIENT-A"), CancellationToken.None);
		await context.Repository.CreateAsync(NewSession(secondUserId, context.Clock.UtcNow, "CLIENT-B"), CancellationToken.None);
		context.Authorization.SignIn(new User { Id = 1, DisplayName = "Admin", IsActive = true }, new HashSet<ApplicationPermission> { ApplicationPermission.UsersView });
		var snapshot = await context.Service.GetSnapshotAsync(CancellationToken.None);
		Assert.Equal(2, snapshot.Metrics.OnlineUsers);
		Assert.Equal(2, snapshot.Metrics.ActiveSessions);
	}

	[Fact]
	public async Task TerminateAllForUserEndsEveryOpenSessionAndAddsHistory()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(new User { Id = 1, DisplayName = "Admin", IsActive = true }, new HashSet<ApplicationPermission> { ApplicationPermission.UsersView, ApplicationPermission.UserSessionsTerminate });
		var first = NewSession(context.UserId, context.Clock.UtcNow.AddMinutes(-20), "CLIENT-A");
		var second = NewSession(context.UserId, context.Clock.UtcNow.AddMinutes(-5), "CLIENT-B");
		await context.Repository.CreateAsync(first, CancellationToken.None);
		await context.Repository.CreateAsync(second, CancellationToken.None);

		Assert.Equal(2, await context.Service.TerminateUserSessionsAsync(context.UserId, CancellationToken.None));
		Assert.All(new[] { first, second }, session =>
		{
			var ended = context.Repository.GetBySessionIdAsync(session.SessionId, CancellationToken.None).GetAwaiter().GetResult();
			Assert.NotNull(ended?.EndedUtc);
			Assert.Equal(UserSessionEndReason.AdministrativeLogout, ended?.EndReason);
		});
		var snapshot = await context.Service.GetSnapshotAsync(CancellationToken.None);
		Assert.Empty(snapshot.Sessions);
		Assert.Equal(2, snapshot.History.Count);
	}

	[Fact]
	public async Task HistoryShowsEndedReasonAndDuration()
	{
		var context = await CreateContextAsync();
		context.Authorization.SignIn(new User { Id = 1, DisplayName = "Admin", IsActive = true }, new HashSet<ApplicationPermission> { ApplicationPermission.UsersView, ApplicationPermission.UserSessionsTerminate });
		var session = NewSession(context.UserId, context.Clock.UtcNow.AddHours(-1), "HISTORY-PC");
		await context.Repository.CreateAsync(session, CancellationToken.None);
		await context.Repository.EndAsync(session.SessionId, context.Clock.UtcNow, UserSessionEndReason.LoggedOut, CancellationToken.None);
		var snapshot = await context.Service.GetSnapshotAsync(CancellationToken.None);
		var ended = Assert.Single(snapshot.History);
		Assert.Equal(UserSessionEndReason.LoggedOut, ended.EndReason);
		var row = new UserSessionHistoryRowViewModel(ended);
		Assert.Equal("1h 0m", row.Duration);
		Assert.Equal("LoggedOut", row.EndReason);
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		UserSessionSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var users = new UserRepository(access);
		var user = new User { Email = "presence@test.local", DisplayName = "Presence User", IsActive = true, CreatedUtc = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc) };
		var userId = await users.CreateAsync(user, "unused", CancellationToken.None);
		var repository = new UserSessionRepository(access);
		var authorization = new AuthorizationService();
		var clock = new MutableTimeProvider { UtcNow = user.CreatedUtc };
		var service = new UserSessionAdministrationService(repository, authorization, clock);
		return new TestContext(access, repository, authorization, service, clock, userId);
	}

	private static UserSession NewSession(long userId, DateTime now, string machineName) => new()
	{
		SessionId = Guid.NewGuid(), UserId = userId, StartedUtc = now, LastSeenUtc = now,
		ClientInstanceId = Guid.NewGuid(), MachineName = machineName, AppVersion = "0.15.88-preview"
	};

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(DatabaseAccess Access, UserSessionRepository Repository, AuthorizationService Authorization, UserSessionAdministrationService Service, MutableTimeProvider Clock, long UserId);
	private sealed class MutableTimeProvider : TimeProvider
	{
		public DateTime UtcNow { get; set; }
		public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
	}
}
