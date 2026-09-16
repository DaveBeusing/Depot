// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Input;
using Depot.Services.Help;

using Xunit;

namespace Depot.Tests;

public sealed class TrackBProductivityReadinessTests
{
	[Fact]
	public async Task WorkspaceHelpDocumentsGlobalSearchAndEveryWorkflowShortcut()
	{
		var content = await LoadHelpAsync("getting-started.workspace-navigation");

		Assert.Contains("Ctrl+K", content, StringComparison.Ordinal);
		Assert.Contains("Global Search", content, StringComparison.Ordinal);
		foreach (var shortcut in WorkflowShortcutCatalog.Definitions)
			Assert.Contains($"`{shortcut.GestureText}`", content, StringComparison.Ordinal);
		Assert.Contains("CanExecute", content, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WorkspaceHelpDocumentsPermissionAwarePersonalProductivity()
	{
		var content = await LoadHelpAsync("getting-started.workspace-navigation");
		var dashboard = await LoadHelpAsync("getting-started.dashboard");

		Assert.Contains("Favorites", content, StringComparison.Ordinal);
		Assert.Contains("Recently used", content, StringComparison.Ordinal);
		Assert.Contains("Default landing workspace", content, StringComparison.Ordinal);
		Assert.Contains("stored route is never treated as authorization", content, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("My workspace", dashboard, StringComparison.Ordinal);
		Assert.Contains("safe", dashboard, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("permission", dashboard, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("inventory.overview")]
	[InlineData("inventory.traceability")]
	public async Task InitialSavedViewWorkspacesDocumentPersistenceAndFailureSafety(string topicId)
	{
		var content = await LoadHelpAsync(topicId);

		Assert.Contains("Saved Views", content, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("presentation", content, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Reset", content, StringComparison.Ordinal);
		Assert.Contains("permission", content, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void TrackBShortcutLayerKeepsGlobalSearchGestureReserved()
	{
		Assert.DoesNotContain(
			WorkflowShortcutCatalog.Definitions.Where(definition => !definition.IsShellHandled),
			definition => definition.Key == System.Windows.Input.Key.K && definition.Modifiers == System.Windows.Input.ModifierKeys.Control);
	}

	private static async Task<string> LoadHelpAsync(string topicId)
	{
		var provider = new EmbeddedHelpContentProvider(typeof(App).Assembly);
		var manifest = await provider.LoadManifestAsync();
		var definition = Assert.Single(manifest.Topics.Where(topic => string.Equals(topic.Id, topicId, StringComparison.Ordinal)));
		return await provider.LoadContentAsync(definition);
	}
}
