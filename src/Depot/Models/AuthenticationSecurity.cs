// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class AuthenticationSecurityPolicy
{
	public const int DefaultFailureWindowMinutes = 15;
	public const int DefaultLockoutThreshold = 5;
	public const int DefaultLockoutDurationMinutes = 15;
	public const int DefaultSecurityEventRetentionDays = 365;
	public const int MinimumFailureWindowMinutes = 1;
	public const int MaximumFailureWindowMinutes = 1440;
	public const int MinimumLockoutThreshold = 3;
	public const int MaximumLockoutThreshold = 20;
	public const int MinimumLockoutDurationMinutes = 1;
	public const int MaximumLockoutDurationMinutes = 1440;
	public const int MinimumSecurityEventRetentionDays = 30;
	public const int MaximumSecurityEventRetentionDays = 3650;

	public long Id { get; set; } = 1;
	public int FailureWindowMinutes { get; set; } = DefaultFailureWindowMinutes;
	public int LockoutThreshold { get; set; } = DefaultLockoutThreshold;
	public int LockoutDurationMinutes { get; set; } = DefaultLockoutDurationMinutes;
	public int SecurityEventRetentionDays { get; set; } = DefaultSecurityEventRetentionDays;
	public DateTime UpdatedUtc { get; set; }
	public long Version { get; set; } = 1;

	public TimeSpan FailureWindow => TimeSpan.FromMinutes(FailureWindowMinutes);
	public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutDurationMinutes);
}

public sealed class AuthenticationThrottleState
{
	public string AccountKey { get; set; } = string.Empty;
	public DateTime FirstFailureUtc { get; set; }
	public int FailureCount { get; set; }
	public DateTime? BlockedUntilUtc { get; set; }
	public DateTime UpdatedUtc { get; set; }
	public long Version { get; set; } = 1;
}

public sealed record AuthenticationThrottleSnapshot(int FailureCount, bool IsBlocked, TimeSpan RetryAfter);
