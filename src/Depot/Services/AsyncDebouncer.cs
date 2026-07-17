// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public sealed class AsyncDebouncer : IDisposable
{
	private readonly TimeSpan _delay;
	private CancellationTokenSource? _cancellationTokenSource;

	public AsyncDebouncer(TimeSpan delay)
	{
		_delay = delay;
	}

	public async Task DebounceAsync(Func<CancellationToken, Task> operation)
	{
		var next = new CancellationTokenSource();
		var previous = Interlocked.Exchange(ref _cancellationTokenSource, next);
		previous?.Cancel();
		previous?.Dispose();
		try
		{
			await Task.Delay(_delay, next.Token);
			await operation(next.Token);
		}
		catch (OperationCanceledException) when (next.IsCancellationRequested)
		{
		}
	}

	public void Dispose()
	{
		_cancellationTokenSource?.Cancel();
		_cancellationTokenSource?.Dispose();
	}
}
