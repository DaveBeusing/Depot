namespace DepotManager;

public static class ManagerSelfUpdatePaths
{
    public static string GetStagedPath(string canonicalManagerPath)
    {
        var target = NormalizeTarget(canonicalManagerPath);
        return Path.Combine(
            Path.GetDirectoryName(target)!,
            $"{Path.GetFileNameWithoutExtension(target)}.update.exe");
    }

    public static string GetPreviousPath(string canonicalManagerPath)
    {
        var target = NormalizeTarget(canonicalManagerPath);
        return Path.Combine(
            Path.GetDirectoryName(target)!,
            $"{Path.GetFileNameWithoutExtension(target)}.previous.exe");
    }

    public static string CreateReadyMarkerPath(string canonicalManagerPath)
    {
        var target = NormalizeTarget(canonicalManagerPath);
        return Path.Combine(
            Path.GetDirectoryName(target)!,
            $"{Path.GetFileNameWithoutExtension(target)}.update.ready.{Guid.NewGuid():N}.marker");
    }

    public static bool IsReadyMarkerForTarget(string canonicalManagerPath, string markerPath)
    {
        if (string.IsNullOrWhiteSpace(markerPath)) return false;
        var target = NormalizeTarget(canonicalManagerPath);
        var marker = Path.GetFullPath(markerPath);
        if (!string.Equals(Path.GetDirectoryName(target), Path.GetDirectoryName(marker), StringComparison.OrdinalIgnoreCase)) return false;

        var prefix = $"{Path.GetFileNameWithoutExtension(target)}.update.ready.";
        var name = Path.GetFileName(marker);
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".marker", StringComparison.OrdinalIgnoreCase)) return false;
        var token = name[prefix.Length..^".marker".Length];
        return Guid.TryParseExact(token, "N", out _);
    }

    public static bool IsExpectedStagedHelper(string canonicalManagerPath, string helperPath)
    {
        if (string.IsNullOrWhiteSpace(helperPath)) return false;
        return string.Equals(
            Path.GetFullPath(helperPath),
            GetStagedPath(canonicalManagerPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTarget(string canonicalManagerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalManagerPath);
        var target = Path.GetFullPath(canonicalManagerPath);
        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The Depot Manager installation directory is invalid.");
        if (!string.Equals(Path.GetFileName(target), "DepotManager.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The canonical Depot Manager executable must be named DepotManager.exe.");
        return Path.Combine(directory, Path.GetFileName(target));
    }
}
