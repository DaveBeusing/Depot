// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ImportMappingItemViewModel
	: BaseViewModel
{
	private ImportTargetDefinition _selectedTarget;

	public ImportMappingItemViewModel(
		ImportSourceColumn sourceColumn)
	{
		ColumnNumber = sourceColumn.ColumnNumber;
		SourceHeader = sourceColumn.Header;
		_selectedTarget = ImportTargetCatalog.Get(sourceColumn.TargetField);
	}

	public int ColumnNumber { get; }

	public string SourceHeader { get; }

	public string SourceDisplay =>
		string.IsNullOrWhiteSpace(SourceHeader)
			? $"Column {ColumnNumber} (unnamed)"
			: SourceHeader;

	public IReadOnlyList<ImportTargetDefinition> TargetOptions =>
		ImportTargetCatalog.All;

	public ImportTargetDefinition SelectedTarget
	{
		get => _selectedTarget;
		set
		{
			if (value is null || _selectedTarget == value)
			{
				return;
			}

			_selectedTarget = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(TargetStatus));
			MappingChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	public string TargetStatus =>
		SelectedTarget.Field == ImportTargetField.Unmapped
			? "Ignored"
			: SelectedTarget.IsRequired
				? "Required target"
				: "Optional target";

	public event EventHandler? MappingChanged;

	public ImportSourceColumn ToModel() =>
		new(
			ColumnNumber,
			SourceHeader,
			SelectedTarget.Field);

	public void ResetDefault()
	{
		ImportTargetCatalog.TryMatchDefaultHeader(SourceHeader, out var target);
		SelectedTarget = ImportTargetCatalog.Get(target);
	}
}
