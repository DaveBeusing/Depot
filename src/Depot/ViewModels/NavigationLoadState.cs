// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public enum NavigationLoadStatus
{
	NotLoaded,
	Loaded,
	Stale
}

public sealed class NavigationLoadState : IDisposable
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private bool _disposed;

	public NavigationLoadStatus Status { get; private set; } = NavigationLoadStatus.NotLoaded;

	public Task ActivateAsync(Func<CancellationToken, Task> loadAsync, CancellationToken cancellationToken = default) =>
		ExecuteAsync(loadAsync, false, cancellationToken);

	public Task RefreshAsync(Func<CancellationToken, Task> loadAsync, CancellationToken cancellationToken = default) =>
		ExecuteAsync(loadAsync, true, cancellationToken);

	public void MarkStale()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Status = NavigationLoadStatus.Stale;
	}

	private async Task ExecuteAsync(
		Func<CancellationToken, Task> loadAsync,
		bool force,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!force && Status == NavigationLoadStatus.Loaded) return;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!force && Status == NavigationLoadStatus.Loaded) return;
			var previousStatus = Status;
			try
			{
				await loadAsync(cancellationToken);
				Status = NavigationLoadStatus.Loaded;
			}
			catch
			{
				Status = previousStatus;
				throw;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_gate.Dispose();
	}
}
