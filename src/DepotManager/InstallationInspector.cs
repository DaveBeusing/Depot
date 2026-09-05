using System.Diagnostics;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Microsoft.Win32;

namespace DepotManager;

public sealed class InstallationInspector
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot";

    public static string? GetRegisteredInstallDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        var value = key?.GetValue("InstallLocation") as string;
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }

    public async Task<InstallationSnapshot> InspectAsync(string fallbackInstallDirectory, CancellationToken cancellationToken)
    {
        var registeredDirectory = GetRegisteredInstallDirectory();
        var installDirectory = Path.GetFullPath(registeredDirectory ?? fallbackInstallDirectory);
        var depotPath = Path.Combine(installDirectory, "Depot.exe");
        var managerPath = Path.Combine(installDirectory, "DepotManager.exe");
        var settingsPath = Path.Combine(installDirectory, "depot.settings");

        using var registry = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        var registryPresent = registry is not null;
        var depotPresent = File.Exists(depotPath);
        var managerPresent = File.Exists(managerPath);
        var settingsPresent = File.Exists(settingsPath);
        var startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Depot.lnk");
        var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Depot.lnk");
        var startMenuPresent = File.Exists(startMenuPath);
        var desktopPresent = File.Exists(desktopPath);
        var desktopExpected = registry?.GetValue("DesktopShortcut") is int preference ? preference == 1 : desktopPresent;
        var registrationConsistent = IsRegistrationConsistent(registry, installDirectory, managerPath, depotPath);

        var depotValid = false;
        Version? depotVersion = null;
        if (depotPresent)
        {
            try
            {
                PortableExecutableValidator.ValidateWindowsExecutable(depotPath);
                var text = FileVersionInfo.GetVersionInfo(depotPath).FileVersion;
                depotValid = Version.TryParse(text, out var parsed);
                if (depotValid) depotVersion = VersionRules.ReleaseVersion(parsed!);
            }
            catch
            {
                depotValid = false;
            }
        }

        Version? managerVersion = null;
        if (managerPresent)
        {
            try
            {
                PortableExecutableValidator.ValidateWindowsExecutable(managerPath);
                var text = FileVersionInfo.GetVersionInfo(managerPath).FileVersion;
                if (Version.TryParse(text, out var parsed)) managerVersion = VersionRules.ReleaseVersion(parsed);
            }
            catch
            {
                managerPresent = false;
            }
        }

        DatabaseConnectionSettings? settings = null;
        var settingsReadable = false;
        var databaseReachable = false;
        int? schemaVersion = null;
        if (settingsPresent)
        {
            try
            {
                settings = new SettingsRepository(settingsPath).Load();
                settingsReadable = true;
                await new ManagerDatabaseConnectionValidator().ValidateAsync(settings, cancellationToken);
                databaseReachable = true;
                schemaVersion = await new DatabaseSchemaInspector().ReadSchemaVersionAsync(settings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                databaseReachable = false;
            }
        }

        var windowsHealthy = registrationConsistent && startMenuPresent && (!desktopExpected || desktopPresent);
        var state = InstallationStateRules.Determine(
            registryPresent,
            depotPresent,
            depotValid,
            managerPresent,
            settingsPresent,
            settingsReadable,
            databaseReachable,
            schemaVersion,
            DatabaseVersion.CurrentVersion,
            windowsHealthy);

        return new InstallationSnapshot(
            state,
            installDirectory,
            depotVersion,
            managerVersion,
            schemaVersion,
            settings?.Provider,
            settings is null ? "Unknown" : DatabaseSchemaInspector.DescribeTarget(settings),
            registryPresent,
            depotPresent,
            managerPresent,
            settingsPresent,
            settingsReadable,
            databaseReachable,
            startMenuPresent,
            desktopExpected,
            desktopPresent,
            registrationConsistent,
            DescribeState(state));
    }

    private static bool IsRegistrationConsistent(RegistryKey? key, string installDirectory, string managerPath, string depotPath)
    {
        if (key is null) return false;
        var registeredPath = key.GetValue("InstallLocation") as string;
        var uninstall = NormalizeCommand(key.GetValue("UninstallString") as string);
        var modify = NormalizeCommand(key.GetValue("ModifyPath") as string);
        var icon = key.GetValue("DisplayIcon") as string;
        return !string.IsNullOrWhiteSpace(registeredPath)
            && string.Equals(Path.GetFullPath(registeredPath), installDirectory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uninstall, managerPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(modify, managerPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(icon, depotPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommand(string? value) => (value ?? string.Empty).Trim().Trim('"');

    public static string DescribeState(InstallationHealthState state) => state switch
    {
        InstallationHealthState.NotInstalled => "Depot is not installed.",
        InstallationHealthState.InstallationIncomplete => "The installation is incomplete and requires attention.",
        InstallationHealthState.InstalledHealthy => "Depot is installed and healthy.",
        InstallationHealthState.RepairRecommended => "Depot is usable, but Windows integration or manager files should be repaired.",
        InstallationHealthState.InstallationDamaged => "The Depot installation is damaged. Repair is recommended.",
        InstallationHealthState.ProvisioningIncomplete => "Depot is installed, but database provisioning is not complete.",
        InstallationHealthState.ConfigurationDamaged => "Depot settings cannot be read or decrypted for the current Windows user.",
        InstallationHealthState.DatabaseUnavailable => "The configured Depot database is unavailable or its schema cannot be read.",
        InstallationHealthState.DatabaseMigrationRequired => "The database schema is older than this Depot version requires.",
        InstallationHealthState.RecoveryRequired => "The database schema is newer than this Depot version supports. Recovery is required.",
        _ => "Unknown installation state."
    };
}
