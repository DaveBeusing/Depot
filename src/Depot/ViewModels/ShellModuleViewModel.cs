// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.ViewModels.Administration;

namespace Depot.ViewModels;

public sealed class ShellModuleViewModel : BaseViewModel, IDisposable
{
	private readonly List<SecondaryNavigationItem> _ownedPages;
	private SecondaryNavigationItem? _selectedPage;
	private bool _administrationPagesExpanded;

	public ShellModuleViewModel(string title, string subtitle, IEnumerable<SecondaryNavigationItem> pages)
	{
		Title = title;
		Subtitle = subtitle;
		_ownedPages = pages.ToList();
		Pages = new ObservableCollection<SecondaryNavigationItem>(_ownedPages);
		_selectedPage = Pages.FirstOrDefault();
	}

	public event EventHandler? NavigationRequested;
	public string Title { get; }
	public string Subtitle { get; }
	public ObservableCollection<SecondaryNavigationItem> Pages { get; }
	public bool HasMultiplePages => Pages.Count > 1;
	public BaseViewModel? CurrentViewModel => SelectedPage?.Content;
	public Func<BaseViewModel?, bool>? NavigationGuard { get; set; }

	public SecondaryNavigationItem? SelectedPage
	{
		get => _selectedPage;
		set
		{
			if (_selectedPage == value || value is null) return;
			if (!SetSelectedPage(value)) return;
			NavigationRequested?.Invoke(this, EventArgs.Empty);
		}
	}

	public bool SetSelectedPage(SecondaryNavigationItem page)
	{
		if (_selectedPage == page) return true;
		if (_selectedPage is not null && NavigationGuard?.Invoke(CurrentViewModel) == false)
		{
			OnPropertyChanged(nameof(SelectedPage));
			return false;
		}
		_selectedPage = page;
		OnPropertyChanged(nameof(SelectedPage));
		OnPropertyChanged(nameof(CurrentViewModel));
		return true;
	}

	public async Task ActivateAsync(CancellationToken cancellationToken = default)
	{
		if (SelectedPage is null) return;
		await SelectedPage.ActivateAsync(cancellationToken);
		ExpandAdministrationPagesIfNeeded();
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		if (SelectedPage is null) return;
		await SelectedPage.RefreshAsync(cancellationToken);
		ExpandAdministrationPagesIfNeeded();
	}

	private void ExpandAdministrationPagesIfNeeded()
	{
		if (_administrationPagesExpanded || CurrentViewModel is not AdministrationViewModel administration) return;
		if (administration.NavigationItems.Count == 0) return;

		administration.NavigationGuard = NavigationGuard;
		var selectedSection = administration.SelectedNavigationItem?.Section;
		var proxyPages = administration.NavigationItems
			.Select(item => new SecondaryNavigationItem(
				item.Name,
				() => administration,
				(_, token) => administration.NavigateToAsync((AdministrationSection)item.Section, token),
				item.HelpTopicId,
				ownsContent: false,
				alwaysActivate: true))
			.ToList();

		Pages.Clear();
		foreach (var page in proxyPages) Pages.Add(page);

		_selectedPage = selectedSection is null
			? Pages.FirstOrDefault()
			: Pages.FirstOrDefault(page =>
				administration.NavigationItems.Any(item =>
					item.Name == page.Name && Equals(item.Section, selectedSection))) ?? Pages.FirstOrDefault();

		_administrationPagesExpanded = true;
		OnPropertyChanged(nameof(Pages));
		OnPropertyChanged(nameof(SelectedPage));
		OnPropertyChanged(nameof(CurrentViewModel));
		OnPropertyChanged(nameof(HasMultiplePages));
	}

	public void Dispose()
	{
		foreach (var page in Pages) page.Dispose();
		foreach (var page in _ownedPages.Where(page => !Pages.Contains(page))) page.Dispose();
	}
}
