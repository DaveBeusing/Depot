// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record SecurityEventExportFilter
{
	public SecurityEventSeverity MinimumSeverity { get; init; } = SecurityEventSeverity.Information;
	public IReadOnlyList<SecurityEventType> EventTypes { get; init; } = [];
}

public sealed record SecurityEventExportCheckpoint(long LastEventId, string FilterSha256);

public sealed record SecurityEventExportRecord(
	long EventId,
	DateTime TimestampUtc,
	SecurityEventType EventType,
	SecurityEventSeverity Severity,
	long? UserId,
	string? AccountIdentifier,
	Guid? SessionId,
	Guid? ClientInstanceId,
	string? MachineName,
	string Summary,
	string? Details);

public sealed record SecurityEventExportBatch(
	int FormatVersion,
	long SnapshotUpperBoundId,
	SecurityEventExportCheckpoint StartCheckpoint,
	SecurityEventExportCheckpoint NextCheckpoint,
	IReadOnlyList<SecurityEventExportRecord> Events,
	bool HasMore)
{
	public const int CurrentFormatVersion = 1;
}
