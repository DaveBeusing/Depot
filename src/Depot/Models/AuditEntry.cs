// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class AuditEntry
{
	public long Id { get; set; }
	public DateTime TimestampUtc { get; set; }
	public long? UserId { get; set; }
	public string UserEmail { get; set; } = string.Empty;
	public string EntityType { get; set; } = string.Empty;
	public long EntityId { get; set; }
	public string Action { get; set; } = string.Empty;
	public string? BeforeJson { get; set; }
	public string? AfterJson { get; set; }
}
