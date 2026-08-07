// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Depot.ViewModels;

public sealed class ShellModuleViewModel : BaseViewModel, IDisposable
{
	private SecondaryNavigationItem? _selectedPage;

	public ShellModuleViewModel(string title, string subtitle, IEnumerable<SecondaryNavigationItem> pages)
	{
		Title = title;
		Subtitle = subtitle;
		Pages = new ObservableCollection<SecondaryNavigationItem>(pages);
		_selectedPage = Pages.FirstOrDefault();
	}

	public event EventHandler? NavigationRequested;
	public string Title { get; }
	public string Subtitle { get; }
	public ObservableCollection<SecondaryNavigationItem> Pages { get; }
	public bool HasMultiplePages => Pages.Count > 1;
	public BaseViewModel? CurrentViewModel => SelectedPage?.Content;

	public SecondaryNavigationItem? SelectedPage
	{
		get => _selectedPage;
		set
		{
			if (_selectedPage == value || value is null) return;
			SetSelectedPage(value);
			NavigationRequested?.Invoke(this, EventArgs.Empty);
		}
	}

	public void SetSelectedPage(SecondaryNavigationItem page)
	{
		if (_selectedPage == page) return;
		_selectedPage = page;
		OnPropertyChanged(nameof(SelectedPage));
		OnPropertyChanged(nameof(CurrentViewModel));
	}

	public Task ActivateAsync(CancellationToken cancellationToken = default) =>
		SelectedPage?.ActivateAsync(cancellationToken) ?? Task.CompletedTask;

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		SelectedPage?.RefreshAsync(cancellationToken) ?? Task.CompletedTask;

	public void Dispose()
	{
		foreach (var page in Pages) page.Dispose();
	}
}
