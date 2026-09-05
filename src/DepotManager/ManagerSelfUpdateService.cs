using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DepotManager;

public sealed class ManagerSelfUpdateService
{
    public async Task StageAndLaunchAsync(
        ManagerReleaseClient client,
        ManagerReleaseInfo release,
        string canonicalManagerPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var target = Path.GetFullPath(canonicalManagerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staged = target + ".update";
        if (File.Exists(staged)) File.Delete(staged);
        try
        {
            await client.DownloadAsync(release, staged, progress, cancellationToken);
            var startInfo = new ProcessStartInfo(staged) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! };
            startInfo.ArgumentList.Add("--apply-manager-update");
            startInfo.ArgumentList.Add(target);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(startInfo) ?? throw new InvalidOperationException("The Depot Manager update helper could not be started.");
        }
        catch
        {
            if (File.Exists(staged)) File.Delete(staged);
            throw;
        }
    }
}

public static class ManagerSelfUpdateBootstrap
{
    private const int MoveFileDelayUntilReboot = 0x4;

    public static bool TryHandle(string[] args)
    {
        if (args.Length >= 3 && string.Equals(args[0], "--apply-manager-update", StringComparison.Ordinal))
        {
            Apply(args[1], ParseProcessId(args[2]));
            return true;
        }
        if (args.Length >= 4 && string.Equals(args[0], "--cleanup-manager-update", StringComparison.Ordinal))
        {
            Cleanup(args[1], ParseProcessId(args[2]), args[3]);
            return false;
        }
        return false;
    }

    private static void Apply(string targetPath, int parentProcessId)
    {
        WaitForProcess(parentProcessId);
        var helperPath = Environment.ProcessPath ?? throw new InvalidOperationException("The manager update helper path is unavailable.");
        targetPath = Path.GetFullPath(targetPath);
        var previousPath = targetPath + ".previous";
        var targetDirectory = Path.GetDirectoryName(targetPath) ?? throw new InvalidOperationException("The manager target directory is unavailable.");
        Directory.CreateDirectory(targetDirectory);
        if (File.Exists(previousPath)) File.Delete(previousPath);

        var hadPrevious = File.Exists(targetPath);
        if (hadPrevious) File.Copy(targetPath, previousPath, true);
        try
        {
            PortableExecutableValidator.ValidateWindowsExecutable(helperPath);
            ExecutableDeployment.Replace(helperPath, targetPath);
            var startInfo = new ProcessStartInfo(targetPath) { UseShellExecute = true, WorkingDirectory = targetDirectory };
            startInfo.ArgumentList.Add("--cleanup-manager-update");
            startInfo.ArgumentList.Add(helperPath);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(previousPath);
            Process.Start(startInfo) ?? throw new InvalidOperationException("The updated Depot Manager could not be started.");
        }
        catch
        {
            if (hadPrevious && File.Exists(previousPath)) File.Move(previousPath, targetPath, true);
            if (File.Exists(targetPath))
            {
                try { Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true, WorkingDirectory = targetDirectory }); } catch { }
            }
            throw;
        }
    }

    private static void Cleanup(string helperPath, int helperProcessId, string previousPath)
    {
        WaitForProcess(helperProcessId);
        DeleteWithFallback(helperPath);
        DeleteWithFallback(previousPath);
    }

    private static void WaitForProcess(int processId)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(30000);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    private static int ParseProcessId(string text) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var processId) ? processId : 0;

    private static void DeleteWithFallback(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { Thread.Sleep(100); }
        }
        MoveFileEx(path, null, MoveFileDelayUntilReboot);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
