// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class MaterialReturnOverviewItem
{
	public long Id { get; init; }
	public string ReturnNumber { get; init; } = string.Empty;
	public DateTime ReturnDate { get; init; }
	public MaterialReturnStatus Status { get; init; }
	public string RecipientOrSource { get; init; } = string.Empty;
	public string? OriginalMaterialIssueNumber { get; init; }
	public string? Reference { get; init; }
	public string? Notes { get; init; }
	public string CreatedByUserName { get; init; } = string.Empty;
	public int LineCount { get; init; }
	public DateTime? PostedAtUtc { get; init; }
	public long Version { get; init; }
	public string StatusDisplayName => Status.ToString();
}
