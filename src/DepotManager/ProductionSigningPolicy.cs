using System.Reflection;

namespace DepotManager;

public static class ProductionSigningPolicy
{
    private const string PreviewMarker = "-preview";

    public static bool RequiresPublisherContinuity =>
        RequiresPublisherContinuityForVersion(
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion);

    public static bool RequiresPublisherContinuityForVersion(string? informationalVersion) =>
        !string.IsNullOrWhiteSpace(informationalVersion) &&
        !informationalVersion.Contains(PreviewMarker, StringComparison.OrdinalIgnoreCase);

    public static void ValidateStableArtifactPublisher(string filePath)
    {
        if (!RequiresPublisherContinuity) return;

        var managerPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(managerPath) || !File.Exists(managerPath))
            throw new InvalidOperationException("The running Depot Manager executable is unavailable for publisher continuity validation.");

        var managerSignature = AuthenticodeVerifier.GetTrustedSignatureInfo(managerPath);
        AuthenticodeVerifier.ValidateTrustedSignature(filePath, managerSignature.Subject);
    }
}
