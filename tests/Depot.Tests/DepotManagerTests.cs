using System.IO;
using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class DepotManagerTests
{
	[Theory]
	[InlineData("0.13.28", 0, 13, 28)]
	[InlineData("v1.2.3", 1, 2, 3)]
	public void ReleaseTag_ParsesSupportedSemanticVersions(string tag, int major, int minor, int build)
	{
		Assert.True(VersionRules.TryParseReleaseTag(tag, out var version));
		Assert.Equal(major, version.Major);
		Assert.Equal(minor, version.Minor);
		Assert.Equal(build, version.Build);
	}

	[Theory]
	[InlineData("")]
	[InlineData("latest")]
	[InlineData("0.13.x")]
	public void ReleaseTag_RejectsInvalidTags(string tag) => Assert.False(VersionRules.TryParseReleaseTag(tag, out _));

	[Fact]
	public void AssetName_UsesReleaseConvention() => Assert.Equal("Depot-0.13.28.exe", VersionRules.AssetName(new Version(0, 13, 28)));

	[Fact]
	public void BackupName_DropsFileVersionRevision() => Assert.Equal("Depot-0.13.27.exe", VersionRules.BackupName(new Version(0, 13, 27, 0)));

	[Fact]
	public void ReleaseVersion_DropsFileVersionRevision() => Assert.Equal(new Version(0, 15, 128), VersionRules.ReleaseVersion(new Version(0, 15, 128, 0)));

	[Fact]
	public void UpdateComparison_OnlyOffersNewerVersion()
	{
		Assert.True(VersionRules.IsUpdate(new Version(0, 13, 27, 0), new Version(0, 13, 28)));
		Assert.False(VersionRules.IsUpdate(new Version(0, 13, 28, 0), new Version(0, 13, 28)));
		Assert.False(VersionRules.IsUpdate(new Version(0, 13, 28, 0), new Version(0, 13, 27)));
	}

	[Theory]
	[InlineData(0, 0, 0, true, "Lokal (sqlite3)")]
	[InlineData(1, 2, 3306, false, "Remote (MySQL/MariaDB)")]
	[InlineData(2, 1, 1433, false, "Remote (SQL)")]
	public void DatabaseSelection_MapsManagerChoicesToDepotProviders(int selection, int provider, int port, bool requiresAdministrator, string displayName)
	{
		Assert.Equal(provider, ManagerDatabaseProviderSelection.ToDepotProviderIndex(selection));
		Assert.Equal(port, ManagerDatabaseProviderSelection.DefaultPort(selection));
		Assert.Equal(requiresAdministrator, ManagerDatabaseProviderSelection.RequiresAdministratorStep(selection));
		Assert.Equal(displayName, ManagerDatabaseProviderSelection.DisplayName(selection));
	}

	[Fact]
	public void BackupCurrent_KeepsOnlyPreviousExecutableAndLeavesDataUntouched()
	{
		var root = CreateTempDirectory();
		try
		{
			var depot = Path.Combine(root, "Depot.exe");
			var backup = Path.Combine(root, "Backup");
			var settings = Path.Combine(root, "depot.settings");
			var businessData = Path.Combine(root, "business.db");
			File.WriteAllText(depot, "version-27");
			Directory.CreateDirectory(backup);
			File.WriteAllText(Path.Combine(backup, "Depot-0.13.26.exe"), "old-backup");
			File.WriteAllText(settings, "protected-settings");
			File.WriteAllText(businessData, "business-data");

			ExecutableDeployment.BackupCurrent(depot, backup, new Version(0, 13, 27, 0));

			var backupNames = Directory.EnumerateFiles(backup).Select(Path.GetFileName).ToArray();
			Assert.Single(backupNames);
			Assert.Equal("Depot-0.13.27.exe", backupNames[0]);
			Assert.Equal("version-27", File.ReadAllText(Path.Combine(backup, "Depot-0.13.27.exe")));
			Assert.Equal("protected-settings", File.ReadAllText(settings));
			Assert.Equal("business-data", File.ReadAllText(businessData));
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void Replace_MissingDownloadLeavesInstalledExecutableUntouched()
	{
		var root = CreateTempDirectory();
		try
		{
			var depot = Path.Combine(root, "Depot.exe");
			File.WriteAllText(depot, "current");
			Assert.Throws<FileNotFoundException>(() => ExecutableDeployment.Replace(Path.Combine(root, "missing.exe"), depot));
			Assert.Equal("current", File.ReadAllText(depot));
			Assert.False(File.Exists(depot + ".new"));
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void Replace_StagesAndReplacesExecutable()
	{
		var root = CreateTempDirectory();
		try
		{
			var depot = Path.Combine(root, "Depot.exe");
			var download = Path.Combine(root, "download.exe");
			File.WriteAllText(depot, "current");
			File.WriteAllText(download, "new");

			ExecutableDeployment.Replace(download, depot);

			Assert.Equal("new", File.ReadAllText(depot));
			Assert.False(File.Exists(depot + ".new"));
		}
		finally { Directory.Delete(root, true); }
	}

	private static string CreateTempDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), "DepotManagerTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}
}
