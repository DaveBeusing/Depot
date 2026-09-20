// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class ImportMappingDesignerTests
{
	[Fact]
	public void DefaultMappingPreservesLegacyWorkbookHeaders()
	{
		var sources = ImportTargetCatalog.All
			.Where(definition => definition.Field != ImportTargetField.Unmapped)
			.Select((definition, index) => (index + 1, definition.DefaultHeader));

		var mapping = ImportMapping.CreateDefault(sources);

		Assert.True(mapping.IsValid);
		foreach (var definition in ImportTargetCatalog.All.Where(definition => definition.Field != ImportTargetField.Unmapped))
			Assert.NotNull(mapping.GetColumnNumber(definition.Field));
	}

	[Fact]
	public void OptionalWarehouseMayRemainUnmapped()
	{
		var sources = ImportTargetCatalog.All
			.Where(definition => definition.IsRequired)
			.Select((definition, index) => (index + 1, definition.DefaultHeader));

		var mapping = ImportMapping.CreateDefault(sources);

		Assert.True(mapping.IsValid);
		Assert.Null(mapping.GetColumnNumber(ImportTargetField.Warehouse));
		Assert.Equal("Main Warehouse", ImportTargetCatalog.Get(ImportTargetField.Warehouse).DefaultValue);
	}

	[Fact]
	public void MissingRequiredTargetBlocksMapping()
	{
		var sources = ImportTargetCatalog.All
			.Where(definition => definition.IsRequired && definition.Field != ImportTargetField.PartNumber)
			.Select((definition, index) => (index + 1, definition.DefaultHeader));

		var mapping = ImportMapping.CreateDefault(sources);

		Assert.False(mapping.IsValid);
		Assert.Contains(
			mapping.Issues,
			issue =>
				issue.Severity == ImportMappingIssueSeverity.Error &&
				issue.TargetField == ImportTargetField.PartNumber);
	}

	[Fact]
	public void DuplicateSourceHeaderIsVisibleAndDuplicateTargetBlocksPreview()
	{
		var sources = ImportTargetCatalog.All
			.Where(definition => definition.IsRequired)
			.Select((definition, index) => (ColumnNumber: index + 1, Header: definition.DefaultHeader))
			.ToList();
		sources.Add((sources.Count + 1, "P/N"));

		var mapping = ImportMapping.CreateDefault(sources);

		Assert.False(mapping.IsValid);
		Assert.Contains(
			mapping.Issues,
			issue =>
				issue.Severity == ImportMappingIssueSeverity.Warning &&
				issue.Message.Contains("occurs 2 times", StringComparison.Ordinal));
		Assert.Contains(
			mapping.Issues,
			issue =>
				issue.Severity == ImportMappingIssueSeverity.Error &&
				issue.TargetField == ImportTargetField.PartNumber);
	}

	[Fact]
	public void ManualMappingSupportsNonStandardHeadersWithoutPersistence()
	{
		var columns = ImportTargetCatalog.All
			.Where(definition => definition.IsRequired)
			.Select((definition, index) =>
				new ImportSourceColumn(
					index + 1,
					$"External {index + 1}",
					definition.Field))
			.ToList();

		var mapping = new ImportMapping(columns);

		Assert.True(mapping.IsValid);
		Assert.Empty(mapping.Issues);
	}

	[Fact]
	public void PreviewMustBeRegeneratedAfterMappingChangesBeforeExecute()
	{
		var root = FindRepositoryRoot();
		var source = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ImportViewModel.cs"));

		Assert.Contains("PreviewCommand = new AsyncRelayCommand", source, StringComparison.Ordinal);
		Assert.Contains("RebuildMapping(invalidatePreview: true)", source, StringComparison.Ordinal);
		Assert.Contains("ClearPreview();", source, StringComparison.Ordinal);
		Assert.Contains("_currentPreview is not null", source, StringComparison.Ordinal);
		Assert.Contains("BlockingRowErrors == 0", source, StringComparison.Ordinal);
		Assert.Contains("_currentPreview = null;", source, StringComparison.Ordinal);
	}

	[Fact]
	public void DesignerExposesKeyboardMappingValidationAndRowWarnings()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ImportView.xaml"));

		Assert.Contains("Import Mapping Designer", view, StringComparison.Ordinal);
		Assert.Contains("Source columns stay on the left", view, StringComparison.Ordinal);
		Assert.Contains("keyboard accessible", view, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("ItemsSource=\"{Binding TargetOptions}\"", view, StringComparison.Ordinal);
		Assert.Contains("SelectedItem=\"{Binding SelectedTarget, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", view, StringComparison.Ordinal);
		Assert.Contains("AppComboBoxStyle", view, StringComparison.Ordinal);
		Assert.Contains("Generate validated preview", view, StringComparison.Ordinal);
		Assert.Contains("WarningItems", view, StringComparison.Ordinal);
		Assert.Contains("SeverityDisplay", view, StringComparison.Ordinal);
		Assert.Contains("EnableRowVirtualization=\"True\"", view, StringComparison.Ordinal);
		Assert.DoesNotContain("DragDrop", view, StringComparison.Ordinal);
		Assert.DoesNotContain("Mouse", view, StringComparison.Ordinal);
	}

	[Fact]
	public void ImportMappingRemainsInMemoryAndCancellationAware()
	{
		var root = FindRepositoryRoot();
		var model = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "ImportMapping.cs"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "Import", "ImportService.cs"));
		var databaseVersion = File.ReadAllText(Path.Combine(root, "src", "Depot", "Data", "DatabaseVersion.cs"));

		Assert.DoesNotContain("Repository", model, StringComparison.Ordinal);
		Assert.DoesNotContain("Database", model, StringComparison.Ordinal);
		Assert.Contains("InspectMapping(", service, StringComparison.Ordinal);
		Assert.Contains("CreatePreview(", service, StringComparison.Ordinal);
		Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", service, StringComparison.Ordinal);
		Assert.Contains("public const int CurrentVersion = 30;", databaseVersion, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
