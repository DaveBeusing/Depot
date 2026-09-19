// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class DashboardView : UserControl
{
	private CancellationTokenSource? _refreshCancellation;

	public DashboardView()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (DataContext is not DashboardViewModel viewModel || viewModel.IsBusy) return;
		_refreshCancellation?.Cancel();
		_refreshCancellation?.Dispose();
		_refreshCancellation = new CancellationTokenSource();
		try { await viewModel.LoadAsync(_refreshCancellation.Token); }
		catch (OperationCanceledException) when (_refreshCancellation.IsCancellationRequested) { }
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_refreshCancellation?.Cancel();
		_refreshCancellation?.Dispose();
		_refreshCancellation = null;
	}

	private async void OnKpiClick(object sender, RoutedEventArgs e)
	{
		if (sender is not FrameworkElement { Tag: string routeId } ||
			Window.GetWindow(this)?.DataContext is not MainViewModel viewModel) return;
		await viewModel.NavigateToRouteAsync(new ShellRoute(routeId));
	}

	private async void OnQuickActionClick(object sender, RoutedEventArgs e)
	{
		if (sender is not FrameworkElement { DataContext: CommercialRoleQuickAction action } ||
			Window.GetWindow(this)?.DataContext is not MainViewModel viewModel) return;
		await viewModel.ExecuteCommercialRoleActionAsync(action);
	}

	private async void OnRecentMovementDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (Window.GetWindow(this)?.DataContext is MainViewModel viewModel)
			await viewModel.NavigateToRouteAsync(new ShellRoute("inventory.movements"));
	}
}
