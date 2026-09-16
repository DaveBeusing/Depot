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

public sealed class WorkspaceViewPersistenceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-workspace-views-{Guid.NewGuid():N}.db");

	[Fact]
	public void FeatureSchemaIsCreatedAndMigrationIsIdempotent()
	{
		var factory = Initialize();
		UserPreferenceSchemaMigration.Migrate(factory);
		UserPreferenceSchemaMigration.Migrate(factory);

		using var connection = Open();
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserWorkspaceViews';"));
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserWorkspaceDefaultViews';"));
		Assert.Equal(UserPreferenceSchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='UserPreferences';"));
	}

	[Fact]
	public async Task SavedViewsAreScopedToCurrentUserAndWorkspace()
	{
		var context = await CreateContextAsync();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);
		var definition = Definition("inventory.part-number", "inventory.search", "camera");

		var saved = await context.Service.SaveAsync("inventory.overview", null, "My inventory", definition, null);
		Assert.Equal("My inventory", saved.Name);
		Assert.Single(await context.Service.ListAsync("inventory.overview"));
		Assert.Empty(await context.Service.ListAsync("inventory.traceability"));

		var second = new User
		{
			Email = $"second-{Guid.NewGuid():N}@example.test",
			DisplayName = "Second User",
			Role = UserRole.User,
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};
		second.Id = await context.Users.CreateAsync(second, "test-password-hash", CancellationToken.None);
		context.Authorization.SignIn(second);
		Assert.Empty(await context.Service.ListAsync("inventory.overview"));
		await context.Service.SaveAsync("inventory.overview", null, "My inventory", definition, null);
		Assert.Single(await context.Service.ListAsync("inventory.overview"));

		context.Authorization.SignIn(admin);
		var adminViews = await context.Service.ListAsync("inventory.overview");
		Assert.Single(adminViews);
		Assert.Equal(saved.ViewId, adminViews[0].ViewId);
	}

	[Fact]
	public async Task DefaultUpdateDeleteAndOptimisticConcurrencyAreEnforced()
	{
		var context = await CreateContextAsync();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);
		var first = await context.Service.SaveAsync("inventory.overview", null, "Operations", Definition("inventory.stock", "inventory.search", null), null);
		var second = await context.Service.SaveAsync("inventory.overview", null, "Planning", Definition("inventory.description", "inventory.search", "switcher"), null);

		await context.Service.SetDefaultAsync("inventory.overview", second.ViewId);
		var views = await context.Service.ListAsync("inventory.overview");
		Assert.False(views.Single(view => view.ViewId == first.ViewId).IsDefault);
		Assert.True(views.Single(view => view.ViewId == second.ViewId).IsDefault);

		var updated = await context.Service.SaveAsync("inventory.overview", first.ViewId, "Operations daily", Definition("inventory.stock", "inventory.search", "rack"), first.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.SaveAsync("inventory.overview", first.ViewId, "Stale", Definition("inventory.stock", "inventory.search", null), first.Version));
		await context.Service.DeleteAsync("inventory.overview", updated.ViewId, updated.Version);
		Assert.DoesNotContain((await context.Service.ListAsync("inventory.overview")), view => view.ViewId == updated.ViewId);
	}

	[Fact]
	public async Task InvalidOrFutureDefinitionsFailSafeWithoutBreakingWorkspace()
	{
		var context = await CreateContextAsync();
		var admin = await context.Users.GetByIdAsync(1, CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
		context.Authorization.SignIn(admin);
		await context.Access.ExecuteAsync(
			"INSERT INTO UserWorkspaceViews (UserId, WorkspaceId, ViewId, Name, DefinitionJson, UpdatedUtc, Version) VALUES (1, $WorkspaceId, $ViewId, 'Broken', $DefinitionJson, $UpdatedUtc, 1);",
			CancellationToken.None,
			new DatabaseParameter("$WorkspaceId", "inventory.overview"),
			new DatabaseParameter("$ViewId", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture)),
			new DatabaseParameter("$DefinitionJson", "{\"formatVersion\":999}"),
			new DatabaseParameter("$UpdatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));

		Assert.Empty(await context.Service.ListAsync("inventory.overview"));
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = Initialize();
		UserPreferenceSchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var users = new UserRepository(access);
		var authorization = new AuthorizationService();
		var service = new WorkspaceViewService(new DatabaseTransactionRunner(access), new WorkspaceViewRepository(access), authorization);
		await Task.CompletedTask;
		return new TestContext(access, users, authorization, service);
	}

	private SqliteConnectionFactory Initialize()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		return factory;
	}

	private static WorkspaceViewDefinition Definition(string columnId, string filterId, string? filterValue) => new()
	{
		GridDensity = WorkspaceGridDensity.Compact,
		Columns = [new WorkspaceColumnPreference(columnId, 0, 140, true)],
		Sorts = [new WorkspaceSortPreference(columnId, WorkspaceSortDirection.Ascending, 0)],
		Filters = [new WorkspaceFilterPreference(filterId, filterValue)]
	};

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

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		DatabaseAccess Access,
		UserRepository Users,
		AuthorizationService Authorization,
		WorkspaceViewService Service);
}
