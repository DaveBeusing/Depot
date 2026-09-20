// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record ApprovalFlowProjection
{
	public IReadOnlyList<WorkflowTimelineItem> Steps { get; init; } = [];
	public string RequiredPermission { get; init; } = "None";
	public string SeparationOfDuties { get; init; } = string.Empty;
	public string NextActions { get; init; } = string.Empty;
	public string RuleSummary { get; init; } = string.Empty;
}
