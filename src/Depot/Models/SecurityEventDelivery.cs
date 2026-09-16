// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SecurityEventDeliveryFailureKind
{
	Transient = 1,
	Permanent = 2
}

public sealed record SecurityEventExportTarget
{
	public long Id { get; init; }
	public string Code { get; init; } = string.Empty;
	public string SinkCode { get; init; } = string.Empty;
	public string EndpointUri { get; init; } = string.Empty;
	public SecurityEventSeverity MinimumSeverity { get; init; } = SecurityEventSeverity.Information;
	public IReadOnlyList<SecurityEventType> EventTypes { get; init; } = [];
	public int BatchSize { get; init; } = 100;
	public bool IsEnabled { get; init; } = true;
	public DateTime CreatedUtc { get; init; }
	public DateTime UpdatedUtc { get; init; }
	public long Version { get; init; } = 1;

	public SecurityEventExportFilter Filter => new()
	{
		MinimumSeverity = MinimumSeverity,
		EventTypes = EventTypes
	};
}

public sealed record SecurityEventDeliveryState
{
	public long TargetId { get; init; }
	public long LastEventId { get; init; }
	public string FilterSha256 { get; init; } = string.Empty;
	public long? PendingSnapshotUpperBoundId { get; init; }
	public DateTime? PendingStartedUtc { get; init; }
	public int ConsecutiveFailures { get; init; }
	public DateTime? NextAttemptUtc { get; init; }
	public DateTime? LastAttemptUtc { get; init; }
	public DateTime? LastSuccessUtc { get; init; }
	public SecurityEventDeliveryFailureKind? LastFailureKind { get; init; }
	public string? LastFailureCode { get; init; }
	public string? LastFailureMessage { get; init; }
	public bool IsSuspended { get; init; }
	public Guid? LeaseToken { get; init; }
	public DateTime? LeaseUntilUtc { get; init; }
	public DateTime UpdatedUtc { get; init; }
	public long Version { get; init; } = 1;
}

public sealed record SecurityEventDeliveryStatus(SecurityEventExportTarget Target, SecurityEventDeliveryState State);

public sealed record SecurityEventDeliveryRunResult(
	long TargetId,
	string TargetCode,
	int DeliveredEvents,
	long CheckpointEventId,
	bool Succeeded,
	bool Deferred,
	bool Suspended,
	SecurityEventDeliveryFailureKind? FailureKind,
	string? FailureCode);
