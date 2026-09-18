// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class CommercialRoleCenterView : UserControl
{
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
