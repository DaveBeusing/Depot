using Depot.Models;
using DepotManager;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class ManagerDatabaseConnectionValidatorTests
{
	[Fact]
	public async Task ValidateAsync_ValidatesSqliteWithoutLaunchingDepot()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"depot-manager-validate-{Guid.NewGuid():N}");
		var databasePath = Path.Combine(directory, "depot.db");
		Directory.CreateDirectory(directory);

		try
		{
			var settings = new DatabaseConnectionSettings
			{
				Provider = DatabaseProvider.Local,
				LocalDatabasePath = databasePath
			};

			var validator = new ManagerDatabaseConnectionValidator();
			await validator.ValidateAsync(settings, CancellationToken.None);

			Assert.True(File.Exists(databasePath));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (Directory.Exists(directory)) Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task ValidateAsync_RejectsMissingRemoteHostBeforeConnecting()
	{
		var settings = new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.MySql,
			MySqlHost = string.Empty,
			MySqlPort = 3306,
			MySqlDatabase = "Depot",
			MySqlUserName = "depot"
		};

		var validator = new ManagerDatabaseConnectionValidator();
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(settings, CancellationToken.None));
		Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
	}
}
