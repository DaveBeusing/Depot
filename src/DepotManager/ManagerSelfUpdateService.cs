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
        var staged = ManagerSelfUpdatePaths.GetStagedPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        DeleteIfExists(staged);
        DeleteReadyMarkers(target);
        var marker = ManagerSelfUpdatePaths.CreateReadyMarkerPath(target);

        try
        {
            await client.DownloadAsync(release, staged, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AuthenticodeVerifier.ValidateTrustedSignature(staged);

            var startInfo = new ProcessStartInfo(staged)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(target)!
            };
            startInfo.ArgumentList.Add("--apply-manager-update");
            startInfo.ArgumentList.Add(target);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(marker);
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("The Depot Manager update helper could not be started.");
        }
        catch
        {
            DeleteIfExists(staged);
            DeleteIfExists(marker);
            throw;
        }
    }

    private static void DeleteReadyMarkers(string target)
    {
        var directory = Path.GetDirectoryName(target)!;
        var pattern = $"{Path.GetFileNameWithoutExtension(target)}.update.ready.*.marker";
        foreach (var candidate in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            if (ManagerSelfUpdatePaths.IsReadyMarkerForTarget(target, candidate)) DeleteIfExists(candidate);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public static class ManagerSelfUpdateBootstrap
{
    private const int MoveFileDelayUntilReboot = 0x4;
    private const int ParentExitTimeoutMilliseconds = 30_000;
    private const int ReadyTimeoutMilliseconds = 45_000;
    private const int PostReadyStabilityMilliseconds = 1_500;
    private static StartupVerification? _startupVerification;

    public static bool TryHandle(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length >= 4 && string.Equals(args[0], "--apply-manager-update", StringComparison.Ordinal))
        {
            Apply(args[1], ParseProcessId(args[2]), args[3]);
            return true;
        }

        if (args.Length >= 3 && string.Equals(args[0], "--manager-update-verification", StringComparison.Ordinal))
        {
            RegisterStartupVerification(ParseProcessId(args[1]), args[2]);
            return false;
        }

        return false;
    }

    public static void AcknowledgeStartup()
    {
        var verification = Interlocked.Exchange(ref _startupVerification, null);
        if (verification is null) return;

        var runningManager = Environment.ProcessPath
            ?? throw new InvalidOperationException("The running Depot Manager path is unavailable.");
        if (!ManagerSelfUpdatePaths.IsReadyMarkerForTarget(runningManager, verification.MarkerPath))
            throw new InvalidOperationException("The Depot Manager update verification marker is invalid.");

        var markerDirectory = Path.GetDirectoryName(verification.MarkerPath)
            ?? throw new InvalidOperationException("The update verification directory is unavailable.");
        Directory.CreateDirectory(markerDirectory);
        File.WriteAllText(
            verification.MarkerPath,
            $"pid={Environment.ProcessId}{Environment.NewLine}ready={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        _ = Task.Run(() => CleanupVerifiedUpdate(runningManager, verification));
    }

    private static void RegisterStartupVerification(int helperProcessId, string markerPath)
    {
        if (helperProcessId <= 0) throw new InvalidOperationException("The Depot Manager update helper process id is invalid.");
        var runningManager = Environment.ProcessPath
            ?? throw new InvalidOperationException("The running Depot Manager path is unavailable.");
        if (!ManagerSelfUpdatePaths.IsReadyMarkerForTarget(runningManager, markerPath))
            throw new InvalidOperationException("The Depot Manager update verification marker is invalid.");
        _startupVerification = new StartupVerification(helperProcessId, Path.GetFullPath(markerPath));
    }

    private static void Apply(string targetPath, int parentProcessId, string markerPath)
    {
        var helperPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The manager update helper path is unavailable.");
        targetPath = Path.GetFullPath(targetPath);
        if (!ManagerSelfUpdatePaths.IsExpectedStagedHelper(targetPath, helperPath))
            throw new InvalidOperationException("The Depot Manager update helper is not running from the expected staged executable path.");
        if (!ManagerSelfUpdatePaths.IsReadyMarkerForTarget(targetPath, markerPath))
            throw new InvalidOperationException("The Depot Manager update verification marker is invalid.");
        if (!WaitForProcessExit(parentProcessId, ParentExitTimeoutMilliseconds))
            throw new TimeoutException("The running Depot Manager did not exit in time for self-update.");

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("The manager target directory is unavailable.");
        var previousPath = ManagerSelfUpdatePaths.GetPreviousPath(targetPath);
        Directory.CreateDirectory(targetDirectory);
        DeleteWithFallback(markerPath);
        DeleteWithFallback(previousPath);

        var hadPrevious = File.Exists(targetPath);
        if (hadPrevious) File.Copy(targetPath, previousPath, true);

        Process? updatedProcess = null;
        try
        {
            PortableExecutableValidator.ValidateWindowsExecutable(helperPath);
            AuthenticodeVerifier.ValidateTrustedSignature(helperPath);
            ExecutableDeployment.Replace(helperPath, targetPath);

            var startInfo = new ProcessStartInfo(targetPath)
            {
                UseShellExecute = true,
                WorkingDirectory = targetDirectory
            };
            startInfo.ArgumentList.Add("--manager-update-verification");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(markerPath);
            updatedProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The updated Depot Manager could not be started.");

            if (!WaitForReady(updatedProcess, markerPath, ReadyTimeoutMilliseconds))
                throw new InvalidOperationException("The updated Depot Manager did not confirm a successful UI startup.");

            Thread.Sleep(PostReadyStabilityMilliseconds);
            if (HasExited(updatedProcess))
                throw new InvalidOperationException("The updated Depot Manager exited immediately after startup verification.");
        }
        catch
        {
            StopProcess(updatedProcess);
            DeleteWithFallback(markerPath);
            var recovered = false;
            if (hadPrevious && File.Exists(previousPath))
            {
                ExecutableDeployment.Replace(previousPath, targetPath);
                recovered = TryStartRecoveredManager(targetPath, targetDirectory);
            }
            MoveFileEx(helperPath, null, MoveFileDelayUntilReboot);
            if (recovered) return;
            throw;
        }
        finally
        {
            updatedProcess?.Dispose();
        }
    }

    private static void CleanupVerifiedUpdate(string runningManagerPath, StartupVerification verification)
    {
        if (!WaitForProcessExit(verification.HelperProcessId, 60_000)) return;
        DeleteWithFallback(ManagerSelfUpdatePaths.GetStagedPath(runningManagerPath));
        DeleteWithFallback(ManagerSelfUpdatePaths.GetPreviousPath(runningManagerPath));
        DeleteWithFallback(verification.MarkerPath);
    }

    private static bool WaitForReady(Process process, string markerPath, int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(markerPath)) return true;
            if (HasExited(process)) return false;
            Thread.Sleep(200);
        }
        return File.Exists(markerPath);
    }

    private static bool WaitForProcessExit(int processId, int timeoutMilliseconds)
    {
        if (processId <= 0) return true;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(timeoutMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static bool TryStartRecoveredManager(string targetPath, string targetDirectory)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(targetPath)
            {
                UseShellExecute = true,
                WorkingDirectory = targetDirectory
            }) is not null;
        }
        catch
        {
            // Preserve the restored executable for a manual restart or repair.
            return false;
        }
    }

    private static int ParseProcessId(string text) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var processId)
            ? processId
            : 0;

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

    private sealed record StartupVerification(int HelperProcessId, string MarkerPath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
