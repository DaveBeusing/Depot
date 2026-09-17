using System.Diagnostics;
using System.Runtime.InteropServices;
using DepotManager;
using Microsoft.Win32;
using Xunit;

namespace Depot.Tests.E2E;

[Collection(PackagedE2ECollection.CollectionName)]
public sealed class WindowsIntegrationPackagedE2ETests
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Depot";

    [PackagedE2EWindowsIntegrationFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Smoke")]
    public async Task WindowsIntegration_RegisterRepairInspectAndUninstall_RoundTripsOnHostedRunner()
    {
        EnsureDisposableWindowsProfile();

        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(WindowsIntegration_RegisterRepairInspectAndUninstall_RoundTripsOnHostedRunner));
        var install = Path.Combine(root, "install");
        var service = new InstallationService(install, _ => { });
        var version = ReadVersion(PackagedE2EEnvironment.CurrentDepot);

        try
        {
            service.Deploy(PackagedE2EEnvironment.CurrentDepot, version, createBackup: false);
            ExecutableDeployment.InstallManagerCopy(PackagedE2EEnvironment.CurrentManager, service.ManagerPath);
            service.RegisterInstalledApp(version);

            var integration = new WindowsIntegrationService();
            integration.SetDesktopShortcutPreference(true);
            service.CreateDesktopShortcut();

            AssertRegistration(service, version);
            AssertShortcutTargets(service.DepotPath);

            var snapshot = await new InstallationInspector().InspectAsync(install, CancellationToken.None);
            Assert.True(snapshot.RegistryPresent);
            Assert.True(snapshot.StartMenuShortcutPresent);
            Assert.True(snapshot.DesktopShortcutExpected);
            Assert.True(snapshot.DesktopShortcutPresent);
            Assert.True(snapshot.RegistrationConsistent);

            File.Delete(GetStartMenuShortcutPath());
            File.Delete(WindowsIntegrationService.GetDesktopShortcutPath());
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: true))
            {
                Assert.NotNull(key);
                key!.SetValue("DisplayIcon", Path.Combine(root, "invalid-icon.exe"));
            }

            integration.Repair(service, version);

            AssertRegistration(service, version);
            AssertShortcutTargets(service.DepotPath);
            snapshot = await new InstallationInspector().InspectAsync(install, CancellationToken.None);
            Assert.True(snapshot.StartMenuShortcutPresent);
            Assert.True(snapshot.DesktopShortcutPresent);
            Assert.True(snapshot.RegistrationConsistent);

            service.Uninstall(removeConfiguration: false);
            AssertWindowsIntegrationAbsent();
            Assert.False(File.Exists(service.DepotPath));
            Assert.False(File.Exists(service.ManagerPath));

            service.Uninstall(removeConfiguration: false);
            AssertWindowsIntegrationAbsent();
        }
        finally
        {
            TryCleanup(service);
        }
    }

    private static void EnsureDisposableWindowsProfile()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        var blockers = new List<string>();
        if (key is not null) blockers.Add("existing Depot uninstall registration");
        if (File.Exists(GetStartMenuShortcutPath())) blockers.Add("existing Depot Start menu shortcut");
        if (File.Exists(WindowsIntegrationService.GetDesktopShortcutPath())) blockers.Add("existing Depot desktop shortcut");

        if (blockers.Count > 0)
        {
            throw new InvalidOperationException(
                "Windows integration acceptance refuses to mutate a non-clean profile: " + string.Join(", ", blockers));
        }
    }

    private static void AssertRegistration(InstallationService service, Version version)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        Assert.NotNull(key);
        Assert.Equal("Depot", key!.GetValue("DisplayName") as string);
        Assert.Equal(VersionRules.VersionText(version), key.GetValue("DisplayVersion") as string);
        Assert.Equal("David Beusing", key.GetValue("Publisher") as string);
        Assert.Equal(service.InstallDirectory, key.GetValue("InstallLocation") as string);
        Assert.Equal(service.DepotPath, key.GetValue("DisplayIcon") as string);
        Assert.Equal($"\"{service.ManagerPath}\"", key.GetValue("UninstallString") as string);
        Assert.Equal($"\"{service.ManagerPath}\"", key.GetValue("ModifyPath") as string);
        Assert.Equal(0, Convert.ToInt32(key.GetValue("NoModify"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, Convert.ToInt32(key.GetValue("NoRepair"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, Convert.ToInt32(key.GetValue("DesktopShortcut"), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertShortcutTargets(string expectedDepotPath)
    {
        var expected = Path.GetFullPath(expectedDepotPath);
        var startMenu = GetStartMenuShortcutPath();
        var desktop = WindowsIntegrationService.GetDesktopShortcutPath();

        Assert.True(File.Exists(startMenu), $"Start menu shortcut is missing: {startMenu}");
        Assert.True(File.Exists(desktop), $"Desktop shortcut is missing: {desktop}");
        Assert.Equal(expected, ReadShortcutTarget(startMenu));
        Assert.Equal(expected, ReadShortcutTarget(desktop));
    }

    private static string ReadShortcutTarget(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable on the Windows acceptance runner.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("WScript.Shell could not be created.");
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(shortcutPath);
            dynamic dynamicShortcut = shortcut;
            return Path.GetFullPath((string)dynamicShortcut.TargetPath);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void AssertWindowsIntegrationAbsent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        Assert.Null(key);
        Assert.False(File.Exists(GetStartMenuShortcutPath()));
        Assert.False(File.Exists(WindowsIntegrationService.GetDesktopShortcutPath()));
    }

    private static string GetStartMenuShortcutPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Depot.lnk");

    private static Version ReadVersion(string file)
    {
        var text = FileVersionInfo.GetVersionInfo(file).FileVersion;
        Assert.True(Version.TryParse(text, out var version), $"File version could not be read from {file}.");
        return VersionRules.ReleaseVersion(version!);
    }

    private static void TryCleanup(InstallationService service)
    {
        try { service.RemoveWindowsIntegration(); }
        catch { }
        try { service.RemoveApplicationFiles(removeConfiguration: true); }
        catch { }
    }
}
