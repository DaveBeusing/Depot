using System.IO;
using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class PortableExecutableValidatorTests
{
	[Fact]
	public void ValidateWindowsExecutable_AcceptsNativeWindowsAppHost()
	{
		var processPath = Environment.ProcessPath;
		Assert.False(string.IsNullOrWhiteSpace(processPath));
		PortableExecutableValidator.ValidateWindowsExecutable(processPath!);
	}

	[Fact]
	public void ValidateWindowsExecutable_RejectsNonPeFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-manager-invalid-{Guid.NewGuid():N}.exe");
		try
		{
			File.WriteAllText(path, "not-a-windows-executable");
			Assert.Throws<InvalidOperationException>(() => PortableExecutableValidator.ValidateWindowsExecutable(path));
		}
		finally
		{
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[Fact]
	public void ValidateWindowsExecutable_RejectsTruncatedPeFile()
	{
		var processPath = Environment.ProcessPath;
		Assert.False(string.IsNullOrWhiteSpace(processPath));
		var path = Path.Combine(Path.GetTempPath(), $"depot-manager-truncated-{Guid.NewGuid():N}.exe");
		try
		{
			File.Copy(processPath!, path);
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
			{
				Assert.True(stream.Length > 1024);
				stream.SetLength(1024);
			}

			Assert.Throws<InvalidOperationException>(() => PortableExecutableValidator.ValidateWindowsExecutable(path));
		}
		finally
		{
			if (File.Exists(path)) File.Delete(path);
		}
	}
}
