using System.Net;
using System.Net.Http;
using System.Text;
using Depot.Models;
using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class DepotManagerCoverageGapTests
{
    [Fact]
    public void InstallationStateRules_DetectsReachableDatabaseWithoutSchemaAsIncomplete()
    {
        var state = InstallationStateRules.Determine(
            registryPresent: true,
            depotPresent: true,
            depotValid: true,
            managerPresent: true,
            settingsPresent: true,
            settingsReadable: true,
            databaseReachable: true,
            databaseSchemaVersion: null,
            supportedSchemaVersion: 30,
            windowsIntegrationHealthy: true);

        Assert.Equal(InstallationHealthState.InstallationIncomplete, state);
    }

    [Fact]
    public void InstallationInspector_DescribesEveryKnownHealthState()
    {
        var states = Enum.GetValues<InstallationHealthState>();
        var messages = states.Select(InstallationInspector.DescribeState).ToArray();

        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Equal(states.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ManagerReleaseClient_SelectsNewestValidStableManagerAsset()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            [
              {"draft":true,"prerelease":false,"tag_name":"0.15.146","assets":[{"name":"DepotManager-0.15.146.exe","size":146,"browser_download_url":"https://example.test/146.exe"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.145","assets":[{"name":"DepotManager-0.15.145.exe","size":145,"browser_download_url":"http://example.test/145.exe"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.144","assets":[{"name":"DepotManager-0.15.144.exe","size":144,"browser_download_url":"https://example.test/144.exe","digest":"sha256:ABCDEF"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.143","assets":[{"name":"DepotManager-0.15.143.exe","size":143,"browser_download_url":"https://example.test/143.exe"}]}
            ]
            """));
        using var http = new HttpClient(handler);

        var release = await new ManagerReleaseClient(http).GetLatestAsync(CancellationToken.None);

        Assert.Equal(new Version(0, 15, 144), release.Version);
        Assert.Equal("ABCDEF", release.Sha256);
        Assert.Equal("0.15.144", release.ReleaseTag);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ManagerReleaseClient_RejectsInsecureDownloadBeforeNetworkAccess()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Network access was not expected."));
        using var http = new HttpClient(handler);
        var release = new ManagerReleaseInfo(new Version(0, 15, 144), new Uri("http://example.test/manager.exe"), 10, null, "0.15.144");
        var destination = Path.Combine(CreateTempDirectory(), "DepotManager.exe");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagerReleaseClient(http).DownloadAsync(release, destination, null, CancellationToken.None));
            Assert.Equal(0, handler.CallCount);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(destination)!, true);
        }
    }

    [Fact]
    public async Task GitHubReleaseClient_SkipsNonInstallableReleases()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            [
              {"draft":false,"prerelease":true,"tag_name":"0.15.145","assets":[{"name":"Depot-0.15.145.exe","size":145,"browser_download_url":"https://example.test/depot-145.exe"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.144","assets":[{"name":"wrong-name.exe","size":144,"browser_download_url":"https://example.test/wrong.exe"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.143","assets":[{"name":"Depot-0.15.143.exe","size":143,"browser_download_url":"https://example.test/depot-143.exe","digest":"sha256:1234"}]}
            ]
            """));
        using var http = new HttpClient(handler);

        var release = await new GitHubReleaseClient(http).GetLatestAsync(CancellationToken.None);

        Assert.Equal(new Version(0, 15, 143), release.Version);
        Assert.Equal("Depot-0.15.143.exe", release.AssetName);
        Assert.Equal("1234", release.Sha256);
    }

    [Fact]
    public async Task DepotReleaseMetadataClient_ValidatesAndReadsManifestMetadata()
    {
        var manifestBytes = Encoding.UTF8.GetBytes("""
            {"DepotVersion":"0.15.143","DepotManagerVersion":"0.2.7","DatabaseSchemaVersion":30,"ManagerCommandProtocol":2}
            """);
        var releaseJson = $$"""
            {
              "draft":false,
              "prerelease":false,
              "published_at":"2026-09-06T12:00:00Z",
              "name":"Depot 0.15.143",
              "body":"Release notes",
              "assets":[{"name":"Depot-0.15.143.manifest.json","size":{{manifestBytes.Length}},"browser_download_url":"https://example.test/Depot-0.15.143.manifest.json"}]
            }
            """;
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.Host == "api.github.com"
                ? JsonResponse(releaseJson)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifestBytes) });
        using var http = new HttpClient(handler);
        var selected = new ReleaseInfo(new Version(0, 15, 143), "0.15.143", "Depot-0.15.143.exe", new Uri("https://example.test/Depot-0.15.143.exe"), 1, null);

        var metadata = await new DepotReleaseMetadataClient(http).GetAsync(selected, CancellationToken.None);

        Assert.Equal(new Version(0, 15, 143), metadata.DepotVersion);
        Assert.Equal(30, metadata.DatabaseSchemaVersion);
        Assert.Equal(new Version(0, 2, 7), metadata.DepotManagerVersion);
        Assert.Equal(2, metadata.ManagerCommandProtocol);
        Assert.Equal("Depot 0.15.143", metadata.ReleaseName);
        Assert.Equal("Release notes", metadata.ReleaseNotes);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DepotReleaseMetadataClient_BlocksReleaseWithoutRequiredManifest()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"draft":false,"prerelease":false,"assets":[]}
            """));
        using var http = new HttpClient(handler);
        var selected = new ReleaseInfo(new Version(0, 15, 143), "0.15.143", "Depot-0.15.143.exe", new Uri("https://example.test/Depot-0.15.143.exe"), 1, null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new DepotReleaseMetadataClient(http).GetAsync(selected, CancellationToken.None));

        Assert.Contains("manifest.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ManagerSelfUpdateService_CleansStagingFilesWhenDownloadValidationFails()
    {
        var root = CreateTempDirectory();
        try
        {
            var target = Path.Combine(root, "DepotManager.exe");
            var staged = ManagerSelfUpdatePaths.GetStagedPath(target);
            var marker = ManagerSelfUpdatePaths.CreateReadyMarkerPath(target);
            File.WriteAllText(staged, "stale");
            File.WriteAllText(marker, "stale");
            var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Network access was not expected."));
            using var http = new HttpClient(handler);
            var release = new ManagerReleaseInfo(new Version(0, 15, 144), new Uri("http://example.test/manager.exe"), 10, null, "0.15.144");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ManagerSelfUpdateService().StageAndLaunchAsync(new ManagerReleaseClient(http), release, target, null, CancellationToken.None));

            Assert.False(File.Exists(staged));
            Assert.False(File.Exists(marker));
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ManagerSelfUpdateBootstrap_RejectsInvalidVerificationProcessId()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ManagerSelfUpdateBootstrap.TryHandle(["--manager-update-verification", "not-a-process-id", "marker"]));

        Assert.Contains("process id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MigrationSafetyService_RejectsRemoteProviderWithoutTouchingDatabase()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new DatabaseConnectionSettings { Provider = DatabaseProvider.SqlServer };
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new MigrationSafetyService().CreateSqliteSafetyBackupAsync(settings, root, new Version(0, 15, 143), 30, CancellationToken.None));

            Assert.Contains("only for local SQLite", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MigrationSafetyService_RejectsMissingLocalDatabase()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new DatabaseConnectionSettings
            {
                Provider = DatabaseProvider.Local,
                LocalDatabasePath = Path.Combine(root, "missing.db")
            };

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                new MigrationSafetyService().CreateSqliteSafetyBackupAsync(settings, root, new Version(0, 15, 143), 30, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RollbackMetadataService_RejectsInvalidAndSchemaIncompatibleCandidates()
    {
        var invalid = new RollbackCandidate("missing.exe", new Version(0, 15, 142), 0, DateTimeOffset.MinValue, false, "Rollback metadata is invalid.");
        var invalidException = Assert.Throws<InvalidOperationException>(() => RollbackMetadataService.EnsureCompatible(invalid, 30));
        Assert.Equal("Rollback metadata is invalid.", invalidException.Message);

        var valid = new RollbackCandidate("Depot-0.15.142.exe", new Version(0, 15, 142), 29, DateTimeOffset.UtcNow, true, "Rollback is available.");
        var schemaException = Assert.Throws<InvalidOperationException>(() => RollbackMetadataService.EnsureCompatible(valid, 30));
        Assert.Contains("schema 30", schemaException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("schema 29", schemaException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallationService_ComposesCanonicalInstallationPaths()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new InstallationService(root, _ => { });

            Assert.Equal(Path.GetFullPath(root), service.InstallDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "Depot.exe"), service.DepotPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "DepotManager.exe"), service.ManagerPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "Backup"), service.BackupDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "depot.settings"), service.SettingsPath);
            Assert.False(service.IsProvisioned);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WindowsIntegrationService_UsesDepotDesktopShortcutName()
    {
        Assert.Equal("Depot.lnk", Path.GetFileName(WindowsIntegrationService.GetDesktopShortcutPath()));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DepotManagerCoverage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(responder(request));
        }
    }
}
