// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;
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
		_ownedPages = title == "Sales" ? NormalizeSalesPages(pages) : pages.ToList();
		Pages = new ObservableCollection<SecondaryNavigationItem>(_ownedPages);
		_selectedPage = Pages.FirstOrDefault(page => page.IsVisible) ?? Pages.FirstOrDefault();
		_selectedPage?.Activate?.Invoke();
	}

	public event EventHandler? NavigationRequested;
	public string Title { get; }
	public string Subtitle { get; }
	public ObservableCollection<SecondaryNavigationItem> Pages { get; }
	public bool HasMultiplePages => Pages.Count(page => page.IsVisible) > 1;
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
		page.Activate?.Invoke();
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

	private static List<SecondaryNavigationItem> NormalizeSalesPages(IEnumerable<SecondaryNavigationItem> pages)
	{
		var original = pages.ToList();
		var approvalPage = original.FirstOrDefault(page => page.Name == "Approvals");
		if (approvalPage is not null) approvalPage.IsVisible = false;

		var result = new List<SecondaryNavigationItem>();
		foreach (var page in original.Where(page => page.Name != "Approvals"))
		{
			result.Add(page);
			if (page.Name != "Overview" || !SalesCommercialContext.IsConfigured || !SalesCommercialContext.IsUiConfigured) continue;

			if (SalesCommercialContext.Quotes.CanView)
			{
				result.Add(new SecondaryNavigationItem(
					"Quotes",
					() => new SalesQuotesViewModel(
						SalesCommercialContext.Quotes,
						SalesCommercialContext.Pricing,
						SalesCommercialContext.Customers,
						SalesCommercialContext.Items,
						SalesCommercialContext.FileDialogs,
						SalesCommercialContext.Documents),
					(viewModel, token) => ((SalesQuotesViewModel)viewModel).LoadAsync(token),
					"sales.quotes"));
			}

			if (SalesCommercialContext.Pricing.CanView)
			{
				result.Add(new SecondaryNavigationItem(
					"Pricing",
					() => new SalesPricingViewModel(
						SalesCommercialContext.Pricing,
						SalesCommercialContext.Customers,
						SalesCommercialContext.Items),
					(viewModel, token) => ((SalesPricingViewModel)viewModel).LoadAsync(token),
					"sales.pricing"));
			}
		}

		if (approvalPage is not null) result.Add(approvalPage);
		return result;
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
		_selectedPage?.Activate?.Invoke();

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
