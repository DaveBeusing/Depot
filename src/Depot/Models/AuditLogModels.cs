// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record AuditLogFilter(
	string? SearchText,
	DateTime? FromUtc,
	DateTime? ToUtcExclusive,
	string? UserEmail,
	string? EntityType,
	string? Action,
	long? EntityId);

public sealed record AuditLogListItem(
	long Id,
	DateTime TimestampUtc,
	long? UserId,
	string UserEmail,
	string EntityType,
	long EntityId,
	string Action)
{
	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}

public sealed record AuditLogDetails(
	long Id,
	DateTime TimestampUtc,
	long? UserId,
	string UserEmail,
	string EntityType,
	long EntityId,
	string Action,
	string? BeforeJson,
	string? AfterJson)
{
	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}

public sealed record AuditLogFilterOptions(
	IReadOnlyList<string> EntityTypes,
	IReadOnlyList<string> Actions);

public sealed record AuditValueChange(string Property, string Before, string After);

public sealed record SanitizedAuditDetails(
	AuditLogDetails Entry,
	string BeforeJson,
	string AfterJson,
	IReadOnlyList<AuditValueChange> Changes);
