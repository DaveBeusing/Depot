// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Models;
using Depot.Services;
using Depot.ViewModels;

namespace Depot.Views;

public partial class PurchaseOrderApprovalsView : UserControl
{
	private readonly TabItem _salesApprovalsTab;
	private readonly ContentControl _salesApprovalsHost;
	private SalesApprovalsViewModel? _salesApprovals;

	public PurchaseOrderApprovalsView()
	{
		InitializeComponent();

		var purchaseContent = Content as UIElement ?? throw new InvalidOperationException("Approval content was not initialized.");
		Content = null;
		_salesApprovalsHost = new ContentControl();
		_salesApprovalsTab = new TabItem { Header = "Sales Approvals", Content = _salesApprovalsHost };
		var tabs = new TabControl { Style = (Style)FindResource("AppTabControlStyle") };
		tabs.Items.Add(new TabItem { Header = "Purchase Approvals", Content = purchaseContent });
		tabs.Items.Add(_salesApprovalsTab);
		Content = tabs;

		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		var canViewSalesApprovals = SalesCommercialContext.IsUiConfigured &&
			SalesCommercialContext.Authorization.HasPermission(ApplicationPermission.SalesOrdersApprove);
		_salesApprovalsTab.Visibility = canViewSalesApprovals ? Visibility.Visible : Visibility.Collapsed;
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
		_salesApprovalsHost.Content = _salesApprovals;
		await _salesApprovals.LoadAsync();
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_salesApprovals?.Dispose();
		_salesApprovals = null;
		_salesApprovalsHost.Content = null;
	}
}
