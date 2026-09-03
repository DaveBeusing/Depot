// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

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

	[Fact]
	public void CheckboxResourceDictionaryLoadsWithoutMissingStaticResources()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var dictionary = (ResourceDictionary)Application.LoadComponent(
					new Uri("/Depot;component/Resources/CheckBox.xaml", UriKind.Relative));

				Assert.True(dictionary.Contains("AppCheckBoxControlStyle"));
				Assert.True(dictionary.Contains("AppDataGridCheckBoxElementStyle"));
				Assert.True(dictionary.Contains("AppDataGridCheckBoxEditingElementStyle"));
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		Assert.Null(failure);
	}

	[Fact]
	public void TypographyDefinesThemeAwareDefaultTextForeground()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var dictionary = (ResourceDictionary)Application.LoadComponent(
					new Uri("/Depot;component/Resources/Typography.xaml", UriKind.Relative));

				var style = Assert.IsType<Style>(dictionary[typeof(TextBlock)]);
				Assert.Contains(
					style.Setters.OfType<Setter>(),
					setter => setter.Property == TextBlock.ForegroundProperty);
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		Assert.Null(failure);
	}

	[Fact]
	public void XamlDoesNotHardCodeBlackTextForegrounds()
	{
		var repositoryRoot = FindRepositoryRoot();
		var sourceRoot = Path.Combine(repositoryRoot, "src", "Depot");
		var directForegroundPattern = new Regex(
			@"(?:^|\s)(?:TextElement\.)?Foreground\s*=\s*[""']\s*(?:Black|#(?:000|000000|FF000000))\s*[""']",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromSeconds(1));
		var setterPattern = new Regex(
			@"<Setter\b(?=[^>]*\bProperty\s*=\s*[""'](?:TextElement\.)?Foreground[""'])(?=[^>]*\bValue\s*=\s*[""']\s*(?:Black|#(?:000|000000|FF000000))\s*[""'])[^>]*>",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromSeconds(1));

		var violations = Directory
			.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
			.SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
			.Where(entry => directForegroundPattern.IsMatch(entry.line) || setterPattern.IsMatch(entry.line))
			.Select(entry => $"{Path.GetRelativePath(repositoryRoot, entry.path)}:{entry.lineNumber}: {entry.line.Trim()}")
			.ToArray();

		Assert.True(
			violations.Length == 0,
			"Hard-coded black text foregrounds were found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
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

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
			{
				return directory.FullName;
			}
		}

		throw new DirectoryNotFoundException("Could not locate the Depot repository root from the test output directory.");
	}
}
