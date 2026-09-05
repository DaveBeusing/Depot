using System.Security.Cryptography;
using System.Text.Json;

namespace DepotManager;

public sealed record DepotReleaseMetadata(
    Version DepotVersion,
    int DatabaseSchemaVersion,
    Version? DepotManagerVersion,
    int ManagerCommandProtocol,
    DateTimeOffset? PublishedAt,
    string ReleaseName,
    string ReleaseNotes);

public sealed class DepotReleaseMetadataClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public DepotReleaseMetadataClient(HttpClient http) => _http = http;

    public async Task<DepotReleaseMetadata> GetAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/DaveBeusing/Depot/releases/tags/{Uri.EscapeDataString(release.Tag)}");
        request.Headers.UserAgent.ParseAdd("DepotManager/1.0");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidOperationException("Migration metadata is accepted only from a published stable Depot release.");

        var expectedName = $"Depot-{VersionRules.VersionText(release.Version)}.manifest.json";
        JsonElement? selected = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), expectedName, StringComparison.Ordinal))
            {
                selected = asset;
                break;
            }
        }
        if (selected is null)
            throw new InvalidOperationException(
                $"Release {VersionRules.VersionText(release.Version)} does not contain {expectedName}. Update is blocked because database migration compatibility cannot be proven.");

        var urlText = selected.Value.GetProperty("browser_download_url").GetString();
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The Depot release metadata asset URL is invalid.");
        var bytes = await _http.GetByteArrayAsync(uri, cancellationToken);
        var expectedSize = selected.Value.GetProperty("size").GetInt64();
        if (bytes.LongLength != expectedSize || expectedSize <= 0)
            throw new InvalidOperationException("The Depot release metadata asset is incomplete.");
        var digest = selected.Value.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
        if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            var actual = Convert.ToHexString(SHA256.HashData(bytes));
            if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Depot release metadata asset failed SHA-256 validation.");
        }

        var payload = JsonSerializer.Deserialize<ManifestPayload>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("The Depot release metadata is empty.");
        if (!Version.TryParse(payload.DepotVersion, out var manifestVersion) || VersionRules.ReleaseVersion(manifestVersion) != release.Version)
            throw new InvalidOperationException("The Depot release metadata version does not match the selected release.");
        if (payload.DatabaseSchemaVersion < 1)
            throw new InvalidOperationException("The Depot release metadata contains an invalid database schema version.");
        Version? managerVersion = null;
        if (!string.IsNullOrWhiteSpace(payload.DepotManagerVersion) && Version.TryParse(payload.DepotManagerVersion, out var parsedManager))
            managerVersion = VersionRules.ReleaseVersion(parsedManager);

        DateTimeOffset? published = null;
        if (root.TryGetProperty("published_at", out var publishedElement) && DateTimeOffset.TryParse(publishedElement.GetString(), out var parsedPublished))
            published = parsedPublished;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
        return new DepotReleaseMetadata(release.Version, payload.DatabaseSchemaVersion, managerVersion, payload.ManagerCommandProtocol, published, name, notes);
    }

    private sealed class ManifestPayload
    {
        public string DepotVersion { get; set; } = string.Empty;
        public string DepotManagerVersion { get; set; } = string.Empty;
        public int DatabaseSchemaVersion { get; set; }
        public int ManagerCommandProtocol { get; set; }
    }
}
