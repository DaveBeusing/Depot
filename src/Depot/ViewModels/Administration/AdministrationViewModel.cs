// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Depot.ViewModels.Administration;

public sealed class AdministrationViewModel
	: BaseViewModel
{
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public AdministrationViewModel()
	{
		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Import",
				Section = AdministrationSection.Import
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Master Data",
				Section = AdministrationSection.MasterData
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Users",
				Section = AdministrationSection.Users
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Database",
				Section = AdministrationSection.Database
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Settings",
				Section = AdministrationSection.Settings
			});

		SelectedNavigationItem =
			NavigationItems[0];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;

		set
		{
			_selectedNavigationItem = value;

			OnPropertyChanged();

			UpdateCurrentViewModel();
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;

		private set
		{
			_currentViewModel = value;

			OnPropertyChanged();
		}
	}

	private void UpdateCurrentViewModel()
	{
		CurrentViewModel = null;
	}
}