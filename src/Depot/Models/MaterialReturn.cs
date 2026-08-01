// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class MaterialReturn
{
	public long Id { get; set; }
	public string ReturnNumber { get; set; } = string.Empty;
	public DateTime ReturnDate { get; set; } = DateTime.Today;
	public MaterialReturnStatus Status { get; set; } = MaterialReturnStatus.Draft;
	public string RecipientOrSource { get; set; } = string.Empty;
	public long? OriginalMaterialIssueId { get; set; }
	public string? OriginalMaterialIssueNumber { get; set; }
	public string? Reference { get; set; }
	public string? Notes { get; set; }
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public DateTime? PostedAtUtc { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<MaterialReturnLine> Lines { get; set; } = [];
}
