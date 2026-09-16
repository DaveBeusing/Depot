// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows.Input;

using Depot.Services;
using Depot.Services.Help;
using Depot.ViewModels;
using Depot.Views;

namespace Depot;

public partial class MainWindow
{
	private GlobalSearchService? _globalSearchService;
	private IHelpService? _globalSearchHelpService;

	public MainWindow(
		IAuthorizationService authorization,
		ApplicationInformationService applicationInformation,
		GlobalSearchService globalSearchService,
		IHelpService helpService)
		: this(authorization, applicationInformation)
	{
		_globalSearchService = globalSearchService ?? throw new ArgumentNullException(nameof(globalSearchService));
		_globalSearchHelpService = helpService ?? throw new ArgumentNullException(nameof(helpService));
		PreviewKeyDown += OnGlobalSearchPreviewKeyDown;
	}

	private void OnGlobalSearchPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Handled || Keyboard.Modifiers != ModifierKeys.Control) return;
		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (key != Key.K) return;
		OpenGlobalSearchPalette();
		e.Handled = true;
	}

	private void OpenGlobalSearchPalette()
	{
		if (DataContext is not MainViewModel viewModel || _globalSearchService is null || _globalSearchHelpService is null) return;
		var palette = new ShellPaletteWindow(
			viewModel,
			() => viewModel.OpenNotificationsAsync(),
			() => viewModel.OpenHelpAsync(),
			() => viewModel.NavigateAsync(_currentUserNavigationItem),
			_recentQuickOpenEntries,
			_globalSearchService,
			_globalSearchHelpService)
		{
			Owner = this
		};
		palette.ShowDialog();
	}
}
