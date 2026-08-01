// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class AuditLogTests
{
	[Fact]
	public void SanitizerMasksNestedSecretsAndBuildsSafeComparison()
	{
		var sanitizer = new AuditJsonSanitizer();
		const string before = """{"name":"Depot","password":"old","nested":{"connectionString":"Server=db;Password=x","value":1}}""";
		const string after = """{"name":"Depot 2","password":"new","nested":{"connectionString":"Server=db2;Password=y","value":2}}""";

		var sanitized = sanitizer.Sanitize(after);
		var changes = sanitizer.Compare(before, after);

		Assert.DoesNotContain("new", sanitized, StringComparison.Ordinal);
		Assert.DoesNotContain("Server=db2", sanitized, StringComparison.Ordinal);
		Assert.Contains("[REDACTED]", sanitized, StringComparison.Ordinal);
		Assert.DoesNotContain(changes, change => change.Property.Contains("password", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(changes, change => change.Property == "$.name" && change.After == "Depot 2");
	}

	[Fact]
	public void SanitizerHidesInvalidPayload()
	{
		var result = new AuditJsonSanitizer().Sanitize("password=plain-text-secret");

		Assert.Equal("[Invalid audit payload hidden]", result);
		Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task AuditLogIsPagedFilteredSanitizedAndHistoricallyReadable()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-audit-{Guid.NewGuid():N}.db");
		try
		{
			var database = new DatabaseAccess(new SqliteConnectionFactory(path));
			new DepotDatabase(new SqliteConnectionFactory(path)).Initialize();
			var repository = new AuditRepository(database);
			await repository.CreateAsync(new AuditEntry
			{
				TimestampUtc = DateTime.UtcNow,
				UserEmail = "deleted.user@example.test",
				EntityType = "Supplier",
				EntityId = 42,
				Action = "Updated",
				BeforeJson = "{\"passwordHash\":\"old-secret\",\"name\":\"Before\"}",
				AfterJson = "{\"passwordHash\":\"new-secret\",\"name\":\"After\"}"
			}, CancellationToken.None);
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = 1, Email = "admin@example.test", IsAdministrator = true, IsActive = true });
			var service = new AuditLogService(repository, authorization, new AuditJsonSanitizer());

			var page = await service.SearchAsync(
				new AuditLogFilter(null, null, null, "deleted.user", "Supplier", "Updated", 42),
				1, 20, CancellationToken.None);
			var details = await service.GetDetailsAsync(page.Items.Single().Id, CancellationToken.None);

			Assert.Equal(1, page.TotalCount);
			Assert.Equal("deleted.user@example.test", page.Items[0].UserEmail);
			Assert.NotNull(details);
			Assert.DoesNotContain("old-secret", details.BeforeJson, StringComparison.Ordinal);
			Assert.DoesNotContain("new-secret", details.AfterJson, StringComparison.Ordinal);
			Assert.Contains(details.Changes, change => change.Property == "$.name");
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[Fact]
	public async Task AuditLogRejectsNonAdministrators()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-audit-auth-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = 2, Email = "user@example.test", IsAdministrator = false, IsActive = true });
			var service = new AuditLogService(
				new AuditRepository(new DatabaseAccess(factory)), authorization, new AuditJsonSanitizer());

			await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(
				new AuditLogFilter(null, null, null, null, null, null, null), 1, 20, CancellationToken.None));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}
}
