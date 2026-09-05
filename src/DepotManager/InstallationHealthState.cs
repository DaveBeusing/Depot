namespace DepotManager;

public enum InstallationHealthState
{
    NotInstalled,
    InstallationIncomplete,
    InstalledHealthy,
    RepairRecommended,
    InstallationDamaged,
    ProvisioningIncomplete,
    ConfigurationDamaged,
    DatabaseUnavailable,
    DatabaseMigrationRequired,
    RecoveryRequired
}
