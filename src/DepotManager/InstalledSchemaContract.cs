using System.Security.Cryptography;
using System.Text.Json;

using Depot.Models;

namespace DepotManager;

/// <summary>Locally retained requirements, bound to the exact installed executable after a successful health check.</summary>
public static class InstalledSchemaContract
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static string PathFor(string directory) => Path.Combine(directory, "depot.schema.json");

    public static void Save(string directory, DatabaseSchemaCompatibility requirements)
    {
        requirements.Validate(requirement: true);
        File.WriteAllText(PathFor(directory), JsonSerializer.Serialize(new Evidence(Hash(directory), requirements), Options));
    }

    public static DatabaseSchemaCompatibility? Read(string directory)
    {
        var path = PathFor(directory);
        if (!File.Exists(path)) return null;
        try
        {
            var evidence = JsonSerializer.Deserialize<Evidence>(File.ReadAllText(path), Options);
            if (evidence is null || evidence.Requirements is null || !string.Equals(evidence.ExecutableSha256, Hash(directory), StringComparison.OrdinalIgnoreCase)) return null;
            evidence.Requirements.Validate(requirement: true);
            return evidence.Requirements;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    public static void Delete(string directory)
    {
        var path = PathFor(directory);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string Hash(string directory)
    {
        using var stream = File.OpenRead(Path.Combine(directory, "Depot.exe"));
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record Evidence(string ExecutableSha256, DatabaseSchemaCompatibility Requirements);
}
