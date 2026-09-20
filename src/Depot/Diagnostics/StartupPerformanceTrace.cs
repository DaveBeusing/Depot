// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;

namespace Depot.Diagnostics;

public enum StartupPerformanceCheckpoint
{
	ApplicationStartup,
	SettingsLoad,
	DatabaseConnectionCreation,
	SchemaProvisioningCheck,
	CompositionCreation,
	AdministratorBootstrapCheck,
	LoginWindowReady,
	AuthenticationComplete,
	MainViewModelCreation,
	MainWindowReady,
	FirstInteractiveShell
}

public sealed record StartupPerformanceSample(StartupPerformanceCheckpoint Checkpoint, TimeSpan Elapsed);

public sealed class StartupPerformanceTrace
{
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	private readonly object _sync = new();
	private readonly HashSet<StartupPerformanceCheckpoint> _recorded = [];
	private readonly List<StartupPerformanceSample> _samples = [];

	public void Mark(StartupPerformanceCheckpoint checkpoint)
	{
		lock (_sync)
		{
			if (!_recorded.Add(checkpoint)) return;
			_samples.Add(new StartupPerformanceSample(checkpoint, _stopwatch.Elapsed));
		}
	}

	public IReadOnlyList<StartupPerformanceSample> Snapshot()
	{
		lock (_sync) return _samples.ToArray();
	}

	public string FormatSummary()
	{
		var samples = Snapshot();
		if (samples.Count == 0) return "Startup performance: no checkpoints recorded.";
		return "Startup performance: " + string.Join(
			"; ",
			samples.Select(sample =>
				$"{ToLabel(sample.Checkpoint)}={sample.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}ms"));
	}

	private static string ToLabel(StartupPerformanceCheckpoint checkpoint) => checkpoint switch
	{
		StartupPerformanceCheckpoint.ApplicationStartup => "application-startup",
		StartupPerformanceCheckpoint.SettingsLoad => "settings-load",
		StartupPerformanceCheckpoint.DatabaseConnectionCreation => "database-connection-creation",
		StartupPerformanceCheckpoint.SchemaProvisioningCheck => "schema-provisioning-check",
		StartupPerformanceCheckpoint.CompositionCreation => "composition-creation",
		StartupPerformanceCheckpoint.AdministratorBootstrapCheck => "administrator-bootstrap-check",
		StartupPerformanceCheckpoint.LoginWindowReady => "login-window-ready",
		StartupPerformanceCheckpoint.AuthenticationComplete => "authentication-complete",
		StartupPerformanceCheckpoint.MainViewModelCreation => "main-view-model-creation",
		StartupPerformanceCheckpoint.MainWindowReady => "main-window-ready",
		StartupPerformanceCheckpoint.FirstInteractiveShell => "first-interactive-shell",
		_ => throw new ArgumentOutOfRangeException(nameof(checkpoint), checkpoint, null)
	};
}

public static class StartupPerformance
{
	private static readonly StartupPerformanceTrace Trace = new();

	public static void Mark(StartupPerformanceCheckpoint checkpoint) => Trace.Mark(checkpoint);
	public static IReadOnlyList<StartupPerformanceSample> Snapshot() => Trace.Snapshot();
	public static void FlushToDiagnostics() => StartupDiagnostics.Log(Trace.FormatSummary());
}
