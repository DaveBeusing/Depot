// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Input;

using Depot.Input;
using Depot.ViewModels;

namespace Depot;

public partial class MainWindow
{
	private static readonly RoutedUICommand WorkflowSearchCommand = new(
		"Search",
		nameof(WorkflowSearchCommand),
		typeof(MainWindow));

	static MainWindow()
	{
		CommandManager.RegisterClassCommandBinding(
			typeof(MainWindow),
			new CommandBinding(WorkflowSearchCommand, OnWorkflowSearchExecuted, OnWorkflowSearchCanExecute));
		EventManager.RegisterClassHandler(
			typeof(MainWindow),
			Keyboard.PreviewKeyDownEvent,
			new KeyEventHandler(OnWorkflowShortcutPreviewKeyDown),
			true);
		WorkflowShortcutHints.Initialize();
	}

	private static void OnWorkflowShortcutPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Handled || sender is not MainWindow window) return;
		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (!WorkflowShortcutCatalog.TryMatch(key, Keyboard.Modifiers, out var definition)) return;

		// Ctrl+W is already the established shell close shortcut in MainWindow.OnPreviewKeyDown.
		// Leaving it to that path avoids duplicate close execution while keeping one documented catalog.
		if (definition.Action == WorkflowShortcutAction.Close) return;

		var current = window.DataContext is MainViewModel main ? main.CurrentViewModel : window.DataContext;
		var context = WorkflowShortcutRouter.FindContext(Keyboard.FocusedElement as DependencyObject, current);
		if (!WorkflowShortcutRouter.TryExecute(context, definition, window.ResolveShellWorkflowCommand, window)) return;
		e.Handled = true;
	}

	private ICommand? ResolveShellWorkflowCommand(WorkflowShortcutAction action) =>
		action == WorkflowShortcutAction.Search ? WorkflowSearchCommand : null;

	private static void OnWorkflowSearchCanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		e.CanExecute = sender is MainWindow window &&
			window.DataContext is MainViewModel &&
			window._globalSearchService is not null &&
			window._globalSearchHelpService is not null;
		e.Handled = true;
	}

	private static void OnWorkflowSearchExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (sender is not MainWindow window) return;
		window.OpenGlobalSearchPalette();
		e.Handled = true;
	}
}
