// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows.Input;

namespace Depot.Commands;

public static class HelpCommands
{
	public static RoutedUICommand OpenHelp { get; } = new(
		"Open Help",
		nameof(OpenHelp),
		typeof(HelpCommands),
		new InputGestureCollection { new KeyGesture(Key.F1) });
	public static RoutedUICommand OpenTopic { get; } = new("Open Help Topic", nameof(OpenTopic), typeof(HelpCommands));
	public static RoutedUICommand CopyDiagnostics { get; } = new("Copy Diagnostics", nameof(CopyDiagnostics), typeof(HelpCommands));
}
