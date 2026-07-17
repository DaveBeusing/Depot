// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public sealed class AsyncCache<T> where T : class
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _lifetime;
	private T? _value;
	private DateTime _expiresUtc;
	private bool _hasValue;

	public AsyncCache(TimeSpan lifetime)
	{
		_lifetime = lifetime;
	}

	public async Task<T> GetAsync(
		Func<CancellationToken, Task<T>> valueFactory,
		CancellationToken cancellationToken)
	{
		if (_hasValue && DateTime.UtcNow < _expiresUtc)
		{
			return _value ?? throw new InvalidOperationException("The cache value is unavailable.");
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_hasValue && DateTime.UtcNow < _expiresUtc)
			{
				return _value ?? throw new InvalidOperationException("The cache value is unavailable.");
			}

			_value = await valueFactory(cancellationToken);
			_hasValue = true;
			_expiresUtc = DateTime.UtcNow.Add(_lifetime);
			return _value;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Invalidate()
	{
		_hasValue = false;
		_value = default;
		_expiresUtc = default;
	}
}
