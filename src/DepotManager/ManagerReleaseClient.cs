using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DepotManager;

public sealed record ManagerReleaseInfo(Version Version, Uri DownloadUri, long Size, string? Sha256, string ReleaseTag);

public sealed partial class ManagerReleaseClient
{
    private readonly HttpClient _http;

    public ManagerReleaseClient(HttpClient http) => _http = http;

    public async Task<ManagerReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/DaveBeusing/Depot/releases?per_page=50");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DepotManager", "1.0"));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        ManagerReleaseInfo? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                var match = ManagerAssetRegex().Match(name);
                if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) continue;
                var size = asset.GetProperty("size").GetInt64();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (size <= 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
                var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null;
                var candidate = new ManagerReleaseInfo(VersionRules.ReleaseVersion(version), uri, size, sha256, tag);
                if (best is null || candidate.Version > best.Version) best = candidate;
            }
        }
        return best ?? throw new InvalidOperationException("No published Depot Manager release asset was found.");
    }

    public async Task DownloadAsync(ManagerReleaseInfo release, string destination, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            var buffer = new byte[1024 * 1024];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress?.Report((int)Math.Min(100, total * 100 / release.Size));
            }
            await target.FlushAsync(cancellationToken);
            if (total != release.Size || total <= 0) throw new InvalidOperationException("The downloaded Depot Manager asset is incomplete.");
        }

        PortableExecutableValidator.ValidateWindowsExecutable(destination);
        var fileVersion = FileVersionInfo.GetVersionInfo(destination).FileVersion;
        if (!Version.TryParse(fileVersion, out var actual) || VersionRules.ReleaseVersion(actual) != release.Version)
            throw new InvalidOperationException("The downloaded Depot Manager file version does not match the published manager asset.");
        if (!string.IsNullOrWhiteSpace(release.Sha256))
        {
            await using var stream = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The downloaded Depot Manager asset failed SHA-256 validation.");
        }
    }

    public static bool IsUpdateAvailable(Version runningVersion, Version availableVersion) =>
        VersionRules.ReleaseVersion(availableVersion) > VersionRules.ReleaseVersion(runningVersion);

    [GeneratedRegex("^DepotManager-(\\d+\\.\\d+\\.\\d+)\\.exe$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagerAssetRegex();
}
