using System.Net;
using System.Net.Http;
using System.Text;
using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class ReleaseChannelTests
{
    [Fact]
    public async Task DepotReleaseDiscovery_IgnoresPreviewReleases()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            [
              {"draft":false,"prerelease":true,"tag_name":"0.15.172-preview","assets":[{"name":"Depot-0.15.172.exe","size":172,"browser_download_url":"https://example.test/preview.exe"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.171","assets":[{"name":"Depot-0.15.171.exe","size":171,"browser_download_url":"https://example.test/stable.exe","digest":"sha256:ABCD"}]}
            ]
            """));
        using var http = new HttpClient(handler);

        var release = await new GitHubReleaseClient(http).GetLatestAsync(CancellationToken.None);

        Assert.Equal(new Version(0, 15, 171), release.Version);
        Assert.Equal("0.15.171", release.Tag);
        Assert.Equal("Depot-0.15.171.exe", release.AssetName);
        Assert.Equal("ABCD", release.Sha256);
    }

    [Fact]
    public async Task ManagerReleaseDiscovery_IgnoresPreviewReleases()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            [
              {"draft":false,"prerelease":true,"tag_name":"0.15.172-preview","assets":[{"name":"DepotManager-0.1.24.exe","size":124,"browser_download_url":"https://example.test/preview-manager.exe"}]},
              {"draft":false,"prerelease":false,"tag_name":"0.15.171","assets":[{"name":"DepotManager-0.1.23.exe","size":123,"browser_download_url":"https://example.test/stable-manager.exe","digest":"sha256:DCBA"}]}
            ]
            """));
        using var http = new HttpClient(handler);

        var release = await new ManagerReleaseClient(http).GetLatestAsync(CancellationToken.None);

        Assert.Equal(new Version(0, 1, 23), release.Version);
        Assert.Equal("0.15.171", release.ReleaseTag);
        Assert.Equal("DCBA", release.Sha256);
    }

    [Fact]
    public async Task ReleaseMetadata_RejectsPreviewReleaseForStableUpdatePath()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "draft":false,
              "prerelease":true,
              "tag_name":"0.15.172-preview",
              "assets":[]
            }
            """));
        using var http = new HttpClient(handler);
        var selected = new ReleaseInfo(
            new Version(0, 15, 172),
            "0.15.172-preview",
            "Depot-0.15.172.exe",
            new Uri("https://example.test/Depot-0.15.172.exe"),
            1,
            null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DepotReleaseMetadataClient(http).GetAsync(selected, CancellationToken.None));

        Assert.Contains("stable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.CallCount);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

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
