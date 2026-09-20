// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Depot.Diagnostics;

internal sealed record MyWorkProviderMeasurement(
	string Provider,
	bool Eligible,
	TimeSpan Elapsed,
	int ReturnedRows,
	bool Failed,
	int QueryCount);

internal static class MyWorkPerformanceDiagnostics
{
	private static readonly object SyncRoot = new();
	private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "performance.log");

	public static MyWorkProviderMeasurement MeasureIneligible(string provider) =>
		new(provider, false, TimeSpan.Zero, 0, false, 0);

	public static async Task<(T Result, MyWorkProviderMeasurement Measurement)> MeasureAsync<T>(
		string provider,
		Func<Task<T>> operation,
		Func<T, int> rowCount)
	{
		using var queryScope = DatabaseQueryDiagnostics.BeginScope();
		var stopwatch = Stopwatch.StartNew();
		try
		{
			var result = await operation().ConfigureAwait(false);
			stopwatch.Stop();
			return (result, new MyWorkProviderMeasurement(
				provider,
				true,
				stopwatch.Elapsed,
				rowCount(result),
				false,
				queryScope.CommandCount));
		}
		catch (OperationCanceledException)
		{
			stopwatch.Stop();
			throw;
		}
		catch
		{
			stopwatch.Stop();
			throw new MyWorkProviderMeasurementException(
				new MyWorkProviderMeasurement(provider, true, stopwatch.Elapsed, 0, true, queryScope.CommandCount));
		}
	}

	public static void Write(MyWorkProviderMeasurement measurement)
	{
		var line = string.Create(
			CultureInfo.InvariantCulture,
			$"{DateTimeOffset.Now:O} my-work provider={Sanitize(measurement.Provider)} eligible={measurement.Eligible} elapsedMs={measurement.Elapsed.TotalMilliseconds:F1} rows={measurement.ReturnedRows} failed={measurement.Failed} queries={measurement.QueryCount}");
		lock (SyncRoot)
			File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
	}

	private static string Sanitize(string value) =>
		new(value.Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_').ToArray());

	internal sealed class MyWorkProviderMeasurementException(MyWorkProviderMeasurement measurement) : Exception
	{
		public MyWorkProviderMeasurement Measurement { get; } = measurement;
	}
}
