using Depot.Models;

namespace DepotManager;

public sealed record InstallationSnapshot(
    InstallationHealthState State,
    string InstallDirectory,
    Version? DepotVersion,
    Version? InstalledManagerVersion,
    int? DatabaseSchemaVersion,
    DatabaseProvider? DatabaseProvider,
    string DatabaseTarget,
    bool RegistryPresent,
    bool DepotExecutablePresent,
    bool ManagerExecutablePresent,
    bool SettingsPresent,
    bool SettingsReadable,
    bool DatabaseReachable,
    bool StartMenuShortcutPresent,
    bool DesktopShortcutExpected,
    bool DesktopShortcutPresent,
    bool RegistrationConsistent,
    string Message)
{
    public bool IsInstalled => State != InstallationHealthState.NotInstalled;
    public bool CanStartDepot => State is InstallationHealthState.InstalledHealthy or InstallationHealthState.RepairRecommended;
}

public static class InstallationStateRules
{
    public static InstallationHealthState Determine(
        bool registryPresent,
        bool depotPresent,
        bool depotValid,
        bool managerPresent,
        bool settingsPresent,
        bool settingsReadable,
        bool databaseReachable,
        int? databaseSchemaVersion,
        int supportedSchemaVersion,
        bool windowsIntegrationHealthy)
    {
        if (!depotPresent)
            return registryPresent || managerPresent || settingsPresent ? InstallationHealthState.InstallationDamaged : InstallationHealthState.NotInstalled;
        if (!depotValid) return InstallationHealthState.InstallationDamaged;
        if (!settingsPresent) return InstallationHealthState.ProvisioningIncomplete;
        if (!settingsReadable) return InstallationHealthState.ConfigurationDamaged;
        if (!databaseReachable) return InstallationHealthState.DatabaseUnavailable;
        if (databaseSchemaVersion is null) return InstallationHealthState.InstallationIncomplete;
        if (databaseSchemaVersion > supportedSchemaVersion) return InstallationHealthState.RecoveryRequired;
        if (databaseSchemaVersion < supportedSchemaVersion) return InstallationHealthState.DatabaseMigrationRequired;
        if (!managerPresent || !windowsIntegrationHealthy) return InstallationHealthState.RepairRecommended;
        return InstallationHealthState.InstalledHealthy;
    }
}
