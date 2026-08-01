// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class MaterialIssue
{
	public long Id { get; set; }
	public string IssueNumber { get; set; } = string.Empty;
	public DateTime IssueDate { get; set; } = DateTime.Today;
	public MaterialIssueStatus Status { get; set; } = MaterialIssueStatus.Draft;
	public string Recipient { get; set; } = string.Empty;
	public string? Reference { get; set; }
	public string? Notes { get; set; }
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public long? ReversedByUserId { get; set; }
	public DateTime? ReversedAtUtc { get; set; }
	public string? ReversalReason { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<MaterialIssueLine> Lines { get; set; } = [];
}
