// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.ViewModels.Shared;
using Depot.ViewModels.MasterData;


namespace Depot.ViewModels.Administration;

/// <summary>
/// Provides navigation and content selection for the administration area.
/// </summary>
public sealed class AdministrationViewModel
	: BaseViewModel
{
	private readonly ImportViewModel _importViewModel;
	private readonly MasterDataViewModel _masterDataViewModel;

	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public AdministrationViewModel(
		ImportViewModel importViewModel)
	{
		_importViewModel = importViewModel;
		_masterDataViewModel = new MasterDataViewModel();

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
		if (SelectedNavigationItem is null)
		{
			CurrentViewModel = null;
			return;
		}

		CurrentViewModel =
			(AdministrationSection)SelectedNavigationItem.Section switch
			{
				AdministrationSection.Import =>
					_importViewModel,

				AdministrationSection.MasterData =>
					_masterDataViewModel,

				AdministrationSection.Users =>
					new PlaceholderViewModel(
						"Users",
						"User management will be available in a future release."),

				AdministrationSection.Database =>
					new PlaceholderViewModel(
						"Database",
						"Database management will be available in a future release."),

				AdministrationSection.Settings =>
					new PlaceholderViewModel(
						"Settings",
						"Application settings will be available in a future release."),

				_ =>
					new PlaceholderViewModel(
						"Administration",
						"This administration section is currently under development.")
			};
	}
}