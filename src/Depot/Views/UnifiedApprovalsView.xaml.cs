// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Models;
using Depot.Services;
using Depot.ViewModels;

namespace Depot.Views;

public partial class UnifiedApprovalsView : UserControl
{
	private SalesApprovalsViewModel? _salesApprovals;

	public UnifiedApprovalsView()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		var canViewSalesApprovals = SalesCommercialContext.IsUiConfigured &&
			SalesCommercialContext.Authorization.HasPermission(ApplicationPermission.SalesOrdersApprove);
		SalesApprovalsTab.Visibility = canViewSalesApprovals ? Visibility.Visible : Visibility.Collapsed;
		if (!canViewSalesApprovals || _salesApprovals is not null) return;

		var workspace = new SalesViewModel(
			SalesCommercialContext.Customers,
			SalesCommercialContext.Orders,
			SalesCommercialContext.Shipments,
			SalesCommercialContext.Invoices,
			SalesCommercialContext.Items,
			SalesCommercialContext.Authorization,
			SalesCommercialContext.FileDialogs,
			SalesCommercialContext.Documents);
		_salesApprovals = new SalesApprovalsViewModel(workspace);
		SalesApprovalsHost.Content = _salesApprovals;
		await _salesApprovals.LoadAsync();
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_salesApprovals?.Dispose();
		_salesApprovals = null;
		SalesApprovalsHost.Content = null;
	}
}
