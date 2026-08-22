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
	private readonly SalesDocumentService _documents = new();

	public SalesShippingView() => InitializeComponent();

	private void OnReturnReceiptClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is not ShippingViewModel page || page.Workspace.SelectedCustomerReturn is null || page.Workspace.SelectedShipment is null) return;
		var dialog = new SaveFileDialog
		{
			Title = "Save customer return receipt",
			Filter = "PDF document (*.pdf)|*.pdf",
			DefaultExt = ".pdf",
			FileName = $"{page.Workspace.SelectedCustomerReturn.ReturnNumber}-return-receipt.pdf"
		};
		if (dialog.ShowDialog() != true) return;
		_documents.CreateCustomerReturnReceipt(dialog.FileName, page.Workspace.SelectedCustomerReturn, page.Workspace.SelectedShipment);
	}
}
