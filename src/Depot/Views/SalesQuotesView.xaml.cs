// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Services;
using Depot.ViewModels;

namespace Depot.Views;

public partial class SalesQuotesView : UserControl
{
	private readonly SalesDocumentService _documents = new();
	private readonly SalesDocumentEmailService _email = new();

	public SalesQuotesView() => InitializeComponent();

	private void OnEmailQuoteClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is not SalesQuotesViewModel viewModel || viewModel.Draft.Id <= 0) return;
		var pdfPath = Path.Combine(Path.GetTempPath(), $"{viewModel.Draft.QuoteNumber}-{Guid.NewGuid():N}.pdf");
		_documents.CreateQuote(pdfPath, viewModel.Draft);
		var draftPath = _email.CreateDraft(
			pdfPath,
			viewModel.SelectedContact?.Email ?? viewModel.SelectedCustomer?.Email,
			$"Quote {viewModel.Draft.QuoteNumber}",
			$"Please find quote {viewModel.Draft.QuoteNumber} attached.\n\nValid until: {viewModel.Draft.ValidUntil:d}");
		_email.OpenDraft(draftPath);
	}
}
