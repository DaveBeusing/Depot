// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class MaterialIssueOverviewItem
{
	public long Id { get; init; }
	public string IssueNumber { get; init; } = string.Empty;
	public DateTime IssueDate { get; init; }
	public MaterialIssueStatus Status { get; init; }
	public string Recipient { get; init; } = string.Empty;
	public string? Reference { get; init; }
	public string? Notes { get; init; }
	public string CreatedByUserName { get; init; } = string.Empty;
	public int LineCount { get; init; }
	public DateTime? PostedAtUtc { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public string? ReversalReason { get; init; }
	public long Version { get; init; }
	public string StatusDisplayName => Status.ToString();
}
