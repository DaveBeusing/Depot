using Microsoft.Win32;

namespace DepotManager;

public sealed class WindowsIntegrationService
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot";

    public bool DesktopShortcutPreferred
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            if (key?.GetValue("DesktopShortcut") is int value) return value == 1;
            return File.Exists(GetDesktopShortcutPath());
        }
    }

    public void SetDesktopShortcutPreference(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DesktopShortcut", enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    public Version? GetRegisteredDepotVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        var text = key?.GetValue("DisplayVersion") as string;
        return VersionRules.TryParseReleaseTag(text ?? string.Empty, out var version) ? version : null;
    }

    public void Repair(InstallationService installation, Version depotVersion)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!File.Exists(installation.ManagerPath)) installation.CopyManagerToInstallLocation();
        var desktopExpected = DesktopShortcutPreferred;
        installation.RegisterInstalledApp(depotVersion);
        SetDesktopShortcutPreference(desktopExpected);
        if (desktopExpected)
        {
            installation.CreateDesktopShortcut();
        }
        else
        {
            var desktop = GetDesktopShortcutPath();
            if (File.Exists(desktop)) File.Delete(desktop);
        }
    }

    public static string GetDesktopShortcutPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Depot.lnk");
}
