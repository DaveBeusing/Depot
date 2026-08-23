// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Depot.Services;

public sealed class LoginAttemptLimiter
{
	private const int MaximumFailures = 5;
	private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
	private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
	private readonly ConcurrentDictionary<string, AttemptState> _attempts = new(StringComparer.OrdinalIgnoreCase);
	private readonly TimeProvider _timeProvider;

	public LoginAttemptLimiter(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

	public bool IsBlocked(string accountKey, out TimeSpan retryAfter)
	{
		retryAfter = TimeSpan.Zero;
		if (!_attempts.TryGetValue(Normalize(accountKey), out var state)) return false;
		var now = _timeProvider.GetUtcNow();
		if (state.BlockedUntil is { } blockedUntil && blockedUntil > now)
		{
			retryAfter = blockedUntil - now;
			return true;
		}
		if (state.BlockedUntil is not null || now - state.FirstFailure >= FailureWindow) _attempts.TryRemove(Normalize(accountKey), out _);
		return false;
	}

	public void RecordFailure(string accountKey)
	{
		var key = Normalize(accountKey);
		var now = _timeProvider.GetUtcNow();
		_attempts.AddOrUpdate(key,
			_ => new AttemptState(now, 1, null),
			(_, existing) =>
			{
				if (now - existing.FirstFailure >= FailureWindow) return new AttemptState(now, 1, null);
				var failures = existing.Failures + 1;
				return new AttemptState(existing.FirstFailure, failures, failures >= MaximumFailures ? now + LockoutDuration : existing.BlockedUntil);
			});
	}

	public void RecordSuccess(string accountKey) => _attempts.TryRemove(Normalize(accountKey), out _);

	private static string Normalize(string accountKey) => accountKey.Trim().ToLowerInvariant();
	private sealed record AttemptState(DateTimeOffset FirstFailure, int Failures, DateTimeOffset? BlockedUntil);
}
