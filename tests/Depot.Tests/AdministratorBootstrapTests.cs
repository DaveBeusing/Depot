// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class AdministratorBootstrapTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-bootstrap-{Guid.NewGuid():N}.db");
	private readonly DatabaseAccess _data;

	public AdministratorBootstrapTests()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		_data = new DatabaseAccess(factory);
	}

	[Fact]
	public async Task LegacyDefaultAdministratorForcesSecureBootstrapAndIsRetired()
	{
		var authorization = new AuthorizationService();
		var service = new AdministratorBootstrapService(_data, new DatabaseTransactionRunner(_data), authorization);
		Assert.True(await service.RequiresSetupAsync(CancellationToken.None));

		var created = await service.CreateAdministratorAsync("owner@depot.test", "Depot Owner", "Secure-Depot-92!Admin", CancellationToken.None);
		Assert.True(created.IsAdministrator);
		Assert.False(await service.RequiresSetupAsync(CancellationToken.None));

		var legacy = await new UserRepository(_data).GetByEmailAsync(AdministratorBootstrapService.LegacyAdministratorEmail, CancellationToken.None);
		Assert.NotNull(legacy);
		Assert.False(legacy!.IsActive);
		var permissions = await new RoleRepository(_data).GetEffectivePermissionsAsync(created.Id, CancellationToken.None);
		Assert.Equal(PermissionCatalog.All.Count, permissions.Count);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
