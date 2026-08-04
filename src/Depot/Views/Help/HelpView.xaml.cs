// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

using Depot.ViewModels.Help;

namespace Depot.Views.Help;

public partial class HelpView : UserControl
{
	public HelpView()
	{
		InitializeComponent();
		DocumentViewer.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));
	}

	private async void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
	{
		if (DataContext is HelpViewModel viewModel && string.Equals(e.Uri.Scheme, "topic", StringComparison.OrdinalIgnoreCase))
			await viewModel.NavigateToTopicAsync(e.Uri.OriginalString["topic:".Length..]);
		e.Handled = true;
	}
}
