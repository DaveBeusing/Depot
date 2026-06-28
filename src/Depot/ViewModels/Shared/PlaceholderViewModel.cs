// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels.Shared;

/// <summary>
/// Represents a placeholder page for modules that have not yet been implemented.
/// </summary>
public sealed class PlaceholderViewModel
	: BaseViewModel
{
	public PlaceholderViewModel(
		string title,
		string description)
	{
		Title = title;
		Description = description;
	}

	public string Title { get; }

	public string Description { get; }
}