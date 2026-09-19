// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Controls;
using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class CommercialRoleCenterView : UserControl
{
	private const double DetailPaneCollapseWidth = 1366d;

	public CommercialRoleCenterView() => InitializeComponent();

	private async void OnQuickActionClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: CommercialRoleQuickAction action } &&
			Window.GetWindow(this)?.DataContext is MainViewModel main)
			await main.ExecuteCommercialRoleActionAsync(action);
	}

	private async void OnOpenClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: CommercialRoleItem item }) await OpenAsync(item);
	}

	private async void OnItemsDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (sender is DataGrid { SelectedItem: CommercialRoleItem item }) await OpenAsync(item);
	}

	private async void OnItemsKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter || sender is not DataGrid { SelectedItem: CommercialRoleItem item }) return;
		e.Handled = true;
		await OpenAsync(item);
	}

	private void OnItemsSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender is DataGrid { SelectedItem: CommercialRoleItem item } &&
			DataContext is CommercialRoleCenterViewModel viewModel)
			viewModel.SelectedItem = item;
	}

	private void OnRoleCenterGridLoaded(object sender, RoutedEventArgs e)
	{
		if (sender is not DataGrid grid || DataContext is not CommercialRoleCenterViewModel viewModel) return;
		var profile = CommercialRoleColumnProfiles.Get(viewModel.Kind);
		foreach (var column in grid.Columns)
		{
			var columnId = WorkspaceViewPersistence.GetColumnId(column);
			if (string.IsNullOrWhiteSpace(columnId)) continue;
			column.Visibility = profile.IsVisible(columnId) ? Visibility.Visible : Visibility.Collapsed;
			column.Header = profile.GetHeader(columnId, Convert.ToString(column.Header) ?? columnId);
		}
	}

	private void OnDetailPaneToggleClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is CommercialRoleCenterViewModel viewModel) viewModel.ToggleDetailPane();
	}

	private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (e.NewSize.Width < DetailPaneCollapseWidth &&
			DataContext is CommercialRoleCenterViewModel { IsDetailPaneVisible: true } viewModel)
			viewModel.IsDetailPaneVisible = false;
	}

	private async void OnApproveClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: CommercialRoleItem item } && DataContext is CommercialRoleCenterViewModel viewModel)
			await viewModel.DecideAsync(item, true);
	}

	private async void OnRejectClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: CommercialRoleItem item } && DataContext is CommercialRoleCenterViewModel viewModel)
			await viewModel.DecideAsync(item, false);
	}

	private async Task OpenAsync(CommercialRoleItem item)
	{
		if (Window.GetWindow(this)?.DataContext is MainViewModel main)
			await main.OpenCommercialRoleItemAsync(item);
	}
}
