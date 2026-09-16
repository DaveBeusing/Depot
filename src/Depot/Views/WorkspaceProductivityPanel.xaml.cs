// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Models;

namespace Depot.Views;

public partial class WorkspaceProductivityPanel : UserControl
{
	public WorkspaceProductivityPanel() => InitializeComponent();

	private async void OnOpenClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: WorkspaceShortcut shortcut } && Window.GetWindow(this) is MainWindow { WorkspaceProductivity: { } productivity })
			await productivity.OpenAsync(shortcut.RouteId);
	}

	private async void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: WorkspaceShortcut shortcut } && Window.GetWindow(this) is MainWindow { WorkspaceProductivity: { } productivity })
			await productivity.ToggleFavoriteAsync(shortcut.RouteId);
	}

	private async void OnSetDefaultClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: WorkspaceShortcut shortcut } && Window.GetWindow(this) is MainWindow { WorkspaceProductivity: { } productivity })
			await productivity.SetDefaultAsync(shortcut.RouteId);
	}

	private async void OnClearDefaultClick(object sender, RoutedEventArgs e)
	{
		if (Window.GetWindow(this) is MainWindow { WorkspaceProductivity: { } productivity })
			await productivity.ClearDefaultAsync();
	}
}
