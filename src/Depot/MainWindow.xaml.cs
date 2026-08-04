// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Input;

using Depot.ViewModels;
using Depot.Services.Help;

namespace Depot;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
	}

	private async void OnOpenHelpExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync();
		e.Handled = true;
	}

	private async void OnOpenHelpTopicExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync(e.Parameter as string);
		e.Handled = true;
	}

	private void OnCopyDiagnosticsExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		var sanitized = new DiagnosticsSanitizer().Sanitize(e.Parameter as string);
		if (!string.IsNullOrWhiteSpace(sanitized)) Clipboard.SetText(sanitized);
		e.Handled = true;
	}
}
