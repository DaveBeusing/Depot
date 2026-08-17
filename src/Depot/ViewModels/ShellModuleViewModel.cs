// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;

using Depot.Models;
using Depot.ViewModels.Administration;

namespace Depot.ViewModels;

public sealed class ShellModuleViewModel : BaseViewModel, IDisposable
{
	private SecondaryNavigationItem? _selectedPage;
	private AdministrationViewModel? _observedAdministration;

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
	public bool HasMultiplePages => ContextPages.Cast<object>().Skip(1).Any();
	public BaseViewModel? CurrentViewModel => SelectedPage?.Content;

	public IEnumerable ContextPages
	{
		get
		{
			var administration = CurrentViewModel as AdministrationViewModel;
			ObserveAdministration(administration);
			return administration?.NavigationItems ?? Pages;
		}
	}

	public object? ContextSelectedPage
	{
		get
		{
			var administration = CurrentViewModel as AdministrationViewModel;
			ObserveAdministration(administration);
			return administration?.SelectedNavigationItem ?? SelectedPage;
		}
		set
		{
			if (CurrentViewModel is AdministrationViewModel administration && value is NavigationItem administrationPage)
			{
				administration.SelectedNavigationItem = administrationPage;
				OnPropertyChanged();
				return;
			}

			if (value is SecondaryNavigationItem page)
				SelectedPage = page;
		}
	}

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
		DetachAdministrationObserver();
		_selectedPage = page;
		OnPropertyChanged(nameof(SelectedPage));
		OnPropertyChanged(nameof(CurrentViewModel));
		OnPropertyChanged(nameof(ContextPages));
		OnPropertyChanged(nameof(ContextSelectedPage));
		OnPropertyChanged(nameof(HasMultiplePages));
	}

	public Task ActivateAsync(CancellationToken cancellationToken = default) =>
		SelectedPage?.ActivateAsync(cancellationToken) ?? Task.CompletedTask;

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		SelectedPage?.RefreshAsync(cancellationToken) ?? Task.CompletedTask;

	private void ObserveAdministration(AdministrationViewModel? administration)
	{
		if (ReferenceEquals(_observedAdministration, administration)) return;
		DetachAdministrationObserver();
		_observedAdministration = administration;
		if (_observedAdministration is not null)
			_observedAdministration.PropertyChanged += OnAdministrationPropertyChanged;
	}

	private void OnAdministrationPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(AdministrationViewModel.SelectedNavigationItem) or nameof(AdministrationViewModel.CurrentViewModel))
			OnPropertyChanged(nameof(ContextSelectedPage));
	}

	private void DetachAdministrationObserver()
	{
		if (_observedAdministration is null) return;
		_observedAdministration.PropertyChanged -= OnAdministrationPropertyChanged;
		_observedAdministration = null;
	}

	public void Dispose()
	{
		DetachAdministrationObserver();
		foreach (var page in Pages) page.Dispose();
	}
}
