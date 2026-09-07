using Xunit;

namespace Depot.Tests.E2E;

public sealed class PackagedE2EFactAttribute : FactAttribute
{
    public PackagedE2EFactAttribute()
    {
        if (!PackagedE2EEnvironment.Enabled)
            Skip = "Packaged E2E tests run only when DEPOT_PACKAGED_E2E=1 is explicitly set.";
    }
}

public sealed class PackagedE2EFullFactAttribute : FactAttribute
{
    public PackagedE2EFullFactAttribute()
    {
        if (!PackagedE2EEnvironment.Enabled)
            Skip = "Packaged E2E tests run only when DEPOT_PACKAGED_E2E=1 is explicitly set.";
        else if (!string.Equals(PackagedE2EEnvironment.Tier, "Full", StringComparison.OrdinalIgnoreCase))
            Skip = "This scenario belongs to the Full packaged E2E tier.";
    }
}

internal static class PackagedE2EEnvironment
{
    public static bool Enabled =>
        OperatingSystem.IsWindows()
        && string.Equals(Environment.GetEnvironmentVariable("DEPOT_PACKAGED_E2E"), "1", StringComparison.Ordinal);

    public static string Tier => Environment.GetEnvironmentVariable("DEPOT_PACKAGED_E2E_TIER") ?? "Smoke";

    public static string CurrentRoot => RequiredPath("DEPOT_PACKAGED_E2E_CURRENT_ROOT");
    public static string PreviousRoot => RequiredPath("DEPOT_PACKAGED_E2E_PREVIOUS_ROOT");
    public static string WorkRoot => RequiredPath("DEPOT_PACKAGED_E2E_WORK_ROOT");

    public static string CurrentDepot => Path.Combine(CurrentRoot, "depot", "Depot.exe");
    public static string CurrentManager => Path.Combine(CurrentRoot, "manager", "DepotManager.exe");
    public static string PreviousDepot => Path.Combine(PreviousRoot, "depot", "Depot.exe");
    public static string PreviousManager => Path.Combine(PreviousRoot, "manager", "DepotManager.exe");

    public static string CreateScenarioRoot(string name)
    {
        var safe = string.Concat(name.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
        var path = Path.Combine(WorkRoot, $"{safe}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "scenario.txt"), $"{name}{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        return path;
    }

    private static string RequiredPath(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{variable} is required for packaged E2E execution.");
        return Path.GetFullPath(value);
    }
}
