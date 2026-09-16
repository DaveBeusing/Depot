// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections;
using System.Windows.Input;

using Depot.Input;

using Xunit;

namespace Depot.Tests;

public sealed class WorkflowShortcutTests
{
	[Fact]
	public void CatalogUsesUniqueGesturesAndAvoidsEstablishedShellNavigationGestures()
	{
		var definitions = WorkflowShortcutCatalog.Definitions;
		Assert.Equal(
			definitions.Count,
			definitions.Select(definition => (definition.Key, definition.Modifiers)).Distinct().Count());

		var establishedShellGestures = new HashSet<(Key Key, ModifierKeys Modifiers)>
		{
			(Key.K, ModifierKeys.Control),
			(Key.P, ModifierKeys.Control),
			(Key.P, ModifierKeys.Control | ModifierKeys.Shift),
			(Key.Tab, ModifierKeys.Control),
			(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift),
			(Key.Left, ModifierKeys.Alt),
			(Key.Right, ModifierKeys.Alt),
			(Key.F1, ModifierKeys.None)
		};

		Assert.DoesNotContain(
			definitions.Where(definition => !definition.IsShellHandled),
			definition => establishedShellGestures.Contains((definition.Key, definition.Modifiers)));
	}

	[Fact]
	public void EveryRegistrationTargetsSupportedProperties()
	{
		foreach (var registration in WorkflowShortcutCommandRegistry.Registrations)
		{
			if (registration.Kind is WorkflowShortcutRegistrationKind.Direct or WorkflowShortcutRegistrationKind.Sequential)
				Assert.True(typeof(ICommand).IsAssignableFrom(registration.PrimaryProperty.PropertyType));
			if (registration.Kind == WorkflowShortcutRegistrationKind.Sequential)
				Assert.True(typeof(ICommand).IsAssignableFrom(registration.SecondaryProperty!.PropertyType));
			if (registration.Kind == WorkflowShortcutRegistrationKind.RelativeSelection)
			{
				Assert.True(registration.PrimaryProperty.CanWrite);
				Assert.True(typeof(IEnumerable).IsAssignableFrom(registration.CollectionProperty!.PropertyType));
			}
		}
	}

	[Fact]
	public void DisabledCommandIsNotExecutedOrHandled()
	{
		var command = new TrackingCommand(false);

		var handled = WorkflowShortcutRouter.TryExecute(command, null);

		Assert.False(handled);
		Assert.Equal(0, command.ExecuteCount);
	}

	[Fact]
	public void EnabledCommandExecutesExactlyOnce()
	{
		var command = new TrackingCommand(true);

		var handled = WorkflowShortcutRouter.TryExecute(command, null);

		Assert.True(handled);
		Assert.Equal(1, command.ExecuteCount);
	}

	[Fact]
	public void SaveAndNewCompositionUsesExistingCommandsInOrder()
	{
		var sequence = new List<string>();
		var save = new TrackingCommand(true, () => sequence.Add("save"));
		var createNew = new TrackingCommand(true, () => sequence.Add("new"));
		var command = new SequentialWorkflowCommand(null, save, createNew);

		command.Execute(null);

		Assert.Equal(new[] { "save", "new" }, sequence);
	}

	[Fact]
	public void RelativeSelectionCommandMovesOnlyWithinTheExistingCollection()
	{
		var context = new SelectionContext
		{
			Values = new ArrayList { "A", "B", "C" },
			Selected = "B"
		};
		var property = typeof(SelectionContext).GetProperty(nameof(SelectionContext.Selected))!;
		var next = new RelativeSelectionWorkflowCommand(context, property, context.Values, 1);
		var previous = new RelativeSelectionWorkflowCommand(context, property, context.Values, -1);

		Assert.True(next.CanExecute(null));
		next.Execute(null);
		Assert.Equal("C", context.Selected);
		Assert.False(next.CanExecute(null));
		Assert.True(previous.CanExecute(null));
		previous.Execute(null);
		Assert.Equal("B", context.Selected);
	}

	[Fact]
	public void DraftLineDeletionUsesAnExplicitNonTextEditingGesture()
	{
		var definition = WorkflowShortcutCatalog.Get(WorkflowShortcutAction.DeleteDraftLine);

		Assert.Equal(Key.Delete, definition.Key);
		Assert.Equal(ModifierKeys.Alt, definition.Modifiers);
		Assert.True(definition.IsDestructive);
	}

	[Fact]
	public void SearchAndCloseRemainShellLevelActions()
	{
		var search = WorkflowShortcutCatalog.Get(WorkflowShortcutAction.Search);
		var close = WorkflowShortcutCatalog.Get(WorkflowShortcutAction.Close);

		Assert.True(search.IsShellHandled);
		Assert.True(close.IsShellHandled);
		Assert.Equal("Ctrl+F", search.GestureText);
		Assert.Equal("Ctrl+W", close.GestureText);
	}

	private sealed class TrackingCommand(bool canExecute, Action? execute = null) : ICommand
	{
		public int ExecuteCount { get; private set; }
		public bool CanExecute(object? parameter) => canExecute;
		public void Execute(object? parameter) { ExecuteCount++; execute?.Invoke(); }
		public event EventHandler? CanExecuteChanged { add { } remove { } }
	}

	private sealed class SelectionContext
	{
		public ArrayList Values { get; init; } = new();
		public string? Selected { get; set; }
	}
}
