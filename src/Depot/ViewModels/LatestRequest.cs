// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class LatestRequest : IDisposable
{
	private readonly object _sync = new();
	private CancellationTokenSource? _cancellation;
	private long _generation;
	private bool _disposed;

	public LatestRequestLease Begin(CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			_cancellation?.Cancel();
			_cancellation?.Dispose();
			_cancellation = cancellationToken.CanBeCanceled
				? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
				: new CancellationTokenSource();
			return new LatestRequestLease(this, ++_generation, _cancellation.Token);
		}
	}

	internal bool IsCurrent(long generation)
	{
		lock (_sync) return !_disposed && generation == _generation;
	}

	public void Dispose()
	{
		lock (_sync)
		{
			if (_disposed) return;
			_disposed = true;
			_cancellation?.Cancel();
			_cancellation?.Dispose();
			_cancellation = null;
		}
	}
}

public readonly record struct LatestRequestLease(
	LatestRequest Owner,
	long Generation,
	CancellationToken Token)
{
	public bool IsCurrent => Owner.IsCurrent(Generation);
}
