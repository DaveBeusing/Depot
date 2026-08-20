// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Services;
using Depot.ViewModels;

namespace Depot.Views;

public partial class SalesQuotesView : UserControl
{
	public SalesQuotesView() => InitializeComponent();

	private void OnEmailQuoteClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is not SalesQuotesViewModel viewModel || viewModel.Draft.Id <= 0) return;
		var pdfPath = Path.Combine(Path.GetTempPath(), $"{viewModel.Draft.QuoteNumber}-{Guid.NewGuid():N}.pdf");
		SalesCommercialContext.Documents.CreateQuote(pdfPath, viewModel.Draft);
		var draftPath = SalesCommercialContext.Email.CreateDraft(
			pdfPath,
			viewModel.SelectedContact?.Email ?? viewModel.SelectedCustomer?.Email,
			$"Quote {viewModel.Draft.QuoteNumber}",
			$"Please find quote {viewModel.Draft.QuoteNumber} attached.\n\nValid until: {viewModel.Draft.ValidUntil:d}");
		SalesCommercialContext.Email.OpenDraft(draftPath);
	}
}
