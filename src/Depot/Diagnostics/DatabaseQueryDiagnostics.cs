// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Diagnostics;

internal static class DatabaseQueryDiagnostics
{
	private static readonly AsyncLocal<QueryCounter?> Current = new();

	public static DatabaseQueryScope BeginScope()
	{
		var scope = new DatabaseQueryScope(Current.Value);
		Current.Value = scope.Counter;
		return scope;
	}

	public static void RecordCommand() => Current.Value?.Increment();

	internal sealed class DatabaseQueryScope : IDisposable
	{
		private readonly QueryCounter? _previous;
		private bool _disposed;

		internal DatabaseQueryScope(QueryCounter? previous)
		{
			_previous = previous;
			Counter = new QueryCounter();
		}

		internal QueryCounter Counter { get; }
		public int CommandCount => Counter.Count;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			Current.Value = _previous;
		}
	}

	internal sealed class QueryCounter
	{
		private int _count;
		public int Count => Volatile.Read(ref _count);
		public void Increment() => Interlocked.Increment(ref _count);
	}
}
