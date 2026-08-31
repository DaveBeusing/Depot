// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.ViewModels.Administration;

namespace Depot.Views.Administration;

public partial class UserSessionsView : UserControl
{
	public UserSessionsView()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (DataContext is UserSessionsViewModel viewModel) viewModel.StartPolling();
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (DataContext is UserSessionsViewModel viewModel) viewModel.StopPolling();
	}
}
