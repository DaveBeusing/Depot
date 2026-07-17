// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.ViewModels.Shared;
using Depot.ViewModels.Users;
using Depot.ViewModels.MasterData;
using Depot.Services;


namespace Depot.ViewModels.Administration;

/// <summary>
/// Provides navigation and content selection for the administration area.
/// </summary>
public sealed class AdministrationViewModel
	: BaseViewModel
{
	private readonly ImportViewModel _importViewModel;
	private readonly MasterDataViewModel _masterDataViewModel;
	private readonly UserViewModel _userViewModel;
	private readonly DatabaseSettingsViewModel _databaseSettingsViewModel;
	private readonly AboutViewModel _aboutViewModel;

	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public AdministrationViewModel(
		ImportViewModel importViewModel,
		PurposeService purposeService,
		ReasonCodeService reasonCodeService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		UserService userService,
		SettingsService settingsService,
		ConnectionStatusService connectionStatusService,
		DatabaseConnectionTester databaseConnectionTester,
		DatabaseManagementService databaseManagementService,
		IFileDialogService fileDialogService,
		ApplicationInformationService applicationInformationService)
	{
		_importViewModel = importViewModel;
		_masterDataViewModel = new MasterDataViewModel(purposeService, reasonCodeService, warehouseService, storageLocationService);
		_userViewModel =
			new UserViewModel(
				userService);
		_databaseSettingsViewModel =
			new DatabaseSettingsViewModel(
				settingsService,
				connectionStatusService,
				databaseConnectionTester,
				databaseManagementService,
				fileDialogService);
		_aboutViewModel = new AboutViewModel(applicationInformationService);

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

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "About",
				Section = AdministrationSection.About
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
					_userViewModel,

				AdministrationSection.Database =>
					_databaseSettingsViewModel,

				AdministrationSection.Settings =>
					new PlaceholderViewModel(
						"Settings",
						"Application settings will be available in a future release."),

				AdministrationSection.About =>
					_aboutViewModel,

				_ =>
					new PlaceholderViewModel(
						"Administration",
						"This administration section is currently under development.")
			};

		_ = LoadCurrentViewModelAsync();
	}

	private Task LoadCurrentViewModelAsync() =>
		CurrentViewModel switch
		{
			MasterDataViewModel masterData => masterData.LoadAsync(),
			UserViewModel users => users.LoadUsersAsync(),
			DatabaseSettingsViewModel database => database.LoadAsync(),
			_ => Task.CompletedTask
		};
}
