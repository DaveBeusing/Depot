// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class ImportPreview
{
	public IReadOnlyList<ImportPreviewItem> Items { get; init; }
		= [];

	public IReadOnlyList<ImportWarning> Warnings { get; init; }
		= [];

	public int TotalItems =>
		Items.Count;

	public int NewItems =>
		Items.Count(
			x => !x.ItemAlreadyExists);

	public int ExistingItems =>
		Items.Count(
			x => x.ItemAlreadyExists);
}