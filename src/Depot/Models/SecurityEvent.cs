// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SecurityEventType
{
	AuthenticationSucceeded = 1,
	AuthenticationFailed = 2,
	SuspiciousAuthenticationFailures = 3,
	AuthenticationBlocked = 4,
	AuthenticationSucceededAfterFailures = 5,
	SessionExpired = 6,
	SessionRevoked = 7,
	AdministrativeSessionTermination = 8,
	SessionPolicyChanged = 9
}

public enum SecurityEventSeverity
{
	Information = 1,
	Warning = 2,
	High = 3,
	Critical = 4
}

public sealed class SecurityEvent
{
	public long Id { get; set; }
	public DateTime TimestampUtc { get; set; }
	public SecurityEventType EventType { get; set; }
	public SecurityEventSeverity Severity { get; set; }
	public long? UserId { get; set; }
	public string? AccountIdentifier { get; set; }
	public Guid? SessionId { get; set; }
	public string? MachineName { get; set; }
	public string Summary { get; set; } = string.Empty;
	public string? Details { get; set; }
	public DateTime? ReviewedUtc { get; set; }
	public long? ReviewedByUserId { get; set; }
	public long Version { get; set; } = 1;
}

public sealed record SecurityCenterMetrics(long Events24Hours, long Suspicious24Hours, long HighRiskOpen, long Blocked24Hours);

public sealed record SecurityEventFilter(string? SearchText, SecurityEventSeverity? MinimumSeverity, bool? Reviewed);

public sealed class SecurityEventListItem
{
	public long Id { get; init; }
	public DateTime TimestampUtc { get; init; }
	public SecurityEventType EventType { get; init; }
	public SecurityEventSeverity Severity { get; init; }
	public long? UserId { get; init; }
	public string? AccountIdentifier { get; init; }
	public Guid? SessionId { get; init; }
	public string? MachineName { get; init; }
	public string Summary { get; init; } = string.Empty;
	public string? Details { get; init; }
	public DateTime? ReviewedUtc { get; init; }
	public long? ReviewedByUserId { get; init; }
	public long Version { get; init; }
	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
	public bool IsReviewed => ReviewedUtc is not null;
}

public sealed record LoginAttemptStatus(int FailureCount, bool IsBlocked, TimeSpan RetryAfter);
