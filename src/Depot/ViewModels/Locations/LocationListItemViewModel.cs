// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels.Locations;

/// <summary>
/// Represents a location in the location list.
/// </summary>
public sealed class LocationListItemViewModel
	: BaseViewModel
{
	public LocationListItemViewModel(
		Location location)
	{
		Id = location.Id;
		Name = location.Name;
		Description = location.Description;
	}

	public long Id { get; }

	public string Name { get; }

	public string? Description { get; }
}