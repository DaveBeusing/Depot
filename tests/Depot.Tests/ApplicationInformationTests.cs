// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;

using Depot.Data;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ApplicationInformationTests
{
	[Fact]
	public void DepotAssemblyExposesConsistentVersionInformation()
	{
		var service = new ApplicationInformationService(typeof(App).Assembly);

		var information = service.GetVersionInfo();

		Assert.Equal("Depot", information.ProductName);
		Assert.Matches(
			new Regex(
				@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$",
				RegexOptions.CultureInvariant,
				TimeSpan.FromSeconds(1)),
			information.Version);
		Assert.NotEqual("—", information.AssemblyVersion);
		Assert.NotEqual("—", information.FileVersion);
		Assert.Equal(DatabaseVersion.CurrentVersion, information.DatabaseSchemaVersion);
		Assert.Equal("MIT", information.License);
		Assert.Equal("https://github.com/DaveBeusing/Depot", information.RepositoryUrl);
	}

	[Theory]
	[InlineData("1.0.0", "Stable")]
	[InlineData("1.0.0-dev.1", "Development")]
	[InlineData("1.0.0-alpha.1", "Alpha")]
	[InlineData("1.0.0-beta.1", "Beta")]
	[InlineData("1.0.0-preview.1", "Preview")]
	[InlineData("1.0.0-rc.1", "Release Candidate")]
	[InlineData("1.0.0-custom.1", "Pre-release")]
	public void ReleaseChannelIsDerivedFromSemanticVersion(string version, string expectedChannel)
	{
		Assert.Equal(expectedChannel, ApplicationInformationService.GetReleaseChannel(version));
	}
}
