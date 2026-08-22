// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class LegacyAdministratorRetirementTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-legacy-admin-{Guid.NewGuid():N}.db");
	private readonly DatabaseAccess _data;

	public LegacyAdministratorRetirementTests()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		_data = new DatabaseAccess(factory);
	}

	[Fact]
	public async Task RetirementDisablesSeedAccountBeforeInteractiveStartup()
	{
		Assert.Equal(1, LegacyAdministratorRetirement.Retire(_data));
		var legacy = await new UserRepository(_data).GetByEmailAsync(AdministratorBootstrapService.LegacyAdministratorEmail, CancellationToken.None);
		Assert.NotNull(legacy);
		Assert.False(legacy!.IsActive);
		Assert.Equal(0, LegacyAdministratorRetirement.Retire(_data));
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
