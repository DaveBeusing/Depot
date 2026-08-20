// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SalesOrderTimelineItem
{
	public DateTime TimestampUtc { get; set; }
	public string EventType { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string? Details { get; set; }
	public string? Reference { get; set; }
	public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}
