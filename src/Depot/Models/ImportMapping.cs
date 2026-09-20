// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ImportTargetField
{
	Unmapped,
	PartNumber,
	Description,
	Manufacturer,
	Category,
	Purpose,
	Warehouse,
	Location,
	Quantity,
	UnitPrice
}

public enum ImportMappingIssueSeverity
{
	Warning,
	Error
}

public sealed record ImportTargetDefinition(
	ImportTargetField Field,
	string DisplayName,
	string DefaultHeader,
	bool IsRequired,
	string? DefaultValue = null);

public sealed record ImportSourceColumn(
	int ColumnNumber,
	string Header,
	ImportTargetField TargetField);

public sealed record ImportMappingIssue(
	ImportMappingIssueSeverity Severity,
	string Message,
	ImportTargetField? TargetField = null,
	int? SourceColumnNumber = null);

public static class ImportTargetCatalog
{
	public static IReadOnlyList<ImportTargetDefinition> All { get; } =
	[
		new(ImportTargetField.Unmapped, "Do not import", string.Empty, false),
		new(ImportTargetField.PartNumber, "Part number", "P/N", true),
		new(ImportTargetField.Description, "Description", "Item Description", true),
		new(ImportTargetField.Manufacturer, "Manufacturer", "Manufacturer", true),
		new(ImportTargetField.Category, "Item category", "Item Category", true),
		new(ImportTargetField.Purpose, "Purpose", "Purpose", true),
		new(ImportTargetField.Warehouse, "Warehouse", "Warehouse", false, "Main Warehouse"),
		new(ImportTargetField.Location, "Location", "Location", true),
		new(ImportTargetField.Quantity, "Current inventory", "Current Inventory", true),
		new(ImportTargetField.UnitPrice, "Unit price", "Unit Price", true)
	];

	public static ImportTargetDefinition Get(ImportTargetField field) =>
		All.First(definition => definition.Field == field);

	public static bool TryMatchDefaultHeader(string header, out ImportTargetField field)
	{
		var definition = All.FirstOrDefault(
			candidate =>
				candidate.Field != ImportTargetField.Unmapped &&
				string.Equals(candidate.DefaultHeader, header, StringComparison.OrdinalIgnoreCase));

		field = definition?.Field ?? ImportTargetField.Unmapped;
		return definition is not null;
	}
}

public sealed class ImportMapping
{
	public ImportMapping(IReadOnlyList<ImportSourceColumn> columns)
	{
		Columns = columns ?? throw new ArgumentNullException(nameof(columns));
		Issues = Validate(columns);
	}

	public IReadOnlyList<ImportSourceColumn> Columns { get; }

	public IReadOnlyList<ImportMappingIssue> Issues { get; }

	public bool IsValid =>
		Issues.All(issue => issue.Severity != ImportMappingIssueSeverity.Error);

	public int? GetColumnNumber(ImportTargetField field)
	{
		var match = Columns.FirstOrDefault(column => column.TargetField == field);
		return match?.ColumnNumber;
	}

	public static ImportMapping CreateDefault(IEnumerable<(int ColumnNumber, string Header)> sourceColumns)
	{
		ArgumentNullException.ThrowIfNull(sourceColumns);

		var columns = sourceColumns
			.Select(source =>
			{
				ImportTargetCatalog.TryMatchDefaultHeader(source.Header, out var target);
				return new ImportSourceColumn(source.ColumnNumber, source.Header, target);
			})
			.ToList();

		return new ImportMapping(columns);
	}

	private static IReadOnlyList<ImportMappingIssue> Validate(IReadOnlyList<ImportSourceColumn> columns)
	{
		var issues = new List<ImportMappingIssue>();

		foreach (var duplicateHeader in columns
			.Where(column => !string.IsNullOrWhiteSpace(column.Header))
			.GroupBy(column => column.Header, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1))
		{
			issues.Add(new ImportMappingIssue(
				ImportMappingIssueSeverity.Warning,
				$"Source header '{duplicateHeader.Key}' occurs {duplicateHeader.Count()} times. Verify the intended mapping."));
		}

		foreach (var unnamed in columns.Where(column => string.IsNullOrWhiteSpace(column.Header)))
		{
			issues.Add(new ImportMappingIssue(
				ImportMappingIssueSeverity.Warning,
				$"Source column {unnamed.ColumnNumber} has no header.",
				SourceColumnNumber: unnamed.ColumnNumber));
		}

		foreach (var duplicateTarget in columns
			.Where(column => column.TargetField != ImportTargetField.Unmapped)
			.GroupBy(column => column.TargetField)
			.Where(group => group.Count() > 1))
		{
			var definition = ImportTargetCatalog.Get(duplicateTarget.Key);
			issues.Add(new ImportMappingIssue(
				ImportMappingIssueSeverity.Error,
				$"Target field '{definition.DisplayName}' is mapped more than once.",
				duplicateTarget.Key));
		}

		foreach (var required in ImportTargetCatalog.All.Where(definition => definition.IsRequired))
		{
			if (columns.All(column => column.TargetField != required.Field))
			{
				issues.Add(new ImportMappingIssue(
					ImportMappingIssueSeverity.Error,
					$"Required target field '{required.DisplayName}' is not mapped.",
					required.Field));
			}
		}

		return issues;
	}
}
