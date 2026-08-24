// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class SalesOrdersView : UserControl
{
	public SalesOrdersView()
	{
		InitializeComponent();
		AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnSelectionChanged));
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (DataContext is not SalesOrdersViewModel viewModel || e.OriginalSource is not ListBox listBox) return;
		if (!ReferenceEquals(listBox.ItemsSource, viewModel.Workspace.Orders)) return;

		var selectedOrder = listBox.SelectedItem as SalesOrder;
		if (!ReferenceEquals(viewModel.Workspace.SelectedOrder, selectedOrder)) viewModel.Workspace.SelectedOrder = selectedOrder;
		if (!ReferenceEquals(viewModel.SelectedOrder, selectedOrder)) viewModel.SelectedOrder = selectedOrder;
	}
}
