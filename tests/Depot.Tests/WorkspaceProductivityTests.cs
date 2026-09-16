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

public sealed class WorkspaceProductivityTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-workspace-productivity-{Guid.NewGuid():N}.db");

	[Fact]
	public void VersionOneMigratesToVersionTwoIdempotently()
	{
		var factory = Initialize();
		UserPreferenceSchemaMigration.Migrate(factory);
		using (var connection = Open())
		{
			Execute(connection, "DROP TABLE IF EXISTS UserWorkspacePreferences;");
			Execute(connection, "DROP TABLE IF EXISTS UserWorkspaceRecents;");
			Execute(connection, "DROP TABLE IF EXISTS UserWorkspaceFavorites;");
			Execute(connection, "UPDATE DepotFeatureVersions SET Version=1 WHERE Name='UserPreferences';");
		}

		UserPreferenceSchemaMigration.Migrate(factory);
		UserPreferenceSchemaMigration.Migrate(factory);

		using var migrated = Open();
		Assert.Equal(1, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserWorkspaceFavorites';"));
		Assert.Equal(1, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserWorkspaceRecents';"));
		Assert.Equal(1, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserWorkspacePreferences';"));
		Assert.Equal(1, Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserWorkspaceViews';"));
		Assert.Equal(2, Scalar(migrated, "SELECT Version FROM DepotFeatureVersions WHERE Name='UserPreferences';"));
	}

	[Fact]
	public async Task PreferencesAreScopedToCurrentUserAndSurviveReload()
	{
		var context = CreateContext();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);
		await context.Service.PinAsync("inventory.items");
		await context.Service.RecordVisitAsync("sales.orders");
		await context.Service.SetDefaultLandingAsync("inventory.items");

		var firstRead = await context.Service.GetAsync();
		Assert.Contains("inventory.items", firstRead.FavoriteRoutes);
		Assert.Contains("sales.orders", firstRead.RecentRoutes);
		Assert.Equal("inventory.items", firstRead.DefaultLandingRoute);

		var second = new User
		{
			Email = $"productivity-{Guid.NewGuid():N}@example.test",
			DisplayName = "Productivity User",
			Role = UserRole.User,
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};
		second.Id = await context.Users.CreateAsync(second, "test-password-hash", CancellationToken.None);
		context.Authorization.SignIn(second);
		var isolated = await context.Service.GetAsync();
		Assert.Empty(isolated.FavoriteRoutes);
		Assert.Empty(isolated.RecentRoutes);
		Assert.Null(isolated.DefaultLandingRoute);

		context.Authorization.SignIn(admin);
		var reloaded = await new WorkspaceProductivityService(
			new DatabaseTransactionRunner(context.Access),
			new WorkspaceProductivityRepository(context.Access),
			context.Authorization).GetAsync();
		Assert.Contains("inventory.items", reloaded.FavoriteRoutes);
		Assert.Equal("inventory.items", reloaded.DefaultLandingRoute);
	}

	[Fact]
	public async Task RecentsAreDeduplicatedAndBounded()
	{
		var context = CreateContext();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);

		for (var index = 0; index < 15; index++)
			await context.Service.RecordVisitAsync($"test.route.{index:00}");
		await context.Service.RecordVisitAsync("test.route.05");

		var snapshot = await context.Service.GetAsync();
		Assert.Equal(WorkspaceProductivityService.MaximumRecents, snapshot.RecentRoutes.Count);
		Assert.Equal(snapshot.RecentRoutes.Count, snapshot.RecentRoutes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		Assert.Equal("test.route.05", snapshot.RecentRoutes[0]);
	}

	[Fact]
	public async Task FavoritesAreBoundedAndCanBeUnpinned()
	{
		var context = CreateContext();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);

		for (var index = 0; index < WorkspaceProductivityService.MaximumFavorites; index++)
			await context.Service.PinAsync($"favorite.route.{index:00}");
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.PinAsync("favorite.route.99"));

		await context.Service.UnpinAsync("favorite.route.00");
		await context.Service.PinAsync("favorite.route.99");
		var snapshot = await context.Service.GetAsync();
		Assert.Equal(WorkspaceProductivityService.MaximumFavorites, snapshot.FavoriteRoutes.Count);
		Assert.DoesNotContain("favorite.route.00", snapshot.FavoriteRoutes);
		Assert.Contains("favorite.route.99", snapshot.FavoriteRoutes);
	}

	[Fact]
	public async Task InvalidSemanticRoutesAreRejected()
	{
		var context = CreateContext();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);

		await Assert.ThrowsAsync<ArgumentException>(() => context.Service.PinAsync("inventory/items?all=true"));
		await Assert.ThrowsAsync<ArgumentException>(() => context.Service.RecordVisitAsync(new string('x', 161)));
	}

	private TestContext CreateContext()
	{
		var factory = Initialize();
		UserPreferenceSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var users = new UserRepository(access);
		var authorization = new AuthorizationService();
		var service = new WorkspaceProductivityService(new DatabaseTransactionRunner(access), new WorkspaceProductivityRepository(access), authorization);
		return new TestContext(access, users, authorization, service);
	}

	private SqliteConnectionFactory Initialize()
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

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	private static int Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		DatabaseAccess Access,
		UserRepository Users,
		AuthorizationService Authorization,
		WorkspaceProductivityService Service);
}
