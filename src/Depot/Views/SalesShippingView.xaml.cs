// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Services;
using Depot.ViewModels;

using Microsoft.Win32;

namespace Depot.Views;

public partial class SalesShippingView : UserControl
{
	public SalesShippingView() => InitializeComponent();

	private void OnReturnReceiptClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is not SalesViewModel viewModel || viewModel.SelectedCustomerReturn is null || viewModel.SelectedShipment is null) return;
		var dialog = new SaveFileDialog
		{
			Title = "Save customer return receipt",
			Filter = "PDF document (*.pdf)|*.pdf",
			DefaultExt = ".pdf",
			FileName = $"{viewModel.SelectedCustomerReturn.ReturnNumber}-return-receipt.pdf"
		};
		if (dialog.ShowDialog() != true) return;
		new SalesDocumentService().CreateCustomerReturnReceipt(dialog.FileName, viewModel.SelectedCustomerReturn, viewModel.SelectedShipment);
	}
}
