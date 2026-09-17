using System.IO;
using System.Threading;
using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class ExecutableDeploymentRetryTests
{
	[Fact]
	public void Replace_RetriesUntilTemporaryTargetLockIsReleased()
	{
		var root = Path.Combine(Path.GetTempPath(), "DepotManagerTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var depot = Path.Combine(root, "Depot.exe");
			var download = Path.Combine(root, "download.exe");
			File.WriteAllText(depot, "current");
			File.WriteAllText(download, "new");

			using var locked = new FileStream(depot, FileMode.Open, FileAccess.Read, FileShare.None);
			var releaseThread = new Thread(() =>
			{
				Thread.Sleep(150);
				locked.Dispose();
			})
			{
				IsBackground = true
			};
			releaseThread.Start();
			try
			{
				ExecutableDeployment.Replace(download, depot);
			}
			finally
			{
				locked.Dispose();
				releaseThread.Join();
			}

			Assert.Equal("new", File.ReadAllText(depot));
			Assert.False(File.Exists(depot + ".new"));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
