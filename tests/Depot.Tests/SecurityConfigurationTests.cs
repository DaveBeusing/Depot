// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class SecurityConfigurationTests
{
	[Fact]
	public void SqlServerConfigurationRejectsUnencryptedTransport()
	{
		var service = new SettingsService(new SettingsRepository($"test-{Guid.NewGuid():N}.settings"));
		var settings = new DatabaseConnectionSettings { Provider = DatabaseProvider.SqlServer, SqlServerHost = "db.example.test", SqlServerDatabase = "Depot", SqlServerUserName = "depot", SqlServerPassword = "secret", EncryptSqlServerConnection = false };
		Assert.Throws<ArgumentException>(() => service.Validate(settings));
	}

	[Fact]
	public void MySqlConfigurationRejectsDisabledTls()
	{
		var service = new SettingsService(new SettingsRepository($"test-{Guid.NewGuid():N}.settings"));
		var settings = new DatabaseConnectionSettings { Provider = DatabaseProvider.MySql, MySqlHost = "db.example.test", MySqlDatabase = "Depot", MySqlUserName = "depot", MySqlPassword = "secret", UseMySqlTls = false };
		Assert.Throws<ArgumentException>(() => service.Validate(settings));
	}

	[Fact]
	public void AuditSanitizerRedactsCommonSecretNames()
	{
		var sanitized = new AuditJsonSanitizer().Sanitize("{\"ApiKey\":\"abc\",\"ClientSecret\":\"def\",\"Name\":\"visible\"}");
		Assert.DoesNotContain("abc", sanitized, StringComparison.Ordinal);
		Assert.DoesNotContain("def", sanitized, StringComparison.Ordinal);
		Assert.Contains("visible", sanitized, StringComparison.Ordinal);
	}
}
