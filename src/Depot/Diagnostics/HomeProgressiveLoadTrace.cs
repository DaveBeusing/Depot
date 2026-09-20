// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Depot.Diagnostics;

internal sealed class HomeProgressiveLoadTrace
{
	private static readonly object SyncRoot = new();
	private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "performance.log");
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	private int _firstContentRecorded;

	public void RecordFirstContent(string block)
	{
		if (Interlocked.Exchange(ref _firstContentRecorded, 1) != 0) return;
		var elapsed = _stopwatch.Elapsed.TotalMilliseconds;
		var safeBlock = block is "my-work" or "dashboard" ? block : "unknown";
		var line = string.Create(
			CultureInfo.InvariantCulture,
			$"{DateTimeOffset.Now:O} home firstContent={safeBlock} elapsedMs={elapsed:F1}");
		lock (SyncRoot)
			File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
	}
}
