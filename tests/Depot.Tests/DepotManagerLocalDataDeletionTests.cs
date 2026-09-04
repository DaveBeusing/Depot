using System.IO;
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
}
