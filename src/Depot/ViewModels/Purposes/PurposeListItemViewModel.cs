// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels.Purposes;

/// <summary>
/// Represents a purpose in the purpose list.
/// </summary>
public sealed class PurposeListItemViewModel
	: BaseViewModel
{
	public PurposeListItemViewModel(
		Purpose purpose)
	{
		Id = purpose.Id;
		Name = purpose.Name;
		Description = purpose.Description;
		Version = purpose.Version;
	}

	public long Id { get; }

	public string Name { get; }

	public string? Description { get; }

	public long Version { get; }
}
