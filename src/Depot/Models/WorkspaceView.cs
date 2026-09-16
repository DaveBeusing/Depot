// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum WorkspaceGridDensity
{
	Compact = 1,
	Standard = 2,
	Comfortable = 3
}

public enum WorkspaceSortDirection
{
	Ascending = 1,
	Descending = 2
}

public sealed record WorkspaceColumnPreference(
	string ColumnId,
	int DisplayIndex,
	double Width,
	bool IsVisible);

public sealed record WorkspaceSortPreference(
	string ColumnId,
	WorkspaceSortDirection Direction,
	int Priority);

public sealed record WorkspaceFilterPreference(
	string FilterId,
	string? Value);

public sealed class WorkspaceViewDefinition
{
	public const int CurrentFormatVersion = 1;

	public int FormatVersion { get; init; } = CurrentFormatVersion;
	public WorkspaceGridDensity GridDensity { get; init; } = WorkspaceGridDensity.Standard;
	public List<WorkspaceColumnPreference> Columns { get; init; } = [];
	public List<WorkspaceSortPreference> Sorts { get; init; } = [];
	public List<WorkspaceFilterPreference> Filters { get; init; } = [];
}

public sealed record SavedWorkspaceView(
	Guid ViewId,
	string WorkspaceId,
	string Name,
	WorkspaceViewDefinition Definition,
	bool IsDefault,
	DateTime UpdatedUtc,
	long Version);

public sealed record UserWorkspaceViewRecord(
	long Id,
	long UserId,
	string WorkspaceId,
	Guid ViewId,
	string Name,
	string DefinitionJson,
	bool IsDefault,
	DateTime UpdatedUtc,
	long Version);
