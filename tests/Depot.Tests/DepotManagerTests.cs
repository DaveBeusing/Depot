using DepotManager;

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
}
