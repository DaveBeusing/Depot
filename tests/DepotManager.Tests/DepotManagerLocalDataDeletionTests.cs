using System.IO;
using Depot.Models;
using Depot.Repositories;
using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class DepotManagerLocalDataDeletionTests
{
	[Fact]
	public void DeleteDirectory_RemovesLocalDataRecursively()
	{
		var root = Path.Combine(Path.GetTempPath(), "DepotManagerLocalDataDeletionTests", Guid.NewGuid().ToString("N"));
		var dataDirectory = Path.Combine(root, "Data");
		var logDirectory = Path.Combine(root, "Logs");
		Directory.CreateDirectory(dataDirectory);
		Directory.CreateDirectory(logDirectory);
		File.WriteAllText(Path.Combine(dataDirectory, "depot.db"), "sqlite-data");
		File.WriteAllText(Path.Combine(logDirectory, "DepotManager.log"), "log-data");

		LocalDataDeletion.DeleteDirectory(root);

		Assert.False(Directory.Exists(root));
	}

	[Fact]
	public void DeleteDirectory_MissingDirectoryIsNoOp()
	{
		var root = Path.Combine(Path.GetTempPath(), "DepotManagerLocalDataDeletionTests", Guid.NewGuid().ToString("N"));
		LocalDataDeletion.DeleteDirectory(root);
		Assert.False(Directory.Exists(root));
	}

	[Fact]
	public void DeleteSqliteDatabaseFiles_RemovesDatabaseAndSidecars()
	{
		var root = Path.Combine(Path.GetTempPath(), "DepotManagerLocalDataDeletionTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var databasePath = Path.Combine(root, "custom.db");
		foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" })
			File.WriteAllText(path, "data");

		try
		{
			LocalDataDeletion.DeleteSqliteDatabaseFiles(databasePath);
			Assert.Empty(Directory.EnumerateFiles(root));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void InstalledSettingsInspector_ResolvesConfiguredLocalDatabasePath()
	{
		var root = Path.Combine(Path.GetTempPath(), "DepotManagerLocalDataDeletionTests", Guid.NewGuid().ToString("N"));
		var installDirectory = Path.Combine(root, "Install");
		Directory.CreateDirectory(installDirectory);
		var settingsPath = Path.Combine(installDirectory, "depot.settings");
		var customDatabasePath = Path.Combine(root, "CustomData", "depot-custom.db");
		new SettingsRepository(settingsPath).Save(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.Local,
			LocalDatabasePath = customDatabasePath
		});

		try
		{
			Assert.Equal(Path.GetFullPath(customDatabasePath), InstalledSettingsInspector.GetLocalDatabasePath(settingsPath, installDirectory));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void InstalledSettingsInspector_DoesNotReturnRemoteDatabaseTargets()
	{
		var root = Path.Combine(Path.GetTempPath(), "DepotManagerLocalDataDeletionTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var settingsPath = Path.Combine(root, "depot.settings");
		new SettingsRepository(settingsPath).Save(new DatabaseConnectionSettings { Provider = DatabaseProvider.MySql });

		try
		{
			Assert.Null(InstalledSettingsInspector.GetLocalDatabasePath(settingsPath, root));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
