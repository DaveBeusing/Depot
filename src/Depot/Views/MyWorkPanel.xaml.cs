// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class MyWorkPanel : UserControl
{
	public MyWorkPanel() => InitializeComponent();

	private async void OnRefreshClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is DashboardViewModel viewModel) await viewModel.RefreshMyWorkAsync();
	}

	private void OnFilterClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is not DashboardViewModel viewModel ||
			sender is not ToggleButton { Tag: string filter } toggle ||
			!Enum.TryParse<MyWorkQuickFilter>(filter, out var parsed)) return;
		viewModel.MyWorkFilter = parsed;
		toggle.IsChecked = true;
	}

	private async void OnOpenClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: MyWorkItem item }) await OpenAsync(item);
	}

	private async void OnItemsDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (sender is DataGrid { SelectedItem: MyWorkItem item }) await OpenAsync(item);
	}

	private async Task OpenAsync(MyWorkItem item)
	{
		if (Window.GetWindow(this)?.DataContext is MainViewModel viewModel)
			await viewModel.OpenMyWorkItemAsync(item);
	}
}
