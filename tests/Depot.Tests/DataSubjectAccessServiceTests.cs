// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class DataSubjectAccessServiceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-privacy-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;
	private readonly DatabaseAccess _database;

	public DataSubjectAccessServiceTests()
	{
		_factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(_factory).Initialize();
		SalesSchemaMigration.Migrate(_factory);
		_database = new DatabaseAccess(_factory);
	}

	[Fact]
	public async Task SearchRequiresAdministrationPermission()
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 42, IsActive = true }, [ApplicationPermission.CustomersView]);
		var service = new DataSubjectAccessService(_database, authorization);

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync("alice"));
	}

	[Fact]
	public async Task SearchFindsPrimaryPersonalDataWithoutAuthenticationSecrets()
	{
		await _database.InsertAsync(
			"INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc) VALUES ('alice@example.test', 'Alice Example', 'TOP-SECRET-HASH', 0, 0, 0, 1, '2026-01-01T00:00:00Z');",
			CancellationToken.None);
		await _database.InsertAsync(
			"INSERT INTO Customers (CustomerNumber,Name,ContactName,Email,Phone,PaymentTermsDays,Currency,IsActive) VALUES ('CU-900001','Example GmbH','Alice Example','alice@example.test','+49 123',30,'EUR',1);",
			CancellationToken.None);
		var authorization = AdministratorAuthorization();
		var service = new DataSubjectAccessService(_database, authorization);

		var result = await service.SearchAsync("alice");
		var json = await service.CreateJsonExportAsync("alice");

		Assert.Contains(result.Records, record => record.Source == "Users" && record.Email == "alice@example.test");
		Assert.Contains(result.Records, record => record.Source == "Customers" && record.Email == "alice@example.test");
		Assert.DoesNotContain("TOP-SECRET-HASH", json, StringComparison.Ordinal);
		Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task SearchRejectsUnboundedOneCharacterQueries()
	{
		var service = new DataSubjectAccessService(_database, AdministratorAuthorization());
		await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync("a"));
	}

	private static AuthorizationService AdministratorAuthorization()
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, Email = "privacy-admin@depot.test", IsActive = true }, [ApplicationPermission.AdministrationView]);
		return authorization;
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
