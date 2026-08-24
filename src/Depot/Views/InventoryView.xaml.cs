// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.ViewModels;

namespace Depot.Views;

public partial class InventoryView : UserControl
{
	private CancellationTokenSource? _refreshCancellation;

	public InventoryView()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (DataContext is not InventoryViewModel viewModel || viewModel.IsBusy) return;
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
}
